using System.Globalization;
using FaceCaptureAgent.Configuration;
using FaceCaptureAgent.Protocol;
using OpenCvSharp;

namespace FaceCaptureAgent.Camera;

public sealed class OpenCvCameraService : ICameraService
{
    private readonly AgentOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private VideoCapture? _capture;
    private bool _disposed;

    public OpenCvCameraService(AgentOptions options)
    {
        _options = options;
    }

    public static IReadOnlyList<int> CandidateIndices(int defaultIndex)
    {
        var indices = Enumerable.Range(0, 10).ToList();
        if (defaultIndex is >= 0 and <= 9)
        {
            indices.Remove(defaultIndex);
            indices.Insert(0, defaultIndex);
        }

        return indices;
    }

    public static void ValidateQuality(int quality)
    {
        if (quality is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(quality), "JPEG quality must be between 1 and 100.");
        }
    }

    public static int ParseDeviceIndex(string deviceId)
    {
        if (!int.TryParse(deviceId, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            || index is < 0 or > 9)
        {
            throw new ArgumentOutOfRangeException(nameof(deviceId), "Camera device ID must be an index from 0 through 9.");
        }

        return index;
    }

    public async Task<IReadOnlyList<CameraDevice>> ListDevicesAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run<IReadOnlyList<CameraDevice>>(() =>
            {
                var devices = new List<CameraDevice>();
                foreach (var index in CandidateIndices(_options.CameraIndex))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var probe = TryCreateCapture(index);
                    if (probe is not null)
                    {
                        devices.Add(new CameraDevice(
                            index.ToString(CultureInfo.InvariantCulture),
                            $"Camera {index}"));
                    }
                }

                return devices;
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CameraOpenResult> OpenAsync(
        CameraOpenRequest request,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var index = ParseDeviceIndex(request.DeviceId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                if (_capture is not null)
                {
                    throw new CameraException(ErrorCodes.InvalidState, "A camera is already open.");
                }

                var capture = TryCreateCapture(index);
                if (capture is null)
                {
                    throw new CameraException(
                        ErrorCodes.CameraOpenFailed,
                        $"Camera {index} could not be opened.",
                        true);
                }

                try
                {
                    capture.FrameWidth = request.Width;
                    capture.FrameHeight = request.Height;
                    capture.Fps = request.Fps;
                    _capture = capture;
                    return new CameraOpenResult(capture.FrameWidth, capture.FrameHeight, capture.Fps);
                }
                catch
                {
                    capture.Release();
                    capture.Dispose();
                    throw;
                }
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (CameraException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new CameraException(
                ErrorCodes.CameraOpenFailed,
                $"Camera {index} could not be opened.",
                true,
                exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<JpegFrame> ReadJpegFrameAsync(
        int jpegQuality,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ValidateQuality(jpegQuality);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                var capture = _capture;
                if (capture is null || !capture.IsOpened())
                {
                    throw new CameraException(
                        ErrorCodes.CameraDisconnected,
                        "The camera is not connected.",
                        true);
                }

                using var frame = new Mat();
                if (!capture.Read(frame) || frame.Empty())
                {
                    throw new CameraException(
                        ErrorCodes.CameraDisconnected,
                        "The camera did not return an image.",
                        true);
                }

                if (!Cv2.ImEncode(
                        ".jpg",
                        frame,
                        out var jpeg,
                        [new ImageEncodingParam(ImwriteFlags.JpegQuality, jpegQuality)]))
                {
                    throw new CameraException(
                        ErrorCodes.CameraDisconnected,
                        "The camera image could not be encoded.",
                        true);
                }

                return new JpegFrame(jpeg, frame.Width, frame.Height, DateTimeOffset.Now);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (CameraException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new CameraException(
                ErrorCodes.CameraDisconnected,
                "The camera frame could not be read.",
                true,
                exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CloseAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                var capture = _capture;
                _capture = null;
                if (capture is null)
                {
                    return;
                }

                capture.Release();
                capture.Dispose();
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await CloseAsync(CancellationToken.None).ConfigureAwait(false);
        _disposed = true;
        _gate.Dispose();
    }

    private static VideoCapture? TryCreateCapture(int index)
    {
        foreach (var backend in new[] { VideoCaptureAPIs.MSMF, VideoCaptureAPIs.DSHOW })
        {
            var capture = new VideoCapture();
            try
            {
                if (capture.Open(index, backend) && capture.IsOpened())
                {
                    return capture;
                }
            }
            catch
            {
            }

            capture.Release();
            capture.Dispose();
        }

        return null;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
