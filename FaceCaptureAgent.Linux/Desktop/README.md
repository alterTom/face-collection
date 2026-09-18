# Linux desktop integration

`LinuxDesktopService` implements `IHostedService`; constructor dependencies are the shared `ActivityLog`, `AgentOptions`, and `IHostApplicationLifetime`. Register it only for graphical mode. It uses system GTK 3 (`libgtk-3.so.0`), GLib and GObject through UTF-8 C ABI calls, with no managed desktop framework.

All widgets and signals live on one background GTK thread. A 250 ms GLib timer copies the bounded in-memory log and observes host shutdown; stopping waits at most two seconds. Native callbacks retain managed delegates and contain exceptions. No photos or log files are written. Missing display/GTK reports a fixed diagnostic and leaves the local HTTP service running.

The test-page button becomes available after `ApplicationStarted` and invokes `xdg-open` with the configured loopback URL. Exit from the window or tray menu requests host shutdown. Closing hides the window only while a tray host embeds the legacy GTK status icon. Otherwise it minimizes and retains a taskbar entry. If the panel/tray disappears while hidden, the timer restores the window. Ordinary minimization retains a taskbar entry.

GTK StatusIcon depends on legacy tray support, typically available in Kylin X11 panels; Wayland and desktops without that support use the window/taskbar fallback. The window and tray use the bundled `face-capture.png` from the executable directory, with a themed `camera-photo` tray fallback if the file is missing. Target-machine validation must cover the installed GTK ABI, icon theme, tray embedding, repeated close/restore, panel restart, browser launching, and exit with active camera connections. Compilation on Windows does not validate native GUI behavior or Kylin compatibility.
