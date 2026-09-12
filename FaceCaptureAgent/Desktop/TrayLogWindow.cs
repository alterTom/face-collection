using System.Windows.Forms;

namespace FaceCaptureAgent.Desktop;

/// <summary>关闭或最小化时隐藏到托盘，只有托盘退出操作才停止后台服务。</summary>
public sealed class TrayLogWindow : Form
{
    public TrayLogWindow()
    {
        FormClosing += (_, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray();
            }
        };
    }

    private void HideToTray()
    {
        Hide();
    }

    protected override void WndProc(ref Message message)
    {
        const int WmSysCommand = 0x0112;
        const int ScMinimize = 0xF020;
        if (message.Msg == WmSysCommand && ((long)message.WParam & 0xFFF0) == ScMinimize)
        {
            // 在原生最小化前隐藏；若在 Resize 中重建句柄，未完成的最小化命令会影响新窗口。
            HideToTray();
            return;
        }
        base.WndProc(ref message);
    }

    public void RestoreFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }
}
