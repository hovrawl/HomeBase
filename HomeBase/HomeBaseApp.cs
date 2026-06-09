using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;
using HomeBase.Models;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using SkiaSharp;

namespace HomeBase;

public sealed class HomeBaseApp : IDisposable
{
    private readonly StorageService _storageService = new();
    private readonly ConcurrentQueue<Action> _actionQueue = new();

    private IWindow? _window;
    private IInputContext? _input;
    private GRGlInterface _grGlInterface;
    private GRContext? _grContext;
    private GRBackendRenderTarget? _renderTarget;
    private SKSurface? _surface;

    private readonly UIState _dockState = new();
    private readonly InputState _inputState = new();
    private readonly DockRenderer _renderer;

    private bool _disposed;
    private HWND hwnd;
    private GlobalHotkey _hotKey;
    
    public HomeBaseApp()
    {
        _renderer = new DockRenderer(_dockState, _inputState, _storageService, _actionQueue);
    }

    public void Run()
    {
        CreateWindow();
        InitializeWindow();
        _window!.Run();
    }

    private void InitializeWindow()
    {
        
        // Setup skia
        _grGlInterface = GRGlInterface.Create(name =>
            _window.GLContext!.TryGetProcAddress(name, out var addr) ? addr : IntPtr.Zero);

        _grGlInterface.Validate();

        _grContext = GRContext.CreateGl(_grGlInterface);
        
        _window.Load += WindowOnLoad;
        _window.Update += WindowOnUpdate;
        _window.Render += WindowOnRender;
        _window.FocusChanged += WindowOnFocusChanged;
        _window.Initialize();

        // Position before applying DWM effects as it forces a show
        if (_window.Monitor != null)
        {
            CalculateDockPositions();
            _window.Position = DockHiddenPosition;
        }

        // Apply DWM effects
        if (_window.Native?.Win32 is { } win32Window)
        {
            hwnd = new HWND(win32Window.Hwnd);
    
            WindowChrome.HideFromTaskbarAndAltTab(hwnd);

            DwmWindowEffects.ApplyDockWindowEffects(hwnd);
    
            TrayIcon.Add(hwnd, 
                () =>
            {
                _window.Close();
            }, 
                () => ActionQueue.Enqueue(AboutWindow.Show));
        }
        
        // Setup windows low level hook
        
        _hotKey = new GlobalHotkey(() =>
        {
            // Summon/focus your window here.
            SummonCalled = true;
        });
    }

    private void CreateWindow()
    {
        // setup window
        var windowOptions = WindowOptions.Default;
        windowOptions.Title = "HomeBase";
        windowOptions.TopMost = true;
        windowOptions.WindowBorder = WindowBorder.Hidden;
        windowOptions.WindowState = WindowState.Normal;
        windowOptions.WindowClass = "Hovrawl.HomeBase";
        windowOptions.PreferredStencilBufferBits = 8;
        windowOptions.PreferredBitDepth = new Vector4D<int>(8, 8, 8, 8);
        windowOptions.TransparentFramebuffer = true;

        _window = Window.Create(windowOptions);
    }


    #region Window Events

    void WindowOnLoad()
    {
        // context dependant initialization
        _input = _window.CreateInput();
        for (int i = 0; i < _input.Keyboards.Count; i++)
        {
            _input.Keyboards[i].KeyDown += KeyDown;
            _input.Keyboards[i].KeyChar += KeyChar;
        }

        for (int i = 0; i < _input.Mice.Count; i++)
        {
            _input.Mice[i].Scroll += (mouse, scroll) =>
            {
                ScrollOffsetY -= scroll.Y * 40;
            };
        }
    }

