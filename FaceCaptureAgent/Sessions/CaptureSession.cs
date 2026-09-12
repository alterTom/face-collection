using System.Text.Json;
using FaceCaptureAgent.AutoCapture;
using FaceCaptureAgent.Camera;
using FaceCaptureAgent.Configuration;
using FaceCaptureAgent.Protocol;

namespace FaceCaptureAgent.Sessions;

/// <summary>管理单个连接的命令、预览和摄像头租约；自动拍照逻辑见同名 AutoCapture 分部文件。</summary>
public sealed partial class CaptureSession : IAsyncDisposable
{
    private readonly Guid _ownerId;
    private readonly ICameraService _camera;
    private readonly CameraLeaseManager _leaseManager;
    private readonly AgentOptions _options;
    private readonly Func<ReadOnlyMemory<byte>, CancellationToken, Task> _binarySender;
    // 命令锁保护状态切换；设备锁串行化预览、手动抓拍和自动检测的取帧操作。
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private readonly SemaphoreSlim _cameraGate = new(1, 1);
    private readonly object _disposeGate = new();
    private IDisposable? _cameraLease;
    private CancellationTokenSource? _previewCancellation;
    private Task? _previewTask;
    private Task? _disposeTask;

    public CaptureSession(
        Guid ownerId,
        ICameraService camera,
        CameraLeaseManager leaseManager,
        AgentOptions options,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> binarySender,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task>? eventSender = null,
        Func<IFaceDetector>? detectorFactory = null)
    {
        _ownerId = ownerId;
        _camera = camera;
        _leaseManager = leaseManager;
        _options = options;
        _binarySender = binarySender;
        _eventSender = eventSender ?? ((_, _) => Task.CompletedTask);
        _detectorFactory = detectorFactory ?? (() => new YuNetFaceDetector());
    }

    public SessionState State { get; private set; } = SessionState.Connected;

    public async Task<ReadOnlyMemory<byte>> HandleAsync(
        ClientMessage message,
        CancellationToken cancellationToken)
    {
        // 检测可能正持有命令锁，必须先取消再等锁；先校验参数，避免无效命令中断有效轮次。
        if (State is SessionState.CameraOpen or SessionState.Previewing)
        {
            if (message.Type == "camera.setCaptureMode")
            {
                _ = AutoCaptureOptions.Parse(message.Payload, _autoOptions);
                CancelAutoCapture();
            }
            else if (message.Type == "camera.close" || message.Type == "capture.rearm" && _autoOptions.CaptureMode == "auto")
                CancelAutoCapture();
        }
        await _commandGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (State == SessionState.Closed)
            {
                return Error(message.RequestId, ErrorCodes.InvalidState, "The session is closed.");
            }

            try
            {
                return message.Type switch
                {
                    "system.info" => SystemInfo(message),
                    "device.list" => await ListDevicesAsync(message, cancellationToken).ConfigureAwait(false),
                    "camera.open" => await OpenCameraAsync(message, cancellationToken).ConfigureAwait(false),
                    "camera.setCaptureMode" => await SetCaptureModeAsync(message).ConfigureAwait(false),
                    "capture.rearm" => await RearmAsync(message).ConfigureAwait(false),
                    "preview.start" => StartPreview(message),
                    "preview.stop" => await StopPreviewCommandAsync(message).ConfigureAwait(false),
                    "capture" => await CaptureAsync(message, cancellationToken).ConfigureAwait(false),
                    "camera.close" => await CloseCameraCommandAsync(message).ConfigureAwait(false),
                    "ping" => ResponseEnvelope.Success("pong", message.RequestId, new { }),
                    _ => Error(message.RequestId, ErrorCodes.InvalidMessage, "Unsupported command type.")
                };
            }
            catch (CameraException exception)
            {
                if (_cameraLease is not null)
                {
                    await StopAutoCaptureAsync().ConfigureAwait(false);
                    await StopPreviewAsync().ConfigureAwait(false);
                    await ReleaseCameraAfterFailureAsync().ConfigureAwait(false);
                    State = SessionState.Connected;
                }

                return Error(message.RequestId, exception.Code, exception.Message, exception.Retryable);
            }
        }
        finally
        {
            _commandGate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeGate)
        {
            _disposeTask ??= DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private static ReadOnlyMemory<byte> SystemInfo(ClientMessage message) =>
        ResponseEnvelope.Success(
            "system.info.result",
            message.RequestId,
            new { agentVersion = "1.1.0", protocolVersion = "1.0", platform = "win-x64", capabilities = new[] { "auto-capture" } });

    private async Task<ReadOnlyMemory<byte>> ListDevicesAsync(
        ClientMessage message,
        CancellationToken cancellationToken)
    {
        var devices = await _camera.ListDevicesAsync(cancellationToken).ConfigureAwait(false);
        return ResponseEnvelope.Success("device.list.result", message.RequestId, new { devices });
    }

    private async Task<ReadOnlyMemory<byte>> OpenCameraAsync(
        ClientMessage message,
        CancellationToken cancellationToken)
    {
        if (State != SessionState.Connected)
        {
            return Error(message.RequestId, ErrorCodes.InvalidState, "The camera is already open.");
        }

        var autoOptions = AutoCaptureOptions.Parse(message.Payload);
        var request = new CameraOpenRequest(
            OptionalString(message.Payload, "deviceId") ?? _options.CameraIndex.ToString(),
            OptionalPositiveInt(message.Payload, "width", _options.CaptureWidth),
            OptionalPositiveInt(message.Payload, "height", _options.CaptureHeight),
            OptionalPositiveInt(message.Payload, "fps", 15));

        _cameraLease = _leaseManager.TryAcquire(_ownerId);
        if (_cameraLease is null)
        {
            return Error(message.RequestId, ErrorCodes.CameraBusy, "The camera is in use by another session.", true);
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(_options.CameraTimeoutSeconds));
            var result = await _camera.OpenAsync(request, timeout.Token).ConfigureAwait(false);
            State = SessionState.CameraOpen;
            PrepareAutoRound(autoOptions);
            return ResponseEnvelope.Success("camera.open.result", message.RequestId,
                new { result.Width, result.Height, result.Fps, _autoOptions.CaptureMode, _autoOptions.StableDurationMs, roundId = _roundId });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await ReleaseCameraAfterFailureAsync().ConfigureAwait(false);
            return Error(message.RequestId, ErrorCodes.CameraOpenFailed, "Opening the camera timed out.", true);
        }
        catch (Exception exception) when (exception is not CameraException)
        {
            await ReleaseCameraAfterFailureAsync().ConfigureAwait(false);
            return Error(message.RequestId, ErrorCodes.CameraOpenFailed, "The camera could not be opened.", true);
        }
        catch
        {
            await ReleaseCameraAfterFailureAsync().ConfigureAwait(false);
            throw;
        }
    }

