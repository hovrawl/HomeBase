using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

internal sealed class GlobalHotkey : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 1;

    private readonly Thread _thread;
    private readonly Action _onHotkey;
    private readonly ManualResetEventSlim _started = new();

    private uint _threadId;
    private bool _disposed;

    public GlobalHotkey(Action onHotkey)
    {
        _onHotkey = onHotkey;

        _thread = new Thread(RunMessageLoop);
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.IsBackground = true;
        _thread.Start();

        _started.Wait();
    }

    private void RunMessageLoop()
    {
        _threadId = PInvoke.GetCurrentThreadId();

        // Force this thread's message queue to be created before Dispose can PostThreadMessage.
        PInvoke.PeekMessage(out _, HWND.Null, 0, 0, PEEK_MESSAGE_REMOVE_TYPE.PM_NOREMOVE);

        const HOT_KEY_MODIFIERS modifiers = HOT_KEY_MODIFIERS.MOD_CONTROL;
        const uint virtualKey = (uint)VIRTUAL_KEY.VK_SPACE;

        if (!PInvoke.RegisterHotKey(HWND.Null, HOTKEY_ID, modifiers, virtualKey))
        {
            _started.Set();
            return;
        }

        _started.Set();

        try
        {
            while (PInvoke.GetMessage(out MSG msg, HWND.Null, 0, 0).Value > 0)
            {
                if (msg.message == WM_HOTKEY && msg.wParam.Value == HOTKEY_ID)
                {
                    _onHotkey();
                }

                PInvoke.TranslateMessage(msg);
                PInvoke.DispatchMessage(msg);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
        finally
        {
            PInvoke.UnregisterHotKey(HWND.Null, HOTKEY_ID);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_threadId != 0)
        {
            PInvoke.PostThreadMessage(_threadId, PInvoke.WM_QUIT, default, default);
        }

        _thread.Join(TimeSpan.FromSeconds(1));
        _started.Dispose();
    }
}