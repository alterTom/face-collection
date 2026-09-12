namespace FaceCaptureAgent.Sessions;

/// <summary>会话状态：连接 → 打开设备 → 预览；手动抓拍临时进入 Capturing，结束后恢复原状态。</summary>
public enum SessionState
{
    Connected,
    CameraOpen,
    Previewing,
    Capturing,
    // 连接已释放的终态；仅关闭摄像头会回到 Connected，而不是 Closed。
    Closed
}
