using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using FaceCaptureAgent.Desktop;
using Xunit;

namespace FaceCaptureAgent.Tests.Hosting;

public sealed class TrayLogWindowTests
{
    [Fact]
    public void MinimizeAndRestore_RepeatedlyRestoresVisibleNativeWindow()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var window = new TrayLogWindow();
                window.RestoreFromTray();
                Application.DoEvents();
                for (var i = 0; i < 3; i++)
                {
                    // SC_MINIMIZE is also sent by Windows when clicking the active taskbar button.
                    SendMessage(window.Handle, 0x0112, 0xF020, 0);
                    Application.DoEvents();
                    Assert.False(IsWindowVisible(window.Handle));

                    window.RestoreFromTray();
                    Application.DoEvents();
                    Assert.True(window.Visible);
                    Assert.True(window.ShowInTaskbar);
                    Assert.True(IsWindowVisible(window.Handle));
                    Assert.False(IsIconic(window.Handle));
                    Assert.Equal(FormWindowState.Normal, window.WindowState);
                }
                window.Close();
                Application.DoEvents();
                Assert.False(window.IsDisposed);
                Assert.False(IsWindowVisible(window.Handle));
                window.RestoreFromTray();
                Application.DoEvents();
                Assert.True(IsWindowVisible(window.Handle));
                Assert.False(IsIconic(window.Handle));
            }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Desktop regression test timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);
}
