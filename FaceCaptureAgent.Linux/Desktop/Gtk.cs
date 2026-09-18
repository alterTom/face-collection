using System.Runtime.InteropServices;

namespace FaceCaptureAgent.Desktop;

// GTK gboolean is a C int; pointers and gulong are native-sized on supported Linux targets.
internal static class Gtk
{
    private const string Library = "libgtk-3.so.0";
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int SourceFunc(nint data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void Signal(nint widget, nint data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int EventSignal(nint widget, nint evt, nint data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void PopupSignal(nint icon, uint button, uint time, nint data);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int gtk_init_check(nint argc, nint argv);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void gtk_main();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void gtk_main_quit();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint gtk_window_new(int type);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void gtk_window_set_title(nint window, [MarshalAs(UnmanagedType.LPUTF8Str)] string title);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void gtk_window_set_default_size(nint window, int width, int height);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int gtk_window_set_icon_from_file(nint window, [MarshalAs(UnmanagedType.LPUTF8Str)] string filename, nint error);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void gtk_window_present(nint window);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void gtk_window_iconify(nint window);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void gtk_window_deiconify(nint window);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint gtk_box_new(int orientation, int spacing);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void gtk_box_pack_start(nint box, nint child, int expand, int fill, uint padding);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void gtk_container_add(nint container, nint child);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void gtk_container_set_border_width(nint container, uint borderWidth);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint gtk_button_new_with_label([MarshalAs(UnmanagedType.LPUTF8Str)] string label);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void gtk_widget_set_sensitive(nint widget, int sensitive);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void gtk_widget_show_all(nint widget);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void gtk_widget_hide(nint widget);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void gtk_widget_destroy(nint widget);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int gtk_widget_get_visible(nint widget);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint gtk_scrolled_window_new(nint horizontalAdjustment, nint verticalAdjustment);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint gtk_text_view_new();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void gtk_text_view_set_editable(nint view, int editable);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void gtk_text_view_set_cursor_visible(nint view, int visible);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void gtk_text_view_set_wrap_mode(nint view, int mode);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint gtk_text_view_get_buffer(nint view);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void gtk_text_buffer_set_text(nint buffer, [MarshalAs(UnmanagedType.LPUTF8Str)] string text, int length);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint gtk_status_icon_new_from_icon_name([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint gtk_status_icon_new_from_file([MarshalAs(UnmanagedType.LPUTF8Str)] string filename);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void gtk_status_icon_set_tooltip_text(nint icon, [MarshalAs(UnmanagedType.LPUTF8Str)] string text);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void gtk_status_icon_set_visible(nint icon, int visible);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int gtk_status_icon_is_embedded(nint icon);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint gtk_menu_new();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint gtk_menu_item_new_with_label([MarshalAs(UnmanagedType.LPUTF8Str)] string label);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void gtk_menu_shell_append(nint menu, nint child);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void gtk_menu_popup(nint menu, nint parentShell, nint parentItem, nint positionFunction, nint data, uint button, uint activateTime);

    [DllImport("libglib-2.0.so.0", CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint g_timeout_add(uint interval, SourceFunc callback, nint data);
    [DllImport("libglib-2.0.so.0", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int g_source_remove(uint tag);
    [DllImport("libgobject-2.0.so.0", CallingConvention = CallingConvention.Cdecl)]
    internal static extern nuint g_signal_connect_data(nint instance, [MarshalAs(UnmanagedType.LPUTF8Str)] string signal, nint callback, nint data, nint destroyData, int flags);
    [DllImport("libgobject-2.0.so.0", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void g_object_unref(nint instance);
}
