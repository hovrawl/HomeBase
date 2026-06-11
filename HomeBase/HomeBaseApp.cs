using System.Collections.Concurrent;
using System.Diagnostics;
using Windows.Win32.Foundation;
using HomeBase.Enums;
using HomeBase.Models;
using HomeBase.Render;
using HomeBase.Services;
using HomeBase.Windows;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using SkiaSharp;

namespace HomeBase;

public sealed class HomeBaseApp : IDisposable
{
    private readonly StorageService _storageService = new();
    private readonly ConcurrentQueue<AppAction> _actionQueue = new();

    private IWindow? _window;
    private IInputContext? _input;
    private GRGlInterface? _grGlInterface;
    private GRContext? _grContext;
    private GRBackendRenderTarget? _renderTarget;
    private SKSurface? _surface;
    private readonly RenderTheme _renderTheme;

    private readonly UIState _ui = new();
    private readonly InputState _inputState = new();
    private readonly DockRenderer _renderer;
    private readonly DockWindowAnimation _animation = new();
    private bool _disposed;
    private HWND _hwnd;
    private GlobalHotkey? _hotKey;

    public HomeBaseApp()
    {
        var savedThemes = _storageService.GetSavedThemes();
        var firstTheme = savedThemes.FirstOrDefault();
        
        _renderTheme = new RenderTheme(firstTheme);
        _renderer = new DockRenderer(_ui, _inputState, _storageService, _actionQueue, _renderTheme);
    }

    private List<StorageService.DockItem> DockItems => _storageService.Items;

    public void Run()
    {
        CreateWindow();
        InitializeWindow();
        _window!.Run();
    }

    private void InitializeWindow()
    {
        _window!.Load += WindowOnLoad;
        _window.Update += WindowOnUpdate;
        _window.Render += WindowOnRender;
        _window.FocusChanged += WindowOnFocusChanged;
        _window.Initialize();

        // Setup skia
        _grGlInterface = GRGlInterface.Create(name =>
            _window.GLContext!.TryGetProcAddress(name, out var addr) ? addr : IntPtr.Zero);
        _grGlInterface.Validate();
        
        _grContext = GRContext.CreateGl(_grGlInterface);
        if (_grContext == null)
        {
            throw new Exception("Failed to create skia context");
        }
        
        // Position before applying DWM effects as it forces a show
        if (_window.Monitor != null)
        {
            CalculateDockPositions();
            _window.Position = _animation.HiddenPosition;
            _ui.SetFramebufferScale(_window);
        }

        // Apply DWM effects
        if (_window.Native?.Win32 is { } win32Window)
        {
            _hwnd = new HWND(win32Window.Hwnd);
    
            WindowChrome.HideFromTaskbarAndAltTab(_hwnd);

            DwmWindowEffects.ApplyDockWindowEffects(_hwnd);
    
            TrayIcon.Add(_hwnd, 
                () =>
            {
                _window.Close();
            }, 
                () => _actionQueue.Enqueue(new AppAction.ShowAbout()));
        }
        
        // Setup windows low level hook
        
        _hotKey = new GlobalHotkey(() =>
        {
            // Summon/focus your window here.
            _actionQueue.Enqueue(new AppAction.SummonDock());
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

        if (_window == null)
        {
            throw new Exception("Failed to create window");
        }
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
                _ui.ScrollOffsetY -= scroll.Y * 40;
            };
        }
    }

    void WindowOnUpdate(double deltaTime)
    {
        // Initial layout
        if (!_ui.DidInitialLayout) WindowLayout();
        
        // Dock summon/hide animation
        var animatedPosition = _animation.Update(deltaTime);
        if (animatedPosition != null && animatedPosition != _window!.Position)
        {
            _window.Position = animatedPosition.Value;
        }
        
        //if (SummonCalled) SummonDock();
        
        // Update mouse state
        var mouse = _input.Mice.FirstOrDefault();
        if (mouse != null)
        {
            float scale = _ui.BufferScale;

            var mousePosition = new Vector2D<int>(
                (int)(mouse.Position.X * scale),
                (int)(mouse.Position.Y * scale));
            var leftMouseDown = mouse.IsButtonPressed(MouseButton.Left);
            var rightMouseDown = mouse.IsButtonPressed(MouseButton.Right);
        
            _inputState.UpdateMouse(mousePosition, leftMouseDown, rightMouseDown);
        }

        // Queued actions
        while (_actionQueue.TryDequeue(out AppAction? action))
        {
            HandleAction(action);
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
        _grContext.ResetContext();

        var canvas = _surface.Canvas;
        _renderer.Render(canvas);
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
            _actionQueue.Enqueue(new AppAction.DismissDock());
        }
    }
    
