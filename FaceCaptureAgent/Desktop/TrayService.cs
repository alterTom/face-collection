using FaceCaptureAgent.Diagnostics;
using FaceCaptureAgent.Configuration;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace FaceCaptureAgent.Desktop;

/// <summary>在独立 STA 线程运行托盘和日志窗口，通过宿主生命周期协调启动与退出。</summary>
public sealed class TrayService(ActivityLog log, IHostApplicationLifetime lifetime, AgentOptions options) : IHostedService
{
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Form? _window;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var thread = new Thread(Run) { IsBackground = true, Name = "FaceCaptureAgent tray" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return _ready.Task.WaitAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_window is { IsDisposed: false, IsHandleCreated: true } window)
        {
            // 停止请求来自宿主线程，必须切回窗口线程退出 WinForms 消息循环。
            try { window.BeginInvoke((Action)Application.ExitThread); }
            catch (InvalidOperationException) { }
        }
        await _stopped.Task.WaitAsync(cancellationToken);
    }

    private void Run()
    {
        try
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            using var iconStream = typeof(TrayService).Assembly.GetManifestResourceStream("FaceCaptureAgent.AppIcon")
                ?? throw new InvalidOperationException("Application icon resource is missing.");
            using var appIcon = new Icon(iconStream);
            using var trayIcon = new Icon(appIcon, SystemInformation.SmallIconSize);
            using var window = new TrayLogWindow
            {
                Text = "刷脸认证 — 运行日志",
                Size = new Size(860, 520),
                MinimumSize = new Size(540, 300),
                StartPosition = FormStartPosition.CenterScreen,
                Icon = appIcon
            };
            using var text = new TextBox
            {
                Multiline = true, ReadOnly = true, Dock = DockStyle.Fill,
                ScrollBars = ScrollBars.Both, WordWrap = false,
                Font = new Font("Microsoft YaHei UI", 10),
                AccessibleName = "运行日志"
            };
            window.Controls.Add(text);
            using var toolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8)
            };
            using var testButton = new Button
            {
                Text = "打开测试页", AutoSize = true, Enabled = false,
                AccessibleName = "打开测试页"
            };
            testButton.Click += (_, _) =>
            {
                var url = $"http://127.0.0.1:{options.ListenPort}/test/";
                try
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    log.Write("已请求浏览器打开测试页");
                }
                catch (Exception exception)
                {
                    log.Write($"打开测试页失败：{exception.GetType().Name}");
                    MessageBox.Show(window, $"无法打开浏览器，请手动访问：{url}", "打开测试页失败",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };
            toolbar.Controls.Add(testButton);
            window.Controls.Add(toolbar);
            using var menu = new ContextMenuStrip();
            using var tray = new NotifyIcon
            {
                Icon = trayIcon, Text = "刷脸认证", ContextMenuStrip = menu, Visible = true
            };
            long lastSequence = 0;
            void RefreshLog()
            {
                var entries = log.Snapshot();
                if (entries.Length == 0 || entries[^1].Sequence == lastSequence) return;
                lastSequence = entries[^1].Sequence;
                text.Text = string.Join(Environment.NewLine, entries.Select(entry => entry.Text));
                text.SelectionStart = text.TextLength;
                text.ScrollToCaret();
            }
            void ShowLog()
            {
                RefreshLog();
                window.RestoreFromTray();
            }
            menu.Items.Add("查看日志", null, (_, _) => ShowLog());
            menu.Items.Add("退出", null, (_, _) =>
            {
                menu.Enabled = false;
                log.Write("正在退出程序，关闭连接并释放摄像头");
                lifetime.StopApplication();
            });
            tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowLog(); };
            using var timer = new System.Windows.Forms.Timer { Interval = 250 };
            timer.Tick += (_, _) =>
            {
                testButton.Enabled = lifetime.ApplicationStarted.IsCancellationRequested
                    && !lifetime.ApplicationStopping.IsCancellationRequested;
                if (window.Visible) RefreshLog();
            };
            timer.Start();
            _ = window.Handle;
            _window = window;
            ShowLog();
            _ready.TrySetResult();
            Application.Run();
            tray.Visible = false;
        }
        catch (Exception exception)
        {
            log.Write($"托盘界面异常：{exception.GetType().Name}");
            _ready.TrySetException(exception);
            lifetime.StopApplication();
        }
        finally
        {
            _window = null;
            _stopped.TrySetResult();
        }
    }
}
