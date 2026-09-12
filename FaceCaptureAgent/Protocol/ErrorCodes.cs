namespace FaceCaptureAgent.Protocol;

/// <summary>浏览器按这些协议错误码选择提示文案，修改时需保持 SDK 兼容。</summary>
public static class ErrorCodes
{
    public const string InvalidMessage = "INVALID_MESSAGE";
    public const string UnsupportedProtocol = "UNSUPPORTED_PROTOCOL";
    public const string NoCamera = "NO_CAMERA";
    public const string CameraBusy = "CAMERA_BUSY";
    public const string CameraOpenFailed = "CAMERA_OPEN_FAILED";
    public const string CameraDisconnected = "CAMERA_DISCONNECTED";
    public const string InvalidState = "INVALID_STATE";
    public const string CaptureTimeout = "CAPTURE_TIMEOUT";
    public const string ImageTooLarge = "IMAGE_TOO_LARGE";
    public const string InternalError = "INTERNAL_ERROR";
}
