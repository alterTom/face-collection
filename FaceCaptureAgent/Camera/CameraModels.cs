namespace FaceCaptureAgent.Camera;

public sealed record CameraDevice(string Id, string Name);

public sealed record CameraOpenRequest(string DeviceId, int Width, int Height, int Fps);

/// <summary>设备实际采用的参数，可能与请求的分辨率或帧率不同。</summary>
public sealed record CameraOpenResult(int Width, int Height, double Fps);

/// <summary>一帧完整 JPEG 及其尺寸、采集时间；预览传二进制，抓拍响应转换为 Base64。</summary>
public sealed record JpegFrame(byte[] Bytes, int Width, int Height, DateTimeOffset CapturedAt);

/// <summary>摄像头访问边界，生产使用 OpenCV，测试可替换为无硬件实现。</summary>
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
