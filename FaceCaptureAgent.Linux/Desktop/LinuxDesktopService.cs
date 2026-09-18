using System.Diagnostics;
using System.Runtime.InteropServices;
using FaceCaptureAgent.Configuration;
using FaceCaptureAgent.Diagnostics;
using Microsoft.Extensions.Hosting;

namespace FaceCaptureAgent.Desktop;

/// <summary>Optional GTK 3 desktop. All GTK access stays on its dedicated event-loop thread.</summary>
public sealed class LinuxDesktopService(ActivityLog log, AgentOptions options, IHostApplicationLifetime lifetime) : IHostedService
{
    private readonly List<Delegate> callbacks = [];
    private readonly TaskCompletionSource finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool stopping;
    private nint window, tray, menu, buffer, testButton;
    private long lastSequence = -1;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        new Thread(Run) { IsBackground = true, Name = "GTK desktop" }.Start();
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        stopping = true;
        // The GTK timer observes this flag; never call GTK from a host worker thread.
        try { await finished.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken); }
        catch (TimeoutException) { }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private void Run()
    {
        uint timer = 0;
        try
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")) &&
                string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
            {
                ReportUnavailable();
                return;
            }
            if (Gtk.gtk_init_check(0, 0) == 0) { ReportUnavailable(); return; }
            if (stopping) return;
            BuildWindow();
            Gtk.SourceFunc tick = Tick;
            callbacks.Add(tick);
            timer = Gtk.g_timeout_add(250, tick, 0);
            Gtk.gtk_widget_show_all(window);
            log.Write("Linux 日志窗口已启动");
            Gtk.gtk_main();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            ReportUnavailable();
        }
        catch (Exception)
        {
            // Never print exception contents, environment values or camera data.
            log.Write("Linux 桌面初始化失败：DESKTOP_START_FAILED；本机服务继续运行");
            Console.Error.WriteLine("DESKTOP_START_FAILED: desktop unavailable; local service continues.");
        }
        finally
        {
            // Cleanup is also confined to the GTK thread. Missing-library startup has no handles.
            try
            {
                if (timer != 0) Gtk.g_source_remove(timer);
                if (tray != 0) { Gtk.gtk_status_icon_set_visible(tray, 0); Gtk.g_object_unref(tray); }
                if (menu != 0) Gtk.gtk_widget_destroy(menu);
                if (window != 0) Gtk.gtk_widget_destroy(window);
            }
            catch (Exception) { /* Partial GTK initialization must not terminate the service. */ }
            finally { finished.TrySetResult(); GC.KeepAlive(callbacks); }
        }
    }

    private void ReportUnavailable()
    {
        log.Write("Linux 桌面不可用：DESKTOP_UNAVAILABLE；本机服务继续运行");
        Console.Error.WriteLine("DESKTOP_UNAVAILABLE: GTK 3 or a graphical session is unavailable; local service continues.");
    }

    private void BuildWindow()
    {
        window = Gtk.gtk_window_new(0);
        Gtk.gtk_window_set_title(window, "刷脸认证");
        Gtk.gtk_window_set_default_size(window, 800, 480);
        var iconPath = Path.Combine(AppContext.BaseDirectory, "face-capture.png");
        var hasBundledIcon = File.Exists(iconPath);
        if (hasBundledIcon) Gtk.gtk_window_set_icon_from_file(window, iconPath, 0);
        var layout = Gtk.gtk_box_new(1, 8);
        Gtk.gtk_container_set_border_width(layout, 12);
        Gtk.gtk_container_add(window, layout);
        var toolbar = Gtk.gtk_box_new(0, 8);
        Gtk.gtk_box_pack_start(layout, toolbar, 0, 0, 0);
        testButton = Gtk.gtk_button_new_with_label("打开测试页");
        Gtk.gtk_widget_set_sensitive(testButton, 0);
        Gtk.gtk_box_pack_start(toolbar, testButton, 0, 0, 0);
        Connect(testButton, "clicked", new Gtk.Signal((_, _) => OpenTestPage()));
        var exit = Gtk.gtk_button_new_with_label("退出");
        Gtk.gtk_box_pack_start(toolbar, exit, 0, 0, 0);
        Connect(exit, "clicked", new Gtk.Signal((_, _) => lifetime.StopApplication()));
        var scroll = Gtk.gtk_scrolled_window_new(0, 0);
        Gtk.gtk_box_pack_start(layout, scroll, 1, 1, 0);
        var text = Gtk.gtk_text_view_new();
        Gtk.gtk_text_view_set_editable(text, 0);
        Gtk.gtk_text_view_set_cursor_visible(text, 0);
        Gtk.gtk_text_view_set_wrap_mode(text, 2);
        buffer = Gtk.gtk_text_view_get_buffer(text);
        Gtk.gtk_container_add(scroll, text);
        Connect(window, "delete-event", new Gtk.EventSignal((_, _, _) =>
        {
            if (tray != 0 && Gtk.gtk_status_icon_is_embedded(tray) != 0) Gtk.gtk_widget_hide(window);
            else Gtk.gtk_window_iconify(window); // Without a tray host retain a taskbar restore path.
            return 1;
        }));
        tray = hasBundledIcon
            ? Gtk.gtk_status_icon_new_from_file(iconPath)
            : Gtk.gtk_status_icon_new_from_icon_name("camera-photo");
        Gtk.gtk_status_icon_set_tooltip_text(tray, "刷脸认证");
        Connect(tray, "activate", new Gtk.Signal((_, _) => Restore()));
        menu = Gtk.gtk_menu_new();
        var showItem = Gtk.gtk_menu_item_new_with_label("查看日志");
        Gtk.gtk_menu_shell_append(menu, showItem);
        Connect(showItem, "activate", new Gtk.Signal((_, _) => Restore()));
        var exitItem = Gtk.gtk_menu_item_new_with_label("退出");
        Gtk.gtk_menu_shell_append(menu, exitItem);
        Connect(exitItem, "activate", new Gtk.Signal((_, _) => lifetime.StopApplication()));
        Gtk.gtk_widget_show_all(menu);
        Connect(tray, "popup-menu", new Gtk.PopupSignal((_, button, time, _) =>
            Gtk.gtk_menu_popup(menu, 0, 0, 0, 0, button, time)));
        Gtk.gtk_status_icon_set_visible(tray, 1);
    }

    private int Tick(nint data)
    {
        // Never let a managed exception unwind through a native callback.
        try
        {
            if (stopping || lifetime.ApplicationStopping.IsCancellationRequested)
            {
                Gtk.gtk_main_quit();
                return 1; // Removed by Run's finally after gtk_main returns.
            }
            Gtk.gtk_widget_set_sensitive(testButton, lifetime.ApplicationStarted.IsCancellationRequested ? 1 : 0);
            // Tray hosts can disappear while hidden (e.g. desktop panel restart).
            if (Gtk.gtk_widget_get_visible(window) == 0 && Gtk.gtk_status_icon_is_embedded(tray) == 0) Restore();
            var entries = log.Snapshot();
            var sequence = entries.Length == 0 ? 0 : entries[^1].Sequence;
            if (sequence != lastSequence)
            {
                Gtk.gtk_text_buffer_set_text(buffer, string.Join('\n', entries.Select(entry => entry.Text)), -1);
                lastSequence = sequence;
            }
        }
        catch (Exception) { /* Keep the native callback boundary intact. */ }
        return 1;
    }

    private void Restore()
    {
        Gtk.gtk_widget_show_all(window);
        Gtk.gtk_window_deiconify(window);
        Gtk.gtk_window_present(window);
    }

    private void OpenTestPage()
    {
        if (!lifetime.ApplicationStarted.IsCancellationRequested) return;
        try
        {
            var start = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
            start.ArgumentList.Add($"http://127.0.0.1:{options.ListenPort}/test/");
            using var process = Process.Start(start);
        }
        catch (Exception) { log.Write("打开测试页失败：BROWSER_START_FAILED"); }
    }

    private void Connect(nint instance, string signal, Delegate callback)
    {
        Delegate guarded = callback switch
        {
            Gtk.Signal action => new Gtk.Signal((widget, data) => SafeCallback(() => action(widget, data))),
            Gtk.PopupSignal action => new Gtk.PopupSignal((icon, button, time, data) => SafeCallback(() => action(icon, button, time, data))),
            Gtk.EventSignal action => new Gtk.EventSignal((widget, evt, data) =>
            {
                try { return action(widget, evt, data); }
                catch (Exception) { return 1; }
            }),
            _ => throw new ArgumentException("Unsupported GTK callback type.", nameof(callback))
        };
        callbacks.Add(guarded);
        Gtk.g_signal_connect_data(instance, signal, Marshal.GetFunctionPointerForDelegate(guarded), 0, 0, 0);
    }

    private static void SafeCallback(Action action)
    {
        try { action(); }
        catch (Exception) { /* No managed exceptions may cross a native signal boundary. */ }
    }
}
