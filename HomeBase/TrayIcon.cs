using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

internal static unsafe class TrayIcon
{
    private const uint TrayIconId = 1;
    private const uint WM_TRAYICON = 0x8000 + 1;

    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_RBUTTONUP = 0x0205;

    private const uint WM_NULL = 0x0000;

    private const uint QuitMenuId = 1001;

    private static NOTIFYICONDATAW _data;
    private static HWND _hwnd;
    private static Action? _onQuit;
    private static nint _oldWndProc;

    public static void Add(HWND hwnd, Action onQuit)
    {
        if (hwnd == HWND.Null)
        {
            return;
        }

        _hwnd = hwnd;
        _onQuit = onQuit;

        SubclassWindow(hwnd);

        HMODULE hInstance = PInvoke.GetModuleHandle(new PCWSTR(null));

        int iconWidth = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXSMICON);
        int iconHeight = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CYSMICON);

        HANDLE image = PInvoke.LoadImage(
            hInstance,
            PInvoke.IDI_APPLICATION,
            GDI_IMAGE_TYPE.IMAGE_ICON,
            iconWidth,
            iconHeight,
            IMAGE_FLAGS.LR_SHARED);

        if (image.IsNull)
        {
            throw new InvalidOperationException("Failed to load embedded application icon.");
        }

        HICON icon = new(image.Value);

        _data = new NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(NOTIFYICONDATAW),
            hWnd = hwnd,
            uID = TrayIconId,
            uFlags =
                NOTIFY_ICON_DATA_FLAGS.NIF_MESSAGE |
                NOTIFY_ICON_DATA_FLAGS.NIF_ICON |
                NOTIFY_ICON_DATA_FLAGS.NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = icon
        };

        string tooltip = "HomeBase";

        fixed (char* tip = _data.szTip.Value)
        {
            tooltip.AsSpan().CopyTo(new Span<char>(tip, _data.szTip.Length));
        }

        _ = PInvoke.Shell_NotifyIcon(
            NOTIFY_ICON_MESSAGE.NIM_ADD,
            ref _data);
    }

    public static void Remove()
    {
        if (_data.cbSize != 0)
        {
            _ = PInvoke.Shell_NotifyIcon(
                NOTIFY_ICON_MESSAGE.NIM_DELETE,
                ref _data);

            _data = default;
        }

        RestoreWindowProc();

        _hwnd = HWND.Null;
        _onQuit = null;
    }

    private static void SubclassWindow(HWND hwnd)
    {
        if (_oldWndProc != 0)
        {
            return;
        }

        _oldWndProc = PInvoke.SetWindowLongPtr(
            hwnd,
            WINDOW_LONG_PTR_INDEX.GWLP_WNDPROC,
            (nint)(delegate* unmanaged[Stdcall]<HWND, uint, WPARAM, LPARAM, LRESULT>)&WndProc);
    }

    private static void RestoreWindowProc()
    {
        if (_hwnd == HWND.Null || _oldWndProc == 0)
        {
            return;
        }

        _ = PInvoke.SetWindowLongPtr(
            _hwnd,
            WINDOW_LONG_PTR_INDEX.GWLP_WNDPROC,
            _oldWndProc);

        _oldWndProc = 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT WndProc(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        if (msg == WM_TRAYICON && wParam.Value == TrayIconId)
        {
            uint mouseMessage = unchecked((uint)lParam.Value);

            if (mouseMessage == WM_RBUTTONUP)
            {
                ShowContextMenu(hwnd);
                return new LRESULT(0);
            }

            if (mouseMessage == WM_LBUTTONUP)
            {
                // Optional: left-click behavior can go here later.
                return new LRESULT(0);
            }
        }

        return PInvoke.CallWindowProc(
            (delegate* unmanaged[Stdcall]<HWND, uint, WPARAM, LPARAM, LRESULT>)_oldWndProc,
            hwnd,
            msg,
            wParam,
            lParam);
    }

    private static void ShowContextMenu(HWND hwnd)
    {
        HMENU menu = PInvoke.CreatePopupMenu();

        if (menu == HMENU.Null)
        {
            return;
        }

        try
        {
            fixed (char* menuItemText = "Quit")
            {
                
                _ = PInvoke.AppendMenu(
                    menu,
                    MENU_ITEM_FLAGS.MF_STRING,
                    QuitMenuId,
                    menuItemText);
            }

            if (!PInvoke.GetCursorPos(out Point cursorPosition))
            {
                return;
            }

            // Required by Win32 so the menu dismisses correctly when clicking away.
            _ = PInvoke.SetForegroundWindow(hwnd);

            var selectedCommand = PInvoke.TrackPopupMenu(
                menu,
                TRACK_POPUP_MENU_FLAGS.TPM_RIGHTBUTTON |
                TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD |
                TRACK_POPUP_MENU_FLAGS.TPM_NONOTIFY,
                cursorPosition.X,
                cursorPosition.Y,
                0,
                hwnd,
                null);

            if (selectedCommand == QuitMenuId)
            {
                _onQuit?.Invoke();
            }

            // Recommended after TrackPopupMenu when using a notification icon.
            _ = PInvoke.PostMessage(hwnd, WM_NULL, default, default);
        }
        finally
        {
            _ = PInvoke.DestroyMenu(menu);
        }
    }
}