    #endregion

    #region Drawing

    void EnsureSkiaSurface()
    {
        var framebufferSize = _window.FramebufferSize;

        if (framebufferSize.X <= 0 || framebufferSize.Y <= 0)
        {
            return;
        }

        if (_surface is not null &&
            _renderTarget is not null &&
            framebufferSize == _ui.LastFramebufferSize)
        {
            return;
        }

        _surface?.Dispose();
        _surface = null;

        _renderTarget?.Dispose();

        _ui.LastFramebufferSize = framebufferSize;

        _renderTarget = new GRBackendRenderTarget(
            framebufferSize.X,
            framebufferSize.Y,
            0,
            8,
            new GRGlFramebufferInfo(0, 0x8058)); // 0x8058 = GL_RGBA8

        _surface = SKSurface.Create(
            _grContext,
            _renderTarget,
            GRSurfaceOrigin.BottomLeft,
            SKColorType.Rgba8888);
        if (_surface == null)
        {
            throw new Exception("Failed to create skia surface");
        }
    }


    void WindowLayout()
    {
        if (_window.Monitor == null) return;

        // Flag so it doesn't run again
        _ui.DidInitialLayout = true;
    
        CalculateDockPositions();
        _window.Position = _animation.HiddenPosition;
        _animation.Hide(_window.Position);
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

        _animation.ShownPosition = new Vector2D<int>(
            x,
            bottomY - _window.Size.Y - bottomPadding);

        _animation.HiddenPosition = new Vector2D<int>(
            x,
            bottomY + _window.Size.Y);
    }

    
    #endregion
    
    #region Methods

    private void HandleAction(AppAction action)
    {
        switch (action)
        {
            case AppAction.ShowAbout:
                AboutWindow.Show();
                break;
            case AppAction.OpenAddItemDialog:
                OpenAddItemDialog();
                break;
            case AppAction.CreateNote:
                CreateNote();
                break;
            case AppAction.CreateTaskList:
                CreateTaskList();
                break;
            case AppAction.RemoveDockItem remove:
                RemoveDockItem(remove.Index);
                break;
            case AppAction.OpenDockItem open:
                OnDockItemClicked(DockItems[open.Index]);
                break;
            case AppAction.SummonDock:
                SummonDock();
                break;
            case AppAction.DismissDock:
                DismissDock();
                break;
            case AppAction.OpenFile openFile:
                OpenLikeExplorer(openFile.Path);
                break;
            case AppAction.RenameItem rename:
                var item = DockItems[rename.Index];
                item.Name = rename.NewName;
                DockItems[rename.Index] = item;
                _storageService.Save();
                break;
        }
    }