    void WindowOnUpdate(double deltaTime)
    {
        // Initial layout
        if (!didInitialLayout) WindowLayout();
        
        UpdateDockAnimation(deltaTime);
        
        if (SummonCalled) SummonDock();
        
        // Query Mouse Position
        var mouse = _input.Mice.FirstOrDefault();
        if (mouse != null)
        {
            bool previousLeftMouseDown = LeftMouseDown;
            bool previousRightMouseDown = RightMouseDown;

            // Get scaled mouse position
            float scale = GetFramebufferScale();

            MousePosition = new Vector2(
                mouse.Position.X * scale,
                mouse.Position.Y * scale);
            
            LeftMouseDown = mouse.IsButtonPressed(MouseButton.Left);
            LeftMousePressed = LeftMouseDown && !previousLeftMouseDown;
            LeftMouseReleased = !LeftMouseDown && previousLeftMouseDown;

            RightMouseDown = mouse.IsButtonPressed(MouseButton.Right);
            RightMousePressed = RightMouseDown && !previousRightMouseDown;
            RightMouseReleased = !RightMouseDown && previousRightMouseDown;
        }

        while (ActionQueue.TryDequeue(out Action? action))
        {
            action();
        }
    }

    void WindowOnRender(double deltaTime)
    {
        EnsureSkiaSurface();

        if (_surface is null)
        {
            return;
        }
        
        // window render loop
        // skia render code
        grContext.ResetContext();

        var canvas = _surface.Canvas;
        
        // Clear canvas with transparent background
        canvas.Clear(SKColors.Transparent);
        
        // draw background
        var bgRect = DrawBackground(canvas);
        
        DrawDock(canvas, bgRect);

        canvas.Flush();
    }
        
    void WindowOnFocusChanged(bool focussed)
    {
        if (focussed)
        {
            _window.TopMost = true;
        }
        else
        {
            // slide off screen
            DismissDock();
        }
    }
    
    #endregion

    #region Drawing

    float GetFramebufferScale()
    {
        if (_window.Size.X <= 0 || _window.FramebufferSize.X <= 0)
        {
            return 1.0f;
        }

        return _window.FramebufferSize.X / (float)_window.Size.X;
    }

    float LogicalToFramebufferPixels(float logicalPixels)
    {
        return logicalPixels * GetFramebufferScale();
    }
    
    void EnsureSkiaSurface()
    {
        var framebufferSize = _window.FramebufferSize;

        if (framebufferSize.X <= 0 || framebufferSize.Y <= 0)
        {
            return;
        }

        if (_surface is not null &&
            _renderTarget is not null &&
            framebufferSize == lastFramebufferSize)
        {
            return;
        }

        _surface?.Dispose();
        _surface = null;

        _renderTarget?.Dispose();
        _renderTarget = null;

        lastFramebufferSize = framebufferSize;

        _renderTarget = new GRBackendRenderTarget(
            framebufferSize.X,
            framebufferSize.Y,
            0,
            8,
            new GRGlFramebufferInfo(0, 0x8058)); // 0x8058 = GL_RGBA8

        _surface = SKSurface.Create(
            grContext,
            _renderTarget,
            GRSurfaceOrigin.BottomLeft,
            SKColorType.Rgba8888);
    }

    void WindowLayout()
    {
        if (_window.Monitor == null) return;

        // Flag so it doesn't run again
        didInitialLayout = true;
    
        CalculateDockPositions();
        _window.Position = DockHiddenPosition;
        uiState = UIAnimationState.Hidden;
    }

    void CalculateDockPositions()
    {
        if (_window.Monitor == null) return;

        var monitorSize = _window.Monitor.Bounds.Size;
        _window.Size = new Vector2D<int>(monitorSize.X / 3, monitorSize.Y / 3);
        var centerX = monitorSize.X / 2;
        var bottomY = monitorSize.Y;
        var bottomPadding = 4;

        int x = centerX - _window.Size.X / 2;

        DockShownPosition = new Vector2D<int>(
            x,
            bottomY - _window.Size.Y - bottomPadding);

        DockHiddenPosition = new Vector2D<int>(
            x,
            bottomY + _window.Size.Y);
    }

