using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

internal static class WindowChrome
{
    public static void HideFromTaskbarAndAltTab(HWND hwnd)
    {
        if (hwnd == HWND.Null)
        {
            return;
        }

        WINDOW_EX_STYLE exStyle =
            (WINDOW_EX_STYLE)PInvoke.GetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);

        exStyle &= ~WINDOW_EX_STYLE.WS_EX_APPWINDOW;
        exStyle |= WINDOW_EX_STYLE.WS_EX_TOOLWINDOW;

        _ = PInvoke.SetWindowLongPtr(
            hwnd,
            WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE,
            (nint)exStyle);

        _ = PInvoke.SetWindowPos(
            hwnd,
            HWND.Null,
            0,
            0,
            0,
            0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE |
            SET_WINDOW_POS_FLAGS.SWP_NOSIZE |
            SET_WINDOW_POS_FLAGS.SWP_NOZORDER |
            SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED |
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
    }
}
