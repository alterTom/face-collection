using System.Diagnostics;
using FaceCaptureAgent.AutoCapture;
using FaceCaptureAgent.Camera;
using FaceCaptureAgent.Protocol;

namespace FaceCaptureAgent.Sessions;

public sealed partial class CaptureSession
{
    private readonly Func<ReadOnlyMemory<byte>, CancellationToken, Task> _eventSender;
    private readonly Func<IFaceDetector> _detectorFactory;
    private AutoCaptureOptions _autoOptions = new();
    private string _roundId = Guid.NewGuid().ToString("N");
    private CancellationTokenSource? _autoCancellation;
    private Task? _autoTask;
    private bool _autoPending;
    private readonly CancellationTokenSource _lifetimeCancellation = new();

    // Called by the connection only after sending the corresponding control response.
    public void StartPendingAutoCapture()
    {
        if (!_autoPending) return;
        _autoPending = false;
        _autoCancellation = new();
        _autoTask = AutoCaptureLoopAsync(_roundId, _autoOptions.StableDurationMs, _autoCancellation.Token);
    }

    private void PrepareAutoRound(AutoCaptureOptions options)
    {
        _autoOptions = options;
        _roundId = Guid.NewGuid().ToString("N");
        _autoPending = options.CaptureMode == "auto";
    }

    private object AutoSettings() => new { _autoOptions.CaptureMode, _autoOptions.StableDurationMs, roundId = _roundId };

    private async Task<ReadOnlyMemory<byte>> SetCaptureModeAsync(ClientMessage message)
    {
        if (State is not (SessionState.CameraOpen or SessionState.Previewing))
            return Error(message.RequestId, ErrorCodes.InvalidState, "Mode changes require an open camera.");
        var options = AutoCaptureOptions.Parse(message.Payload, _autoOptions);
        await StopAutoCaptureAsync().ConfigureAwait(false);
        PrepareAutoRound(options);
        return ResponseEnvelope.Success("camera.setCaptureMode.result", message.RequestId, AutoSettings());
    }

    private async Task<ReadOnlyMemory<byte>> RearmAsync(ClientMessage message)
    {
        if (State is not (SessionState.CameraOpen or SessionState.Previewing) || _autoOptions.CaptureMode != "auto")
            return Error(message.RequestId, ErrorCodes.InvalidState, "Rearm requires an open camera in auto mode.");
        await StopAutoCaptureAsync().ConfigureAwait(false);
        PrepareAutoRound(_autoOptions);
        return ResponseEnvelope.Success("capture.rearm.result", message.RequestId, AutoSettings());
    }

    private async Task StopAutoCaptureAsync()
    {
        _autoPending = false;
        var cancellation = _autoCancellation;
        var task = _autoTask;
        _autoCancellation = null;
        _autoTask = null;
        if (cancellation is null) return;
        cancellation.Cancel();
        try
        {
            if (task is not null) await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        finally { cancellation.Dispose(); }
    }

    private void CancelAutoCapture()
    {
        try { _autoCancellation?.Cancel(); }
        catch (ObjectDisposedException) { /* A completed round was already cleaned up. */ }
    }

    private async Task CloseCameraAfterAutoFailureAsync()
    {
        try { await CloseOwnedCameraAsync(CancellationToken.None).ConfigureAwait(false); }
        catch { /* CloseOwnedCameraAsync releases the lease even if the device close fails. */ }
        finally { State = SessionState.Connected; }
    }

    private async Task AutoCaptureLoopAsync(string roundId, int stableDurationMs, CancellationToken token)
    {
        IFaceDetector? detector = null;
        var tracker = new FaceStabilityTracker(stableDurationMs);
        var clock = Stopwatch.StartNew();
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                await _commandGate.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    token.ThrowIfCancellationRequested();
                    if (State is not (SessionState.CameraOpen or SessionState.Previewing)) return;
                    JpegFrame frame;
                    try
                    {
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                        timeout.CancelAfter(TimeSpan.FromSeconds(_options.CameraTimeoutSeconds));
                        frame = await ReadFrameAsync(timeout.Token).ConfigureAwait(false);
                    }
                    catch (Exception exception) when (!token.IsCancellationRequested)
                    {
                        await StopPreviewIgnoringFailureAsync().ConfigureAwait(false);
                        await CloseCameraAfterAutoFailureAsync().ConfigureAwait(false);
                        var code = exception is CameraException cameraError ? cameraError.Code : ErrorCodes.CaptureTimeout;
                        await SendAutoErrorAsync(roundId, code, true, token).ConfigureAwait(false);
                        return;
                    }

                    IReadOnlyList<FaceBox> faces;
                    var frameTime = clock.ElapsedMilliseconds;
                    try
                    {
                        detector ??= _detectorFactory();
                        faces = await detector.DetectAsync(frame, token).ConfigureAwait(false);
                    }
                    catch (Exception) when (!token.IsCancellationRequested)
                    {
                        await SendAutoErrorAsync(roundId, "FACE_DETECTION_FAILED", false, token).ConfigureAwait(false);
                        return;
                    }
                    token.ThrowIfCancellationRequested();
                    // Do not capture stale frames if inference stalls.
                    var result = tracker.Update(clock.ElapsedMilliseconds - frameTime > 750 ? [] : faces, frameTime);
                    if (result.Ready)
                    {
                        if (frame.Bytes.Length > _options.MaxImageBytes)
                        {
                            await SendAutoErrorAsync(roundId, ErrorCodes.ImageTooLarge, false, token).ConfigureAwait(false);
                            return;
                        }
                        await _eventSender(ResponseEnvelope.Event("auto.capture", new
                        {
                            roundId, mimeType = "image/jpeg", frame.Width, frame.Height,
                            size = frame.Bytes.Length, frame.CapturedAt, base64 = Convert.ToBase64String(frame.Bytes)
                        }), token).ConfigureAwait(false);
                        await _eventSender(ResponseEnvelope.Event("auto.status", new { roundId, status = "complete" }), token).ConfigureAwait(false);
                        return;
                    }
                    await _eventSender(ResponseEnvelope.Event("auto.status", new { roundId, result.Status }), token).ConfigureAwait(false);
                }
                finally { _commandGate.Release(); }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch
        {
            // Transport shutdown or cleanup failure ends this round; disposal still releases ownership.
        }
        finally { detector?.Dispose(); }
    }

    private async Task SendAutoErrorAsync(string roundId, string code, bool cameraClosed, CancellationToken token)
    {
        // Camera faults belong to the camera lifetime, not the cancelled detection
        // round. Concurrent preview/auto faults must not cancel the only notification.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cameraClosed ? _lifetimeCancellation.Token : token);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        await _eventSender(ResponseEnvelope.Event("auto.error", new
        {
            roundId, code, message = cameraClosed ? "The camera could not return a frame." : "Automatic capture failed. Retry or switch to manual mode.",
            retryable = true, cameraClosed
        }), timeout.Token).ConfigureAwait(false);
    }

    private async Task StopPreviewIgnoringFailureAsync()
    {
        try { await StopPreviewAsync().ConfigureAwait(false); }
        catch { /* Camera cleanup must run even after a preview failure. */ }
    }
}
