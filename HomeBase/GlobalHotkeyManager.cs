using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

internal sealed class GlobalHotkey : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 1;

    private readonly Thread _thread;
    private uint _threadId;
    private CancellationTokenSource _cts = new();

    public GlobalHotkey(Action onHotkey)
    {

        // Ctrl + Space
        const HOT_KEY_MODIFIERS modifiers = HOT_KEY_MODIFIERS.MOD_CONTROL;
        const uint virtualKey = (uint)VIRTUAL_KEY.VK_SPACE;

        if (!PInvoke.RegisterHotKey(HWND.Null, HOTKEY_ID, modifiers, virtualKey))
        {
            throw new InvalidOperationException("Failed to register global hotkey.");
        }
        
        _thread = new Thread(() => RunMessageLoop(onHotkey,_cts.Token));
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.IsBackground = true;
        _thread.Start();
    }
    
    public void RunMessageLoop(Action onHotkey,CancellationToken token)
    {
        try
        {
            _threadId = PInvoke.GetCurrentThreadId();
            while (PInvoke.GetMessage(out MSG msg, HWND.Null, 0, 0).Value > 0)
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }

                if (msg.message == WM_HOTKEY && msg.wParam.Value == HOTKEY_ID)
                {
                    onHotkey();
                }

                PInvoke.TranslateMessage(msg);
                PInvoke.DispatchMessage(msg);
            }
        }
        catch { /* ignored */ }
    }
    
    public void Dispose()
    {
        // signal thread/message loop to stop
        _cts.Cancel();

        if (_threadId != 0)
        {
            PInvoke.PostThreadMessage(_threadId, PInvoke.WM_QUIT, default, default);
        }
        
        _thread.Join(TimeSpan.FromSeconds(1));
        
        // unregister hotkey
        PInvoke.UnregisterHotKey(HWND.Null, HOTKEY_ID);
        
        _cts.Dispose();
    }
}