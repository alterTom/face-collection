using System.Windows.Forms;

namespace FaceCaptureAgent.Desktop;

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
            // Hide before native minimization starts. Changing the handle from a
            // Resize callback lets the pending native command minimize the new window.
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