    void UpdateDockAnimation(double deltaTime)
    {
        if (uiState != UIAnimationState.Showing &&
            uiState != UIAnimationState.Hiding)
        {
            return;
        }

        DockAnimationTime += (float)deltaTime;

        float t = DockAnimationTime / DockAnimationDuration;
        t = Math.Clamp(t, 0f, 1f);

        float easedT = uiState == UIAnimationState.Showing
            ? EaseOutCubic(t)
            : EaseInCubic(t);

        Vector2 position = Lerp(
            DockAnimationStartPosition,
            DockAnimationTargetPosition,
            easedT);

        _window.Position = new Vector2D<int>(
            (int)MathF.Round(position.X),
            (int)MathF.Round(position.Y));

        if (t >= 1f)
        {
            _window.Position = new Vector2D<int>(
                (int)MathF.Round(DockAnimationTargetPosition.X),
                (int)MathF.Round(DockAnimationTargetPosition.Y));

            if (uiState == UIAnimationState.Hiding)
            {
                uiState = UIAnimationState.Hidden;
                _window.TopMost = false;
            }
            else
            {
                uiState = UIAnimationState.Shown;
            }
        }
    }
    #endregion
    
    #region Methods
    void KeyDown(IKeyboard keyboard, Key key, int keyCode)
{
    // Quit
    if (key == Key.Escape)
    {
        if (RenamingItemId != null)
        {
            SaveRenaming();
            return;
        }
        DismissDock();
        return;
    }

    if (RenamingItemId != null)
    {
        int index = int.Parse(RenamingItemId.Substring(10));
        var item = DockItems[index];

        if (key == Key.Enter)
        {
            SaveRenaming();
            return;
        }
        
        if (key == Key.Left)
        {
            RenamingCaretIndex = Math.Max(0, RenamingCaretIndex - 1);
        }
        else if (key == Key.Right)
        {
            RenamingCaretIndex = Math.Min(item.Name.Length, RenamingCaretIndex + 1);
        }
        else if (key == Key.Backspace)
        {
            if (RenamingCaretIndex > 0)
            {
                item.Name = item.Name.Remove(RenamingCaretIndex - 1, 1);
                RenamingCaretIndex--;
                DockItems[index] = item;
            }
        }
        else if (key == Key.Delete)
        {
            if (RenamingCaretIndex < item.Name.Length)
            {
                item.Name = item.Name.Remove(RenamingCaretIndex, 1);
                DockItems[index] = item;
            }
        }
        return;
    }
    
    if (FocusedItemId != null)
    {
        int index = int.Parse(FocusedItemId.Substring(10));
        var item = DockItems[index];

        if (key == Key.Left)
        {
            CaretIndex = Math.Max(0, CaretIndex - 1);
        }
        else if (key == Key.Right)
        {
            int len = 0;
            if (item.Type == ItemType.Note) len = GetNote(item.Value).Content.Length;
            else if (item.Type == ItemType.TaskList)
            {
                var list = GetTaskList(item.Value);
                if (FocusedTaskListIndex >= 0 && FocusedTaskListIndex < list.Tasks.Count)
                    len = list.Tasks[FocusedTaskListIndex].Text.Length;
            }
            CaretIndex = Math.Min(len, CaretIndex + 1);
        }
        else if (key == Key.Up)
        {
            if (item.Type == ItemType.Note)
            {
                var note = GetNote(item.Value);
                float cardSize = (80 - 16) * ItemScale;
                float textWidth = cardSize - 12 * ItemScale;
                using var paint = new SKPaint { TextSize = 11 * ItemScale };
                var visualLines = GetNoteVisualLines(note.Content, textWidth, paint);
                
                int currentLineIdx = -1;
                for (int i = 0; i < visualLines.Count; i++)
                {
                    if (CaretIndex >= visualLines[i].startIdx && CaretIndex <= visualLines[i].startIdx + visualLines[i].text.Length)
                    {
                        currentLineIdx = i;
                        break;
                    }
                }
                
                if (currentLineIdx > 0)
                {
                    int col = CaretIndex - visualLines[currentLineIdx].startIdx;
                    var prevLine = visualLines[currentLineIdx - 1];
                    CaretIndex = prevLine.startIdx + Math.Min(col, prevLine.text.Length);
                }
                else
                {
                    CaretIndex = 0;
                }
            }
            else if (item.Type == ItemType.TaskList)
            {
                var list = GetTaskList(item.Value);
                if (FocusedTaskListIndex > 0)
                {
                    FocusedTaskListIndex--;
                    CaretIndex = Math.Min(CaretIndex, list.Tasks[FocusedTaskListIndex].Text.Length);
                }
            }
        }
        else if (key == Key.Down)
        {
            if (item.Type == ItemType.Note)
            {
                var note = GetNote(item.Value);
                float cardSize = (80 - 16) * ItemScale;
                float textWidth = cardSize - 12 * ItemScale;
                using var paint = new SKPaint { TextSize = 11 * ItemScale };
                var visualLines = GetNoteVisualLines(note.Content, textWidth, paint);
                
                int currentLineIdx = -1;
                for (int i = 0; i < visualLines.Count; i++)
                {
                    if (CaretIndex >= visualLines[i].startIdx && CaretIndex <= visualLines[i].startIdx + visualLines[i].text.Length)
                    {
                        currentLineIdx = i;
                        break;
                    }
                }
                
                if (currentLineIdx != -1 && currentLineIdx < visualLines.Count - 1)
                {
                    int col = CaretIndex - visualLines[currentLineIdx].startIdx;
                    var nextLine = visualLines[currentLineIdx + 1];
                    CaretIndex = nextLine.startIdx + Math.Min(col, nextLine.text.Length);
                }
                else
                {
                    CaretIndex = note.Content.Length;
                }
            }
            else if (item.Type == ItemType.TaskList)
            {
                var list = GetTaskList(item.Value);
                if (FocusedTaskListIndex < list.Tasks.Count - 1)
                {
                    FocusedTaskListIndex++;
                    CaretIndex = Math.Min(CaretIndex, list.Tasks[FocusedTaskListIndex].Text.Length);
                }
            }
        }
        else if (key == Key.Backspace)
        {
            if (item.Type == ItemType.Note)
            {
                var note = GetNote(item.Value);
                if (CaretIndex > 0)
                {
                    note.Content = note.Content.Remove(CaretIndex - 1, 1);
                    CaretIndex--;
                    _noteCache[item.Value] = note;
                    storageService.SaveNote(note);
                }
            }
            else if (item.Type == ItemType.TaskList)
            {
                var list = GetTaskList(item.Value);
                if (FocusedTaskListIndex >= 0 && FocusedTaskListIndex < list.Tasks.Count)
                {
                    var task = list.Tasks[FocusedTaskListIndex];
                    if (CaretIndex > 0)
                    {
                        task.Text = task.Text.Remove(CaretIndex - 1, 1);
                        CaretIndex--;
                        list.Tasks[FocusedTaskListIndex] = task;
                        _taskListCache[item.Value] = list;
                        storageService.SaveTaskList(list);
                    }
                    else if (FocusedTaskListIndex > 0)
                    {
                        var prevTask = list.Tasks[FocusedTaskListIndex - 1];
                        int oldLen = prevTask.Text.Length;
                        prevTask.Text += task.Text;
                        list.Tasks[FocusedTaskListIndex - 1] = prevTask;
                        list.Tasks.RemoveAt(FocusedTaskListIndex);
                        FocusedTaskListIndex--;
                        CaretIndex = oldLen;
                        _taskListCache[item.Value] = list;
                        storageService.SaveTaskList(list);
                    }
                }
            }
        }
        else if (key == Key.Delete)
        {
            if (item.Type == ItemType.Note)
            {
                var note = GetNote(item.Value);
                if (CaretIndex < note.Content.Length)
                {
                    note.Content = note.Content.Remove(CaretIndex, 1);
                    _noteCache[item.Value] = note;
                    storageService.SaveNote(note);
                }
            }
            else if (item.Type == ItemType.TaskList)
            {
                var list = GetTaskList(item.Value);
                if (FocusedTaskListIndex >= 0 && FocusedTaskListIndex < list.Tasks.Count)
                {
                    var task = list.Tasks[FocusedTaskListIndex];
                    if (CaretIndex < task.Text.Length)
                    {
                        task.Text = task.Text.Remove(CaretIndex, 1);
                        list.Tasks[FocusedTaskListIndex] = task;
                        _taskListCache[item.Value] = list;
                        storageService.SaveTaskList(list);
                    }
                    else if (FocusedTaskListIndex < list.Tasks.Count - 1)
                    {
                        var nextTask = list.Tasks[FocusedTaskListIndex + 1];
                        task.Text += nextTask.Text;
                        list.Tasks[FocusedTaskListIndex] = task;
                        list.Tasks.RemoveAt(FocusedTaskListIndex + 1);
                        _taskListCache[item.Value] = list;
                        storageService.SaveTaskList(list);
                    }
                }
            }
        }
        else if (key == Key.Enter)
        {
            if (item.Type == ItemType.Note)
            {
                var note = GetNote(item.Value);
                note.Content = note.Content.Insert(CaretIndex, "\n");
                CaretIndex++;
                _noteCache[item.Value] = note;
                storageService.SaveNote(note);
            }
            else if (item.Type == ItemType.TaskList)
            {
                var list = GetTaskList(item.Value);
                if (FocusedTaskListIndex < 0) FocusedTaskListIndex = list.Tasks.Count - 1;
                
                var currentTask = list.Tasks[FocusedTaskListIndex];
                var newText = currentTask.Text.Substring(CaretIndex);
                currentTask.Text = currentTask.Text.Substring(0, CaretIndex);
                list.Tasks[FocusedTaskListIndex] = currentTask;
                
                list.Tasks.Insert(FocusedTaskListIndex + 1, new StorageService.TaskItem(newText, false));
                FocusedTaskListIndex++;
                CaretIndex = 0;
                
                _taskListCache[item.Value] = list;
                storageService.SaveTaskList(list);
            }
        }
    }
}