    private ReadOnlyMemory<byte> StartPreview(ClientMessage message)
    {
        if (State != SessionState.CameraOpen)
        {
            return Error(message.RequestId, ErrorCodes.InvalidState, "Preview requires an open camera.");
        }

        var requestedFps = OptionalPositiveInt(message.Payload, "fps", _options.PreviewFps);
        var fps = Math.Min(requestedFps, _options.PreviewFps);
        _previewCancellation = new CancellationTokenSource();
        State = SessionState.Previewing;
        _previewTask = PreviewLoopAsync(fps, _previewCancellation.Token);
        return ResponseEnvelope.Success("preview.start.result", message.RequestId, new { fps });
    }

    private async Task<ReadOnlyMemory<byte>> StopPreviewCommandAsync(ClientMessage message)
    {
        if (State != SessionState.Previewing)
        {
            return Error(message.RequestId, ErrorCodes.InvalidState, "Preview is not running.");
        }

        await StopPreviewAsync().ConfigureAwait(false);
        State = SessionState.CameraOpen;
        return ResponseEnvelope.Success("preview.stop.result", message.RequestId, new { });
    }

    private async Task<ReadOnlyMemory<byte>> CaptureAsync(
        ClientMessage message,
        CancellationToken cancellationToken)
    {
        if (State is not (SessionState.CameraOpen or SessionState.Previewing))
        {
            return Error(message.RequestId, ErrorCodes.InvalidState, "Capture requires an open camera.");
        }

        if (_autoOptions.CaptureMode == "auto")
            return Error(message.RequestId, ErrorCodes.InvalidState, "Switch to manual mode before explicit capture.");

        var precedingState = State;
        State = SessionState.Capturing;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(_options.CameraTimeoutSeconds));
            var frame = await ReadFrameAsync(timeout.Token).ConfigureAwait(false);
            if (frame.Bytes.Length > _options.MaxImageBytes)
            {
                return Error(
                    message.RequestId,
                    ErrorCodes.ImageTooLarge,
                    "The captured JPEG exceeds the configured size limit.");
            }

