using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

internal static class GlobalHotkey
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 1;

    public static Thread Start(Action onHotkey)
    {
        var thread = new Thread(() =>
        {
            // Ctrl + Space
            const HOT_KEY_MODIFIERS modifiers = HOT_KEY_MODIFIERS.MOD_CONTROL;
            const uint virtualKey = (uint)VIRTUAL_KEY.VK_SPACE;

            if (!PInvoke.RegisterHotKey(HWND.Null, HOTKEY_ID, modifiers, virtualKey))
            {
                throw new InvalidOperationException("Failed to register global hotkey.");
            }

            try
            {
                while (PInvoke.GetMessage(out MSG msg, HWND.Null, 0, 0).Value > 0)
                {
                    if (msg.message == WM_HOTKEY && msg.wParam.Value == HOTKEY_ID)
                    {
                        onHotkey();
                    }

                    PInvoke.TranslateMessage(msg);
                    PInvoke.DispatchMessage(msg);
                }
            }
            finally
            {
                PInvoke.UnregisterHotKey(HWND.Null, HOTKEY_ID);
            }
        });

        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return thread;
    }
}