    void KeyDown(IKeyboard keyboard, Key key, int keyCode)
{
    // Quit
    if (key == Key.Escape)
    {
        if (_ui.RenamingItemId != null)
        {
            _renderer.SaveRenaming();
            return;
        }
        _actionQueue.Enqueue(new AppAction.DismissDock());
        return;
    }

    if (_ui.RenamingItemId != null)
    {
        int index = int.Parse(_ui.RenamingItemId.Substring(10));
        var item = DockItems[index];

        if (key == Key.Enter)
        {
            _renderer.SaveRenaming();
            return;
        }
        
        if (key == Key.Left)
        {
            _ui.RenamingCaretIndex = Math.Max(0, _ui.RenamingCaretIndex - 1);
        }
        else if (key == Key.Right)
        {
            _ui.RenamingCaretIndex = Math.Min(item.Name.Length, _ui.RenamingCaretIndex + 1);
        }
        else if (key == Key.Backspace)
        {
            if (_ui.RenamingCaretIndex > 0)
            {
                item.Name = item.Name.Remove(_ui.RenamingCaretIndex - 1, 1);
                _ui.RenamingCaretIndex--;
                DockItems[index] = item;
            }
        }
        else if (key == Key.Delete)
        {
            if (_ui.RenamingCaretIndex < item.Name.Length)
            {
                item.Name = item.Name.Remove(_ui.RenamingCaretIndex, 1);
                DockItems[index] = item;
            }
        }
        return;
    }
    
    if (_ui.FocusedItemId != null)
    {
        int index = int.Parse(_ui.FocusedItemId.Substring(10));
        var item = DockItems[index];

        if (key == Key.Left)
        {
            _ui.CaretIndex = Math.Max(0, _ui.CaretIndex - 1);
        }
        else if (key == Key.Right)
        {
            int len = 0;
            if (item.Type == ItemType.Note) len = _storageService.GetNote(item.Value).Content.Length;
            else if (item.Type == ItemType.TaskList)
            {
                var list = _storageService.GetTaskList(item.Value);
                if (_ui.FocusedTaskListIndex >= 0 && _ui.FocusedTaskListIndex < list.Tasks.Count)
                    len = list.Tasks[_ui.FocusedTaskListIndex].Text.Length;
            }
            _ui.CaretIndex = Math.Min(len, _ui.CaretIndex + 1);
        }
        else if (key == Key.Up)
        {
            if (item.Type == ItemType.Note)
            {
                var note = _storageService.GetNote(item.Value);
                float cardSize = (80 - 16) * _ui.ItemScale;
                float textWidth = cardSize - 12 * _ui.ItemScale;
                using var paint = new SKPaint();
                using var font = new SKFont();
                font.Size = 11 * _ui.ItemScale;
                var visualLines = DockRenderer.GetNoteVisualLines(note.Content, textWidth, font, paint);
                
                int currentLineIdx = -1;
                for (int i = 0; i < visualLines.Count; i++)
                {
                    if (_ui.CaretIndex >= visualLines[i].startIdx && _ui.CaretIndex <= visualLines[i].startIdx + visualLines[i].text.Length)
                    {
                        currentLineIdx = i;
                        break;
                    }
                }
                
                if (currentLineIdx > 0)
                {
                    int col = _ui.CaretIndex - visualLines[currentLineIdx].startIdx;
                    var prevLine = visualLines[currentLineIdx - 1];
                    _ui.CaretIndex = prevLine.startIdx + Math.Min(col, prevLine.text.Length);
                }
                else
                {
                    _ui.CaretIndex = 0;
                }
            }
            else if (item.Type == ItemType.TaskList)
            {
                var list = _storageService.GetTaskList(item.Value);
                if (_ui.FocusedTaskListIndex > 0)
                {
                    _ui.FocusedTaskListIndex--;
                    _ui.CaretIndex = Math.Min(_ui.CaretIndex, list.Tasks[_ui.FocusedTaskListIndex].Text.Length);
                }
            }
        }
        else if (key == Key.Down)
        {
            if (item.Type == ItemType.Note)
            {
                var note = _storageService.GetNote(item.Value);
                float cardSize = (80 - 16) * _ui.ItemScale;
                float textWidth = cardSize - 12 * _ui.ItemScale;
                using var textPaint = new SKPaint();
                textPaint.IsAntialias = true;
                textPaint.Color = SKColors.Black;
                using var font = new SKFont();
                font.Size = 11 * _ui.ItemScale;
                var visualLines = DockRenderer.GetNoteVisualLines(note.Content, textWidth, font, textPaint);
                
                int currentLineIdx = -1;
                for (int i = 0; i < visualLines.Count; i++)
                {
                    if (_ui.CaretIndex >= visualLines[i].startIdx && _ui.CaretIndex <= visualLines[i].startIdx + visualLines[i].text.Length)
                    {
                        currentLineIdx = i;
                        break;
                    }
                }
                
                if (currentLineIdx != -1 && currentLineIdx < visualLines.Count - 1)
                {
                    int col = _ui.CaretIndex - visualLines[currentLineIdx].startIdx;
                    var nextLine = visualLines[currentLineIdx + 1];
                    _ui.CaretIndex = nextLine.startIdx + Math.Min(col, nextLine.text.Length);
                }
                else
                {
                    _ui.CaretIndex = note.Content.Length;
                }
            }
            else if (item.Type == ItemType.TaskList)
            {
                var list = _storageService.GetTaskList(item.Value);
                if (_ui.FocusedTaskListIndex < list.Tasks.Count - 1)
                {
                    _ui.FocusedTaskListIndex++;
                    _ui.CaretIndex = Math.Min(_ui.CaretIndex, list.Tasks[_ui.FocusedTaskListIndex].Text.Length);
                }
            }
        }
        else if (key == Key.Backspace)
        {
            if (item.Type == ItemType.Note)
            {
                var note = _storageService.GetNote(item.Value);
                if (_ui.CaretIndex > 0)
                {
                    note.Content = note.Content.Remove(_ui.CaretIndex - 1, 1);
                    _ui.CaretIndex--;
                    _storageService.SaveNote(note);
                }
            }
            else if (item.Type == ItemType.TaskList)
            {
                var list = _storageService.GetTaskList(item.Value);
                if (_ui.FocusedTaskListIndex >= 0 && _ui.FocusedTaskListIndex < list.Tasks.Count)
                {
                    var task = list.Tasks[_ui.FocusedTaskListIndex];
                    if (_ui.CaretIndex > 0)
                    {
                        task.Text = task.Text.Remove(_ui.CaretIndex - 1, 1);
                        _ui.CaretIndex--;
                        list.Tasks[_ui.FocusedTaskListIndex] = task;
                        _storageService.SaveTaskList(list);
                    }
                    else if (_ui.FocusedTaskListIndex > 0)
                    {
                        var prevTask = list.Tasks[_ui.FocusedTaskListIndex - 1];
                        int oldLen = prevTask.Text.Length;
                        prevTask.Text += task.Text;
                        list.Tasks[_ui.FocusedTaskListIndex - 1] = prevTask;
                        list.Tasks.RemoveAt(_ui.FocusedTaskListIndex);
                        _ui.FocusedTaskListIndex--;
                        _ui.CaretIndex = oldLen;
                        _storageService.SaveTaskList(list);
                    }
                }
            }
        }
        else if (key == Key.Delete)
        {
            if (item.Type == ItemType.Note)
            {
                var note = _storageService.GetNote(item.Value);
                if (_ui.CaretIndex < note.Content.Length)
                {
                    note.Content = note.Content.Remove(_ui.CaretIndex, 1);
                    _storageService.SaveNote(note);
                }
            }
            else if (item.Type == ItemType.TaskList)
            {
                var list = _storageService.GetTaskList(item.Value);
                if (_ui.FocusedTaskListIndex >= 0 && _ui.FocusedTaskListIndex < list.Tasks.Count)
                {
                    var task = list.Tasks[_ui.FocusedTaskListIndex];
                    if (_ui.CaretIndex < task.Text.Length)
                    {
                        task.Text = task.Text.Remove(_ui.CaretIndex, 1);
                        list.Tasks[_ui.FocusedTaskListIndex] = task;
                        _storageService.SaveTaskList(list);
                    }
                    else if (_ui.FocusedTaskListIndex < list.Tasks.Count - 1)
                    {
                        _storageService.SaveTaskList(list);
                    }
                    else if (_ui.FocusedTaskListIndex < list.Tasks.Count - 1)
                    {
                        var nextTask = list.Tasks[_ui.FocusedTaskListIndex + 1];
                        task.Text += nextTask.Text;
                        list.Tasks[_ui.FocusedTaskListIndex] = task;
                        list.Tasks.RemoveAt(_ui.FocusedTaskListIndex + 1);
                        _storageService.SaveTaskList(list);
                    }
                }
            }
        }
        else if (key == Key.Enter)
        {
            if (item.Type == ItemType.Note)
            {
                var note = _storageService.GetNote(item.Value);
                note.Content = note.Content.Insert(_ui.CaretIndex, "\n");
                _ui.CaretIndex++;
                _storageService.SaveNote(note);
            }
            else if (item.Type == ItemType.TaskList)
            {
                var list = _storageService.GetTaskList(item.Value);
                if (_ui.FocusedTaskListIndex < 0) _ui.FocusedTaskListIndex = list.Tasks.Count - 1;
                
                var currentTask = list.Tasks[_ui.FocusedTaskListIndex];
                var newText = currentTask.Text.Substring(_ui.CaretIndex);
                currentTask.Text = currentTask.Text.Substring(0, _ui.CaretIndex);
                list.Tasks[_ui.FocusedTaskListIndex] = currentTask;
                
                list.Tasks.Insert(_ui.FocusedTaskListIndex + 1, new StorageService.TaskItem(newText, false));
                _ui.FocusedTaskListIndex++;
                _ui.CaretIndex = 0;
                
                _storageService.SaveTaskList(list);
            }
        }
    }
}