            return ResponseEnvelope.Success(
                "capture.result",
                message.RequestId,
                new
                {
                    mimeType = "image/jpeg",
                    frame.Width,
                    frame.Height,
                    size = frame.Bytes.Length,
                    frame.CapturedAt,
                    base64 = Convert.ToBase64String(frame.Bytes)
                });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Error(message.RequestId, ErrorCodes.CaptureTimeout, "Waiting for a camera frame timed out.", true);
        }
        finally
        {
            if (State != SessionState.Closed)
            {
                State = precedingState;
            }
        }
    }

    private async Task<ReadOnlyMemory<byte>> CloseCameraCommandAsync(ClientMessage message)
    {
        if (State is not (SessionState.CameraOpen or SessionState.Previewing))
        {
            return Error(message.RequestId, ErrorCodes.InvalidState, "The camera is not open.");
        }

        await StopAutoCaptureAsync().ConfigureAwait(false);
        await StopPreviewAsync().ConfigureAwait(false);
        await CloseOwnedCameraAsync(CancellationToken.None).ConfigureAwait(false);
        State = SessionState.Connected;
        return ResponseEnvelope.Success("camera.close.result", message.RequestId, new { });
    }

    private async Task PreviewLoopAsync(int fps, CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1d / fps));
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                var frame = await ReadFrameAsync(cancellationToken).ConfigureAwait(false);
                await _binarySender(frame.Bytes, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            CancelAutoCapture();
            try
            {
                await _commandGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await StopAutoCaptureAsync().ConfigureAwait(false);
                    await CloseCameraAfterAutoFailureAsync().ConfigureAwait(false);
                    await SendAutoErrorAsync(_roundId,
                        exception is CameraException cameraError ? cameraError.Code : ErrorCodes.CameraDisconnected,
                        true, cancellationToken).ConfigureAwait(false);
                }
                finally { _commandGate.Release(); }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        }
    }

    // 所有取帧入口共用设备锁，避免同时读取同一个摄像头句柄。
    private async Task<JpegFrame> ReadFrameAsync(CancellationToken cancellationToken)
    {
        await _cameraGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await _camera.ReadJpegFrameAsync(_options.JpegQuality, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _cameraGate.Release();
        }
    }

    private async Task StopPreviewAsync()
    {
        var cancellation = _previewCancellation;
        var task = _previewTask;
        _previewCancellation = null;
        _previewTask = null;

        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
        try
        {
            if (task is not null)
            {
                await task.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    // 设备关闭即使抛出异常，也必须在 finally 中归还全局租约。
    private async Task CloseOwnedCameraAsync(CancellationToken cancellationToken)
    {
        var lease = _cameraLease;
        if (lease is null)
        {
            return;
        }

        _cameraLease = null;
        try
        {
            await _cameraGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _camera.CloseAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _cameraGate.Release();
            }
        }
        finally
        {
            lease.Dispose();
        }
    }

    private async Task ReleaseCameraAfterFailureAsync()
    {
        var lease = _cameraLease;
        _cameraLease = null;
        if (lease is null)
        {
            return;
        }

        try
        {
            try
            {
                await _camera.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
            }
        }
        finally
        {
            lease.Dispose();
        }
    }

    private async Task DisposeCoreAsync()
    {
        _lifetimeCancellation.Cancel();
        CancelAutoCapture();
        await _commandGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (State == SessionState.Closed)
            {
                return;
            }

            try
            {
                await StopAutoCaptureAsync().ConfigureAwait(false);
                await StopPreviewAsync().ConfigureAwait(false);
            }
            catch
            {
                // 预览任务失败也要继续释放摄像头和租约。
            }

            try
            {
                await CloseOwnedCameraAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                State = SessionState.Closed;
                await _camera.DisposeAsync().ConfigureAwait(false);
                _lifetimeCancellation.Dispose();
            }
        }
        finally
        {
            _commandGate.Release();
        }
    }

    private static string? OptionalString(JsonElement payload, string propertyName)
    {
        if (!payload.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new ProtocolException(ErrorCodes.InvalidMessage, $"{propertyName} must be a non-empty string.");
        }

        return value.GetString();
    }

    private static int OptionalPositiveInt(JsonElement payload, string propertyName, int defaultValue)
    {
        if (!payload.TryGetProperty(propertyName, out var value))
        {
            return defaultValue;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result) || result <= 0)
        {
            throw new ProtocolException(ErrorCodes.InvalidMessage, $"{propertyName} must be a positive integer.");
        }

        return result;
    }

    private static ReadOnlyMemory<byte> Error(
        string requestId,
        string code,
        string message,
        bool retryable = false) =>
        ResponseEnvelope.Error(requestId, code, message, retryable);
}