    void KeyChar(IKeyboard keyboard, char character)
    {
        if (RenamingItemId != null)
        {
            int renameIndex = int.Parse(RenamingItemId.Substring(10));
            var renameItem = DockItems[renameIndex];
            renameItem.Name = renameItem.Name.Insert(RenamingCaretIndex, character.ToString());
            RenamingCaretIndex++;
            DockItems[renameIndex] = renameItem;
            return;
        }

        if (FocusedItemId == null) return;
        
        if (!FocusedItemId.StartsWith("dock-item-")) return;
        int index = int.Parse(FocusedItemId.Substring(10));
        if (index < 0 || index >= DockItems.Count) return;
        
        var item = DockItems[index];
        if (item.Type == ItemType.Note)
        {
            var note = GetNote(item.Value);
            CaretIndex = Math.Clamp(CaretIndex, 0, note.Content.Length);
            note.Content = note.Content.Insert(CaretIndex, character.ToString());
            CaretIndex++;
            _noteCache[item.Value] = note;
            storageService.SaveNote(note);
        }
        else if (item.Type == ItemType.TaskList)
        {
            var list = GetTaskList(item.Value);
            if (list.Tasks.Count == 0)
            {
                list.Tasks.Add(new StorageService.TaskItem("", false));
                FocusedTaskListIndex = 0;
                CaretIndex = 0;
            }
            
            if (FocusedTaskListIndex < 0) FocusedTaskListIndex = list.Tasks.Count - 1;
            var task = list.Tasks[FocusedTaskListIndex];
            CaretIndex = Math.Clamp(CaretIndex, 0, task.Text.Length);
            task.Text = task.Text.Insert(CaretIndex, character.ToString());
            CaretIndex++;
            list.Tasks[FocusedTaskListIndex] = task;
            _taskListCache[item.Value] = list;
            storageService.SaveTaskList(list);
        }
    }