    void KeyChar(IKeyboard keyboard, char character)
    {
        if (_ui.RenamingItemId != null)
        {
            int renameIndex = int.Parse(_ui.RenamingItemId.Substring(10));
            var renameItem = DockItems[renameIndex];
            renameItem.Name = renameItem.Name.Insert(_ui.RenamingCaretIndex, character.ToString());
            _ui.RenamingCaretIndex++;
            DockItems[renameIndex] = renameItem;
            return;
        }

        if (_ui.FocusedItemId == null) return;
        
        if (!_ui.FocusedItemId.StartsWith("dock-item-")) return;
        int index = int.Parse(_ui.FocusedItemId.Substring(10));
        if (index < 0 || index >= DockItems.Count) return;
        
        var item = DockItems[index];
        if (item.Type == ItemType.Note)
        {
            var note = _storageService.GetNote(item.Value);
            _ui.CaretIndex = Math.Clamp(_ui.CaretIndex, 0, note.Content.Length);
            note.Content = note.Content.Insert(_ui.CaretIndex, character.ToString());
            _ui.CaretIndex++;
            _storageService.SaveNote(note);
        }
        else if (item.Type == ItemType.TaskList)
        {
            var list = _storageService.GetTaskList(item.Value);
            if (list.Tasks.Count == 0)
            {
                list.Tasks.Add(new StorageService.TaskItem("", false));
                _ui.FocusedTaskListIndex = 0;
                _ui.CaretIndex = 0;
            }
            
            if (_ui.FocusedTaskListIndex < 0) _ui.FocusedTaskListIndex = list.Tasks.Count - 1;
            var task = list.Tasks[_ui.FocusedTaskListIndex];
            _ui.CaretIndex = Math.Clamp(_ui.CaretIndex, 0, task.Text.Length);
            task.Text = task.Text.Insert(_ui.CaretIndex, character.ToString());
            _ui.CaretIndex++;
            list.Tasks[_ui.FocusedTaskListIndex] = task;
            _storageService.SaveTaskList(list);
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
        var result = Windows.Windows.OpenFilePicker(HWND.Null);
        if (result == null)
            return;
    
        var dockItem = new StorageService.DockItem(result.Item1, result.Item2, ItemType.File);
        Debug.WriteLine($"Selected file: {dockItem.Name}, Path: {dockItem.Value}");
    
        DockItems.Add(dockItem);
        _storageService.Save();
    }

    void CreateNote()
    {
        string id = Guid.NewGuid().ToString();
        var note = new StorageService.Note(id, "");
        _storageService.SaveNote(note);
    
        var dockItem = new StorageService.DockItem("Note", id, ItemType.Note);
        DockItems.Add(dockItem);
        _storageService.Save();
    }

    void CreateTaskList()
    {
        string id = Guid.NewGuid().ToString();
        var taskList = new StorageService.TaskList(id, new List<StorageService.TaskItem>());
        _storageService.SaveTaskList(taskList);
    
        var dockItem = new StorageService.DockItem("Tasks", id, ItemType.TaskList);
        DockItems.Add(dockItem);
        _storageService.Save();
    }

    void RemoveDockItem(int index)
    {
        if (index >= 0 && index < DockItems.Count)
        {
            DockItems.RemoveAt(index);
            _storageService.Save();
        }
    }

    void SummonDock()
    {
        _window.TopMost = true;

        // Calculate will ensure anim positions are latest per monitor
        CalculateDockPositions();

        if (_animation.State == UIAnimationState.Hidden)
        {
            _window.Position = _animation.HiddenPosition;
        }
        
        _animation.Show(_window.Position);

        _window.Focus();
    }

    void DismissDock()
    {
        if (_ui.RenamingItemId != null)
        {
            _renderer.SaveRenaming();
        }

        _ui.ActiveElementId = null;
        _ui.IsAddMenuOpen = false;

        CalculateDockPositions();

        _animation.Hide(_window.Position);
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
    
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _hotKey.Dispose();
        _surface?.Dispose();
        _renderTarget.Dispose();
        _grContext.Dispose();
        _input.Dispose();

        TrayIcon.Remove();

        _window.Dispose();
    }
}