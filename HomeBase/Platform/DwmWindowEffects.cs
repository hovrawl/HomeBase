using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.Controls;

namespace HomeBase.Windows;

internal static unsafe class DwmWindowEffects
{
    public static void ApplyDockWindowEffects(HWND hwnd)
    {
        if (hwnd == HWND.Null)
        {
            return;
        }

        TryExtendFrameIntoClientArea(hwnd);
        TryUseImmersiveDarkMode(hwnd, false);
        TrySetRoundedCorners(hwnd);
        TrySetMicaBackdrop(hwnd);
    }

    private static void TryExtendFrameIntoClientArea(HWND hwnd)
    {
        MARGINS margins = new()
        {
            cxLeftWidth = -1,
            cxRightWidth = -1,
            cyTopHeight = -1,
            cyBottomHeight = -1
        };

        _ = PInvoke.DwmExtendFrameIntoClientArea(hwnd, &margins);
    }
    
    private static void TryUseImmersiveDarkMode(HWND hwnd, BOOL enabled)
    {

        _ = PInvoke.DwmSetWindowAttribute(
            hwnd,
            DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE,
            &enabled,
            (uint)sizeof(BOOL));
    }

    private static void TrySetRoundedCorners(HWND hwnd)
    {
        DWM_WINDOW_CORNER_PREFERENCE preference =
            DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;

        _ = PInvoke.DwmSetWindowAttribute(
            hwnd,
            DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE,
            &preference,
            (uint)sizeof(DWM_WINDOW_CORNER_PREFERENCE));
    }

    private static void TrySetMicaBackdrop(HWND hwnd)
    {
        DWM_SYSTEMBACKDROP_TYPE backdrop =
            DWM_SYSTEMBACKDROP_TYPE.DWMSBT_TRANSIENTWINDOW;

        _ = PInvoke.DwmSetWindowAttribute(
            hwnd,
            DWMWINDOWATTRIBUTE.DWMWA_SYSTEMBACKDROP_TYPE,
            &backdrop,
            (uint)sizeof(DWM_SYSTEMBACKDROP_TYPE));
    }
}