    void OpenLikeExplorer(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        if (!File.Exists(path))
            return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open '{path}': {ex.Message}");
        }
    }

    void OpenAddItemDialog()
    {
        // show file picker dialog
        var result = OpenFilePicker(HWND.Null);
        if (result == null)
            return;
    
        var dockItem = new StorageService.DockItem(result.Item1, result.Item2, ItemType.File);
        Debug.WriteLine($"Selected file: {dockItem.Name}, Path: {dockItem.Value}");
    
        DockItems.Add(dockItem);
        StorageService.Instance.Save(DockItems);
    }

    void CreateNote()
    {
        string id = Guid.NewGuid().ToString();
        var note = new StorageService.Note(id, "");
        storageService.SaveNote(note);
        _noteCache[id] = note;
    
        var dockItem = new StorageService.DockItem("Note", id, ItemType.Note);
        DockItems.Add(dockItem);
        StorageService.Instance.Save(DockItems);
    }

    void CreateTaskList()
    {
        string id = Guid.NewGuid().ToString();
        var taskList = new StorageService.TaskList(id, new List<StorageService.TaskItem>());
        storageService.SaveTaskList(taskList);
        _taskListCache[id] = taskList;
    
        var dockItem = new StorageService.DockItem("Tasks", id, ItemType.TaskList);
        DockItems.Add(dockItem);
        StorageService.Instance.Save(DockItems);
    }

    void RemoveDockItem(int index)
    {
        if (index >= 0 && index < DockItems.Count)
        {
            DockItems.RemoveAt(index);
            StorageService.Instance.Save(DockItems);
        }
    }

    void SummonDock()
    {
        _window.TopMost = true;

        CalculateDockPositions();

        if (uiState == UIAnimationState.Hidden)
        {
            _window.Position = DockHiddenPosition;
        }

        DockAnimationStartPosition = new Vector2(
            _window.Position.X,
            _window.Position.Y);

        DockAnimationTargetPosition = new Vector2(
            DockShownPosition.X,
            DockShownPosition.Y);

        DockAnimationTime = 0f;
        uiState = UIAnimationState.Showing;

        _window.Focus();

        SummonCalled = false;
    }

    void DismissDock()
    {
        if (RenamingItemId != null)
        {
            SaveRenaming();
        }

        ActiveElementId = null;
        IsAddMenuOpen = false;

        CalculateDockPositions();

        DockAnimationStartPosition = new Vector2(
            _window.Position.X,
            _window.Position.Y);

        DockAnimationTargetPosition = new Vector2(
            DockHiddenPosition.X,
            DockHiddenPosition.Y);

        DockAnimationTime = 0f;
        uiState = UIAnimationState.Hiding;
    }

    SKImage? GetDockItemIcon(string path)
    {
        if (_iconCache.TryGetValue(path, out SKImage? image))
        {
            return image;
        }

        image = LoadIconImageForFile(path);
        

        if (image is not null)
        {
            _iconCache[path] = image;
        }

        return image;
    }

    unsafe SKImage? LoadIconImageForFile(string path)
    {
        // load image from filepath
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        const int requestedIconSize = 128;

        HRESULT hr = PInvoke.CoInitializeEx(
            null,
            COINIT.COINIT_APARTMENTTHREADED | COINIT.COINIT_DISABLE_OLE1DDE);

        bool shouldUninitialize = hr.Succeeded;

        HBITMAP bitmapHandle = HBITMAP.Null;

        try
        {
            Guid shellItemImageFactoryId = typeof(IShellItemImageFactory).GUID;

            hr = PInvoke.SHCreateItemFromParsingName(
                path,
                null,
                in shellItemImageFactoryId,
                out object imageFactoryObject);

            if (hr.Failed || imageFactoryObject is not IShellItemImageFactory imageFactory)
            {
                return null;
            }

            SIZE size = new()
            {
                cx = requestedIconSize,
                cy = requestedIconSize
            };

            try
            {
                imageFactory.GetImage(
                    size,
                    SIIGBF.SIIGBF_ICONONLY | SIIGBF.SIIGBF_BIGGERSIZEOK,
                    &bitmapHandle);
            }
            catch
            {
                return null;
            }

            if (bitmapHandle == HBITMAP.Null)
            {
                return null;
            }

            return CreateSkImageFromHBitmap(bitmapHandle);
        }
        finally
        {
            if (bitmapHandle != HBITMAP.Null)
            {
                _ = PInvoke.DeleteObject(bitmapHandle);
            }

            if (shouldUninitialize)
            {
                PInvoke.CoUninitialize();
            }
        }
    }

    unsafe SKImage? CreateSkImageFromHBitmap(HBITMAP bitmapHandle)
    {
        BITMAP bitmap = default;

        int objectSize = PInvoke.GetObject(
            new HGDIOBJ(bitmapHandle.Value),
            sizeof(BITMAP),
            &bitmap);

        if (objectSize == 0 || bitmap.bmWidth <= 0 || bitmap.bmHeight <= 0)
        {
            return null;
        }

        int width = bitmap.bmWidth;
        int height = bitmap.bmHeight;

        BITMAPINFO bitmapInfo = default;
        bitmapInfo.bmiHeader.biSize = (uint)sizeof(BITMAPINFOHEADER);
        bitmapInfo.bmiHeader.biWidth = width;
        bitmapInfo.bmiHeader.biHeight = -height;
        bitmapInfo.bmiHeader.biPlanes = 1;
        bitmapInfo.bmiHeader.biBitCount = 32;
        bitmapInfo.bmiHeader.biCompression = 0;

        int stride = width * 4;
        int byteCount = stride * height;
        byte[] pixels = new byte[byteCount];

        HDC screenDc = PInvoke.GetDC(HWND.Null);

        try
        {
            fixed (byte* pixelsPtr = pixels)
            {
                int scanLines = PInvoke.GetDIBits(
                    screenDc,
                    bitmapHandle,
                    0,
                    (uint)height,
                    pixelsPtr,
                    &bitmapInfo,
                    DIB_USAGE.DIB_RGB_COLORS);

                if (scanLines == 0)
                {
                    return null;
                }
            }
        }
        finally
        {
            _ = PInvoke.ReleaseDC(HWND.Null, screenDc);
        }

        SKImageInfo imageInfo = new(
            width,
            height,
            SKColorType.Bgra8888,
            SKAlphaType.Premul);

        using SKBitmap skBitmap = new(imageInfo);

        nint skiaPixels = skBitmap.GetPixels();

        if (skiaPixels == 0)
        {
            return null;
        }

        Marshal.Copy(pixels, 0, skiaPixels, byteCount);

        return SKImage.FromBitmap(skBitmap);
    }

    void OnDockItemClicked(StorageService.DockItem dockItem)
    {
        // click dock item
        // for programs we will launch
        // other bespoke items will have special actions
        
        switch(dockItem.Type)
        {
            case ItemType.File:
            {
                OpenLikeExplorer(dockItem.Value);
                break;
            }
            case ItemType.Note:
            {
                break;
            }
            case ItemType.TaskList:
            {
                break;
            }
        }
    }
    
    #endregion
    
    static float EaseOutCubic(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return 1f - MathF.Pow(1f - t, 3f);
    }

    static float EaseInCubic(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * t;
    }

    static Vector2 Lerp(Vector2 a, Vector2 b, float t)
    {
        return a + (b - a) * t;
    }
    
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _surface?.Dispose();
        _renderTarget?.Dispose();
        _grContext?.Dispose();
        _input?.Dispose();

        TrayIcon.Remove();

        _window?.Dispose();
    }
}