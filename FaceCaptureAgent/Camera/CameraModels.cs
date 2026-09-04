namespace FaceCaptureAgent.Camera;

public sealed record CameraDevice(string Id, string Name);

public sealed record CameraOpenRequest(string DeviceId, int Width, int Height, int Fps);

public sealed record CameraOpenResult(int Width, int Height, double Fps);

public sealed record JpegFrame(byte[] Bytes, int Width, int Height, DateTimeOffset CapturedAt);

public interface ICameraService : IAsyncDisposable
{
    Task<IReadOnlyList<CameraDevice>> ListDevicesAsync(CancellationToken cancellationToken);

    Task<CameraOpenResult> OpenAsync(CameraOpenRequest request, CancellationToken cancellationToken);

    Task<JpegFrame> ReadJpegFrameAsync(int jpegQuality, CancellationToken cancellationToken);

    Task CloseAsync(CancellationToken cancellationToken);
}

public sealed class CameraException : Exception
{
    public CameraException(string code, string message, bool retryable = false, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        Retryable = retryable;
    }

    public string Code { get; }

    public bool Retryable { get; }
}
