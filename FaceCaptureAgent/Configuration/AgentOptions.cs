using Tomlyn.Serialization;

namespace FaceCaptureAgent.Configuration;

/// <summary>本机服务配置及默认值；自动拍照模式和稳定时长由每个会话单独设置。</summary>
public sealed class AgentOptions
{
    [TomlPropertyName("listen_address")]
    public string ListenAddress { get; set; } = "127.0.0.1";

    [TomlPropertyName("listen_port")]
    public int ListenPort { get; set; } = 17653;

    [TomlPropertyName("camera_index")]
    public int CameraIndex { get; set; }

    [TomlPropertyName("capture_width")]
    public int CaptureWidth { get; set; } = 1280;

    [TomlPropertyName("capture_height")]
    public int CaptureHeight { get; set; } = 720;

    [TomlPropertyName("preview_fps")]
    public int PreviewFps { get; set; } = 5;

    [TomlPropertyName("jpeg_quality")]
    public int JpegQuality { get; set; } = 85;

    [TomlPropertyName("max_image_bytes")]
    public int MaxImageBytes { get; set; } = 2 * 1024 * 1024;

    [TomlPropertyName("camera_timeout_seconds")]
    public int CameraTimeoutSeconds { get; set; } = 10;

    public void Validate()
    {
        if (!string.Equals(ListenAddress, "127.0.0.1", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("listen_address must be exactly 127.0.0.1.");
        }

        if (ListenPort is < 1024 or > 65535)
        {
            throw new InvalidOperationException("listen_port must be between 1024 and 65535.");
        }

        if (CameraIndex < 0)
        {
            throw new InvalidOperationException("camera_index must be zero or greater.");
        }

        if (CaptureWidth <= 0 || CaptureHeight <= 0)
        {
            throw new InvalidOperationException("capture_width and capture_height must be greater than zero.");
        }

        if (PreviewFps is < 1 or > 15)
        {
            throw new InvalidOperationException("preview_fps must be between 1 and 15.");
        }

        if (JpegQuality is < 1 or > 100)
        {
            throw new InvalidOperationException("jpeg_quality must be between 1 and 100.");
        }

        if (MaxImageBytes <= 0)
        {
            throw new InvalidOperationException("max_image_bytes must be greater than zero.");
        }

        if (CameraTimeoutSeconds <= 0)
        {
            throw new InvalidOperationException("camera_timeout_seconds must be greater than zero.");
        }
    }
}
