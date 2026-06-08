using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Windows.Win32;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using SkiaSharp;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;
using Silk.NET.GLFW;
using Silk.NET.Input.Glfw;
using Silk.NET.Windowing.Glfw;
using HomeBase;
using MouseButton = Silk.NET.Input.MouseButton;
using WindowState = Silk.NET.Windowing.WindowState;

// Window handle
HWND hwnd = HWND.Null;
// Skia
GRBackendRenderTarget? skiaRenderTarget = null;
SKSurface? skiaSurface = null;
Vector2D<int> lastFramebufferSize = new(0, 0);
IInputContext WindowInput = null;
bool IsAddMenuOpen = false;
SKRect AddMenuRect = SKRect.Empty;

bool IsContextMenuOpen = false;
SKRect ContextMenuRect = SKRect.Empty;
int ContextMenuItemIndex = -1;
Vector2 ContextMenuPosition = Vector2.Zero;

Dictionary<string, SKImage> _iconCache = new(StringComparer.OrdinalIgnoreCase);

// Mouse Globals
Vector2 MousePosition = new Vector2(0, 0);
bool LeftMouseDown = false;
bool LeftMousePressed = false;
bool LeftMouseReleased = false;
bool RightMouseDown = false;
bool RightMousePressed = false;
bool RightMouseReleased = false;

string? ActiveElementId = null;
string? FocusedItemId = null;
float ItemScale = 1.2f; // Default slightly bigger as requested
float ScrollOffsetY = 0;
float TotalContentHeight = 0;

Queue<Action> ActionQueue = new();

// State bools
bool didInitialLayout = false;
bool SummonCalled = false;
ViewMode CurrentViewMode = ViewMode.All;

//Storage
StorageService storageService = new StorageService();
List<StorageService.DockItem> DockItems = storageService.Load();

Dictionary<string, StorageService.Note> _noteCache = new();
Dictionary<string, StorageService.TaskList> _taskListCache = new();

GlfwWindowing.Use();
GlfwInput.RegisterPlatform();

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

IWindow window = Window.Create(windowOptions);

window.Load += WindowOnLoad;


window.Initialize();

// Apply DWM effects
if (window.Native?.Win32 is { } win32Window)
{
    hwnd = new HWND(win32Window.Hwnd);
    
    WindowChrome.HideFromTaskbarAndAltTab(hwnd);

    DwmWindowEffects.ApplyDockWindowEffects(hwnd);
    
    TrayIcon.Add(hwnd, () =>
    {
        window.Close();
    });
}

// Setup skia
using var grGlInterface = GRGlInterface.Create(name =>
    window.GLContext!.TryGetProcAddress(name, out var addr) ? addr : IntPtr.Zero);

grGlInterface.Validate();

using var grContext = GRContext.CreateGl(grGlInterface);


window.Update += WindowOnUpdate;
window.Render += WindowOnRender;
window.FocusChanged += WindowOnFocusChanged;

// Setup windows low level hook
GlobalHotkey.Start(() =>
{
    // Summon/focus your window here.
    SummonCalled = true;
});



window.Run();

TrayIcon.Remove();

foreach (SKImage iconImage in _iconCache.Values)
{
    iconImage.Dispose();
}

_iconCache.Clear();

skiaRenderTarget?.Dispose();
skiaSurface?.Dispose();


#region WindowEvents
void WindowOnLoad()
{
    // context dependant initialization
    WindowInput = window.CreateInput();
    for (int i = 0; i < WindowInput.Keyboards.Count; i++)
    {
        WindowInput.Keyboards[i].KeyDown += KeyDown;
        WindowInput.Keyboards[i].KeyChar += KeyChar;
    }

    for (int i = 0; i < WindowInput.Mice.Count; i++)
    {
        WindowInput.Mice[i].Scroll += (mouse, scroll) =>
        {
            ScrollOffsetY -= scroll.Y * 40;
        };
    }
}


void WindowOnUpdate(double deltaTime)
{
    // Initial layout
    if (!didInitialLayout) WindowLayout();
    
    if (SummonCalled) SummonDock();
    
    // Query Mouse Position
    var mouse = WindowInput.Mice.FirstOrDefault();
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

    if (skiaSurface is null)
    {
        return;
    }
    
    // window render loop
    // skia render code
    grContext.ResetContext();

    var canvas = skiaSurface.Canvas;
    
    // Clear canvas with transparent background
    canvas.Clear(SKColors.Transparent);
    
    // draw background
    var bgRect = DrawBackground(canvas);
    
    DrawDock(canvas, bgRect);

    canvas.Flush();
}
#endregion

void WindowOnFocusChanged(bool focussed)
{
    // When focussed make it the top most, otherwise we allow to sink 
    window.TopMost = focussed;
    if (!focussed)
    {
        // slide off screen
        DismissDock();
    }
}

#region Drawing code

float GetFramebufferScale()
{
    if (window.Size.X <= 0 || window.FramebufferSize.X <= 0)
    {
        return 1.0f;
    }

    return window.FramebufferSize.X / (float)window.Size.X;
}

float LogicalToFramebufferPixels(float logicalPixels)
{
    return logicalPixels * GetFramebufferScale();
}

void WindowLayout()
{
    if (window.Monitor == null) return;

    // Flag so it doesn't run again
    didInitialLayout = true;
    

    var monitorSize = window.Monitor.Bounds.Size;
    window.Size = new Vector2D<int>(monitorSize.X / 3, monitorSize.Y / 3);
    var centerX = monitorSize.X / 2; // middle of screen
    var bottomY = monitorSize.Y; // bottom of screen
    var bottomPadding = 4; // lift window up slightly
    
    // set new window options for new default position that we will interpolate up from
    // use bottom of screen as window will move off screen
    // windowOptions.Position = new Vector2D<int>(centerX - window.Size.X / 2, bottomY);

    // minus half our window width so the center point is the centre of the window.
    // minus entire window height so bottom of the window is where the desired point is
    window.Position = new Vector2D<int>(centerX - window.Size.X / 2, bottomY - window.Size.Y - bottomPadding);
}

void EnsureSkiaSurface()
{
    var framebufferSize = window.FramebufferSize;

    if (framebufferSize.X <= 0 || framebufferSize.Y <= 0)
    {
        return;
    }

    if (skiaSurface is not null &&
        skiaRenderTarget is not null &&
        framebufferSize == lastFramebufferSize)
    {
        return;
    }

    skiaSurface?.Dispose();
    skiaSurface = null;

    skiaRenderTarget?.Dispose();
    skiaRenderTarget = null;

    lastFramebufferSize = framebufferSize;

    skiaRenderTarget = new GRBackendRenderTarget(
        framebufferSize.X,
        framebufferSize.Y,
        0,
        8,
        new GRGlFramebufferInfo(0, 0x8058)); // 0x8058 = GL_RGBA8

    skiaSurface = SKSurface.Create(
        grContext,
        skiaRenderTarget,
        GRSurfaceOrigin.BottomLeft,
        SKColorType.Rgba8888);
}

SKRect DrawBackground(SKCanvas canvas)
{
    var bounds = canvas.LocalClipBounds;
    
    var panelRect = new SKRect(
        0.5f,
        0.5f,
        bounds.Width - 0.5f,
        bounds.Height - 0.5f);

    using var panelPaint = new SKPaint
    {
        IsAntialias = true,
        Color = new SKColor(242, 242, 242, 232)
    };

    float radius = LogicalToFramebufferPixels(12);
    canvas.DrawRoundRect(panelRect, radius, radius, panelPaint);

    using var borderPaint = new SKPaint
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 1,
        Color = new SKColor(255, 255, 255, 120)
    };

    canvas.DrawRoundRect(panelRect, radius, radius, borderPaint);

    using var innerBorderPaint = new SKPaint
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 1,
        Color = new SKColor(0, 0, 0, 24)
    };

    var innerRect = new SKRect(
        panelRect.Left + 1,
        panelRect.Top + 1,
        panelRect.Right - 1,
        panelRect.Bottom - 1);

    canvas.DrawRoundRect(innerRect, radius, radius, innerBorderPaint);

    return panelRect;
}

void DrawDock(SKCanvas canvas, SKRect panelRect)
{
    // Items Viewport
    var viewportRect = new SKRect(
        panelRect.Left,
        panelRect.Top + 16,
        panelRect.Right,
        panelRect.Bottom - 80);

    // Clamp scroll
    float maxScroll = Math.Max(0, TotalContentHeight - viewportRect.Bottom);
    ScrollOffsetY = Math.Clamp(ScrollOffsetY, 0, maxScroll);

    canvas.Save();
    canvas.ClipRect(viewportRect);
    canvas.Translate(0, -ScrollOffsetY);

    var originalMousePos = MousePosition;
    if (viewportRect.Contains(originalMousePos.X, originalMousePos.Y))
    {
        MousePosition = new Vector2(originalMousePos.X, originalMousePos.Y + ScrollOffsetY);
    }
    else
    {
        // Move mouse away so items don't hover
        MousePosition = new Vector2(-10000, -10000);
    }

    TotalContentHeight = DrawDockItems(canvas, panelRect);

    MousePosition = originalMousePos;
    canvas.Restore();
    
    // Footer
    using var footerPaint = new SKPaint
    {
        IsAntialias = true,
        Color = new SKColor(255, 255, 255, 105)
    };

    var footerRect = new SKRect(
        panelRect.Left + 16,
        panelRect.Bottom - 72,
        panelRect.Right - 16,
        panelRect.Bottom - 16);

    canvas.DrawRoundRect(footerRect, 16, 16, footerPaint);

    // View Mode Toggle
    var modeBtnRect = new SKRect(footerRect.Left + 12, footerRect.Top + 12, footerRect.Left + 120, footerRect.Bottom - 12);
    if (TextButton(canvas, modeBtnRect, CurrentViewMode == ViewMode.All ? "Mode: All" : "Mode: Grouped", "view-mode-btn"))
    {
        CurrentViewMode = CurrentViewMode == ViewMode.All ? ViewMode.Grouped : ViewMode.All;
    }

    // Scale Controls
    var scaleLabelRect = new SKRect(modeBtnRect.Right + 12, footerRect.Top, modeBtnRect.Right + 80, footerRect.Bottom);
    using var labelPaint = new SKPaint { IsAntialias = true, Color = SKColors.DimGray };
    using var labelFont = new SKFont { Size = 12, Typeface = SKTypeface.FromFamilyName("Segoe UI") };
    canvas.DrawText($"Size: {(int)(ItemScale * 100)}%", scaleLabelRect.Left, footerRect.MidY + 5, labelFont, labelPaint);

    var minusBtnRect = new SKRect(scaleLabelRect.Right, footerRect.Top + 12, scaleLabelRect.Right + 32, footerRect.Bottom - 12);
    if (TextButton(canvas, minusBtnRect, "-", "scale-minus-btn"))
    {
        ItemScale = Math.Max(0.5f, ItemScale - 0.1f);
    }

    var plusBtnRect = new SKRect(minusBtnRect.Right + 4, footerRect.Top + 12, minusBtnRect.Right + 36, footerRect.Bottom - 12);
    if (TextButton(canvas, plusBtnRect, "+", "scale-plus-btn"))
    {
        ItemScale = Math.Min(3.0f, ItemScale + 0.1f);
    }
    
    var addButtonRect = new SKRect(
        footerRect.Right - 56,
        footerRect.Top + 8,
        footerRect.Right - 8,
        footerRect.Bottom - 8);
    
    var addClicked = AddButton(canvas, addButtonRect, "add-item");
    if (addClicked)
    {
        IsAddMenuOpen = !IsAddMenuOpen;
    }

    AddMenuRect = GetAddMenuRect(addButtonRect);

    // Extra hit test to hide menu if we clicked elsewhere
    if (LeftMouseReleased && !addClicked)
    {
        if (IsAddMenuOpen && !AddMenuRect.Contains(MousePosition.X, MousePosition.Y))
        {
            IsAddMenuOpen = false;
            ActiveElementId = null;
        }
    }

    // Ensure we have the context menu rect
    ContextMenuRect = GetContextMenuRect(ContextMenuPosition);

    // Extra hit test to hide context menu if we clicked elsewhere
    if (IsContextMenuOpen &&
        (LeftMouseReleased || RightMouseReleased) &&
        !ContextMenuRect.Contains(MousePosition.X, MousePosition.Y))
    {
        IsContextMenuOpen = false;
        ActiveElementId = null;
    }
    
    if (IsAddMenuOpen)
    {
        string? selectedAddMenuItem = DrawAddMenu(canvas, addButtonRect);

        if (selectedAddMenuItem == "Item")
        {
            IsAddMenuOpen = false;
            ActionQueue.Enqueue(OpenAddItemDialog);
        }
        else if (selectedAddMenuItem == "Note")
        {
            IsAddMenuOpen = false;
            ActionQueue.Enqueue(CreateNote);
        }
        else if (selectedAddMenuItem == "TaskList")
        {
            IsAddMenuOpen = false;
            ActionQueue.Enqueue(CreateTaskList);
        }    
    }

    if (IsContextMenuOpen)
    {
        string? selectedContextAction = DrawContextMenu(canvas);

        if (selectedContextAction == "Remove")
        {
            IsContextMenuOpen = false;
            int indexToRemove = ContextMenuItemIndex;
            ActionQueue.Enqueue(() => RemoveDockItem(indexToRemove));
        }
    }
}

float DrawDockItems(SKCanvas canvas, SKRect dockPanel)
{
    float currentY = 16;
    if (CurrentViewMode == ViewMode.All)
    {
        using var groupTitlePaint = new SKPaint { IsAntialias = true, Color = SKColors.DimGray };
        using var groupTitleFont = new SKFont { Size = 14, Typeface = SKTypeface.FromFamilyName("Segoe UI Semibold") };
        
        canvas.DrawText("All Items", dockPanel.Left + 32, dockPanel.Top + currentY + 20, groupTitleFont, groupTitlePaint);
        currentY += 30;

        currentY += DrawItemsList(canvas, dockPanel, DockItems.Select((item, index) => (item, index)).ToList(), currentY);
    }
    else
    {
        var groups = DockItems.Select((item, index) => (item, index))
            .GroupBy(x => x.item.Type)
            .OrderBy(g => g.Key);
        
        foreach (var group in groups)
        {
            string groupName = group.Key switch
            {
                ItemType.File => "Files",
                ItemType.Note => "Notes",
                ItemType.TaskList => "Task Lists",
                _ => group.Key.ToString()
            };

            using var groupTitlePaint = new SKPaint { IsAntialias = true, Color = SKColors.DimGray };
            using var groupTitleFont = new SKFont { Size = 14, Typeface = SKTypeface.FromFamilyName("Segoe UI Semibold") };
            
            canvas.DrawText(groupName, dockPanel.Left + 32, dockPanel.Top + currentY + 20, groupTitleFont, groupTitlePaint);
            currentY += 30;

            currentY += DrawItemsList(canvas, dockPanel, group.ToList(), currentY);
            currentY += 20;
        }
    }
    return currentY + dockPanel.Top;
}

float DrawItemsList(SKCanvas canvas, SKRect dockPanel, List<(StorageService.DockItem item, int originalIndex)> items, float startY)
{
    const float paddingX = 32;
    float baseCellSize = 80;
    float cellSize = baseCellSize * ItemScale;
    float cardSize = (baseCellSize - 16) * ItemScale;

    float availableWidth = dockPanel.Width - paddingX * 2;
    int columns = Math.Max(1, (int)Math.Floor(availableWidth / cellSize));
    int rows = (int)Math.Ceiling((float)items.Count / columns);

    bool anyHovered = false;

    for (int i = 0; i < items.Count; i++)
    {
        var (item, originalIndex) = items[i];
        int row = i / columns;
        int column = i % columns;

        float x = dockPanel.Left + paddingX + column * cellSize;
        float y = dockPanel.Top + startY + row * cellSize;

        var cardRect = new SKRect(x, y, x + cardSize, y + cardSize);
        if (cardRect.Contains(MousePosition.X, MousePosition.Y)) anyHovered = true;

        string stableId = $"dock-item-{originalIndex}";
        bool rightClicked = false;
        bool clicked = false;

        switch (item.Type)
        {
            case ItemType.File:
                clicked = DockItemButton(canvas, cardRect, item, stableId, out rightClicked);
                break;
            case ItemType.Note:
                clicked = NoteDockItem(canvas, cardRect, item, stableId, out rightClicked);
                break;
            case ItemType.TaskList:
                clicked = TaskListDockItem(canvas, cardRect, item, stableId, out rightClicked);
                break;
        }

        if (clicked)
        {
            if (item.Type == ItemType.File)
            {
                ActionQueue.Enqueue(() => OnDockItemClicked(item));
            }
            else
            {
                FocusedItemId = stableId;
            }
        }

        if (rightClicked)
        {
            IsContextMenuOpen = true;
            ContextMenuItemIndex = originalIndex;
            ContextMenuPosition = MousePosition;
            IsAddMenuOpen = false;
        }
    }

    if (LeftMouseReleased && !anyHovered && !IsAddMenuOpen && !ContextMenuRect.Contains(MousePosition.X, MousePosition.Y))
    {
        if (dockPanel.Contains(MousePosition.X, MousePosition.Y))
        {
            FocusedItemId = null;
        }
    }

    return rows * cellSize;
}

bool NoteDockItem(SKCanvas canvas, SKRect rect, StorageService.DockItem item, string id, out bool rightClicked)
{
    rightClicked = false;
    bool hovered = rect.Contains(MousePosition.X, MousePosition.Y);
    bool focused = FocusedItemId == id;

    if (hovered && (LeftMousePressed || RightMousePressed))
    {
        ActiveElementId = id;
    }

    bool active = ActiveElementId == id;
    bool pressed = active && (LeftMouseDown || RightMouseDown);
    bool clicked = active && hovered && LeftMouseReleased;
    rightClicked = active && hovered && RightMouseReleased;

    var bgColor = focused ? new SKColor(255, 255, 180) : new SKColor(255, 255, 220);
    if (pressed) bgColor = new SKColor(240, 240, 160);

    using var paint = new SKPaint { IsAntialias = true, Color = bgColor };
    float radius = 4 * ItemScale;
    canvas.DrawRoundRect(rect, radius, radius, paint);

    var note = GetNote(item.Value);
    using var textPaint = new SKPaint { IsAntialias = true, Color = SKColors.Black, TextSize = 11 * ItemScale };
    var textPadding = 6 * ItemScale;
    var textRect = new SKRect(rect.Left + textPadding, rect.Top + textPadding, rect.Right - textPadding, rect.Bottom - textPadding);
    DrawTextInRect(canvas, note.Content, textRect, textPaint);

    if (focused)
    {
        using var borderPaint = new SKPaint { IsAntialias = true, Color = new SKColor(255, 165, 0), Style = SKPaintStyle.Stroke, StrokeWidth = 2 * ItemScale };
        canvas.DrawRoundRect(rect, radius, radius, borderPaint);
    }

    if (active && (LeftMouseReleased || RightMouseReleased)) ActiveElementId = null;
    return clicked;
}

bool TaskListDockItem(SKCanvas canvas, SKRect rect, StorageService.DockItem item, string id, out bool rightClicked)
{
    rightClicked = false;
    bool hovered = rect.Contains(MousePosition.X, MousePosition.Y);
    bool focused = FocusedItemId == id;

    if (hovered && (LeftMousePressed || RightMousePressed))
    {
        ActiveElementId = id;
    }

    bool active = ActiveElementId == id;
    bool pressed = active && (LeftMouseDown || RightMouseDown);
    bool clicked = active && hovered && LeftMouseReleased;
    rightClicked = active && hovered && RightMouseReleased;

    var bgColor = focused ? new SKColor(240, 240, 240) : new SKColor(255, 255, 255);
    if (pressed) bgColor = new SKColor(220, 220, 220);

    using var paint = new SKPaint { IsAntialias = true, Color = bgColor };
    float radius = 4 * ItemScale;
    canvas.DrawRoundRect(rect, radius, radius, paint);

    var list = GetTaskList(item.Value);
    using var textPaint = new SKPaint { IsAntialias = true, Color = SKColors.Black, TextSize = 10 * ItemScale };
    
    float itemHeight = 14 * ItemScale;
    float circleSize = 10 * ItemScale;
    float padding = 6 * ItemScale;

    for (int i = 0; i < list.Tasks.Count; i++)
    {
        var task = list.Tasks[i];
        float ty = rect.Top + padding + i * itemHeight;
        if (ty + itemHeight > rect.Bottom) break;

        var circleRect = new SKRect(rect.Left + padding, ty + (itemHeight - circleSize)/2, rect.Left + padding + circleSize, ty + (itemHeight + circleSize)/2);
        
        if (LeftMousePressed && circleRect.Contains(MousePosition.X, MousePosition.Y))
        {
            task.IsCompleted = !task.IsCompleted;
            list.Tasks[i] = task;
            _taskListCache[item.Value] = list;
            storageService.SaveTaskList(list);
        }

        using var circlePaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = SKColors.Gray, StrokeWidth = 1 * ItemScale };
        canvas.DrawOval(circleRect, circlePaint);
        
        if (task.IsCompleted)
        {
            using var checkPaint = new SKPaint { IsAntialias = true, Color = SKColors.Green, Style = SKPaintStyle.Fill };
            circleRect.Inflate(-1 * ItemScale, -1 * ItemScale);
            canvas.DrawOval(circleRect, checkPaint);
        }

        canvas.DrawText(task.Text, rect.Left + padding + circleSize + 6 * ItemScale, ty + itemHeight * 0.75f, textPaint);
    }

    if (focused)
    {
        using var borderPaint = new SKPaint { IsAntialias = true, Color = SKColors.DodgerBlue, Style = SKPaintStyle.Stroke, StrokeWidth = 2 * ItemScale };
        canvas.DrawRoundRect(rect, radius, radius, borderPaint);
    }

    if (active && (LeftMouseReleased || RightMouseReleased)) ActiveElementId = null;
    return clicked;
}

void DrawTextInRect(SKCanvas canvas, string text, SKRect rect, SKPaint paint)
{
    if (string.IsNullOrEmpty(text)) return;
    
    var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
    float y = rect.Top + paint.TextSize;
    float lineHeight = paint.TextSize + 2;
    
    foreach (var rawLine in lines)
    {
        if (y > rect.Bottom) break;

        var words = rawLine.Split(' ');
        var currentLine = "";
        
        foreach (var word in words)
        {
            var testLine = currentLine == "" ? word : currentLine + " " + word;
            if (paint.MeasureText(testLine) > rect.Width && currentLine != "")
            {
                canvas.DrawText(currentLine, rect.Left, y, paint);
                y += lineHeight;
                currentLine = word;
            }
            else
            {
                currentLine = testLine;
            }
            if (y > rect.Bottom) break;
        }
        
        if (y <= rect.Bottom)
        {
            canvas.DrawText(currentLine, rect.Left, y, paint);
            y += lineHeight;
        }
    }
}

StorageService.Note GetNote(string id)
{
    if (_noteCache.TryGetValue(id, out var note)) return note;
    note = storageService.LoadNote(id);
    _noteCache[id] = note;
    return note;
}

StorageService.TaskList GetTaskList(string id)
{
    if (_taskListCache.TryGetValue(id, out var list)) return list;
    list = storageService.LoadTaskList(id);
    _taskListCache[id] = list;
    return list;
}

bool TextButton(SKCanvas canvas, SKRect rect, string text, string id)
{
    bool hovered = rect.Contains(MousePosition.X, MousePosition.Y);

    if (hovered && LeftMousePressed)
    {
        ActiveElementId = id;
    }

    bool active = ActiveElementId == id;
    bool pressed = active && LeftMouseDown;
    bool clicked = active && hovered && LeftMouseReleased;

    var color = pressed
        ? new SKColor(180, 180, 180, 255)
        : hovered
            ? new SKColor(220, 220, 220, 255)
            : new SKColor(200, 200, 200, 255);

    using var buttonPaint = new SKPaint
    {
        IsAntialias = true,
        Color = color
    };

    canvas.DrawRoundRect(rect, 8, 8, buttonPaint);

    using var textPaint = new SKPaint
    {
        IsAntialias = true,
        Color = SKColors.Black,
        TextSize = 13,
        Typeface = SKTypeface.FromFamilyName("Segoe UI")
    };

    var textWidth = textPaint.MeasureText(text);
    
    canvas.DrawText(text, rect.MidX - textWidth / 2, rect.MidY + 5, textPaint);

    if (active && (LeftMouseReleased || RightMouseReleased))
    {
        ActiveElementId = null;
    }

    return clicked;
}

bool DockItemButton(SKCanvas canvas, SKRect rect, StorageService.DockItem item, string id, out bool rightClicked)
{
    rightClicked = false;
    bool hovered = rect.Contains(MousePosition.X, MousePosition.Y);

    if (hovered && (LeftMousePressed || RightMousePressed))
    {
        ActiveElementId = id;
    }

    bool active = ActiveElementId == id;
    bool pressed = active && (LeftMouseDown || RightMouseDown);
    bool clicked = active && hovered && LeftMouseReleased;
    rightClicked = active && hovered && RightMouseReleased;

    var cardColor = pressed
        ? new SKColor(230, 230, 230, 180)
        : hovered
            ? new SKColor(255, 255, 255, 180)
            : new SKColor(255, 255, 255, 130);

    using var cardPaint = new SKPaint
    {
        IsAntialias = true,
        Color = cardColor
    };

    float radius = 12 * ItemScale;
    canvas.DrawRoundRect(rect, radius, radius, cardPaint);

    SKImage? icon = GetDockItemIcon(item.Value);

    if (icon is not null)
    {
        // icon rect is inner rect with small padding
        var iconPadding = 1 * ItemScale;
        SKRect iconRect = new SKRect(
            rect.Left + iconPadding,
            rect.Top + iconPadding,
            rect.Right - iconPadding,
            rect.Bottom - iconPadding
        );
        
        canvas.DrawImage(icon, iconRect);
    }
    
    if (active && (LeftMouseReleased || RightMouseReleased))
    {
        ActiveElementId = null;
    }

    return clicked;
}

bool AddButton(SKCanvas canvas, SKRect rect, string id)
{
    bool hovered = rect.Contains(MousePosition.X, MousePosition.Y);

    if (hovered && LeftMousePressed)
    {
        ActiveElementId = id;
    }

    bool active = ActiveElementId == id;
    bool pressed = active && LeftMouseDown;
    bool clicked = active && hovered && LeftMouseReleased;

    var color = pressed
        ? new SKColor(60, 100, 200, 255)
        : hovered
            ? new SKColor(95, 135, 235, 255)
            : new SKColor(80, 120, 220, 255);

    using var buttonPaint = new SKPaint
    {
        IsAntialias = true,
        Color = color
    };

    canvas.DrawRoundRect(rect, 12, 12, buttonPaint);

    using var plusPaint = new SKPaint
    {
        IsAntialias = true,
        Color = SKColors.White,
        StrokeWidth = 3,
        Style = SKPaintStyle.Stroke,
        StrokeCap = SKStrokeCap.Round
    };

    float centerX = rect.MidX;
    float centerY = rect.MidY;
    float plusSize = 10;

    canvas.DrawLine(centerX - plusSize, centerY, centerX + plusSize, centerY, plusPaint);
    canvas.DrawLine(centerX, centerY - plusSize, centerX, centerY + plusSize, plusPaint);

    if (active && (LeftMouseReleased || RightMouseReleased))
    {
        ActiveElementId = null;
    }

    return clicked;
}

SKRect GetAddMenuRect(SKRect addButtonRect)
{
    const float menuWidth = 160;
    const float rowHeight = 40;
    const float menuPadding = 6;

    return new SKRect(
        addButtonRect.Right - menuWidth,
        addButtonRect.Top - rowHeight * 3 - menuPadding * 2 - 8,
        addButtonRect.Right,
        addButtonRect.Top - 8);
}

string? DrawAddMenu(SKCanvas canvas, SKRect addButtonRect)
{
    const float menuWidth = 160;
    const float rowHeight = 40;
    const float menuPadding = 6;

    AddMenuRect = GetAddMenuRect(addButtonRect);


    using var menuPaint = new SKPaint
    {
        IsAntialias = true,
        Color = new SKColor(255, 255, 255, 245)
    };

    canvas.DrawRoundRect(AddMenuRect, 12, 12, menuPaint);

    var itemRect = new SKRect(
        AddMenuRect.Left + menuPadding,
        AddMenuRect.Top + menuPadding,
        AddMenuRect.Right - menuPadding,
        AddMenuRect.Top + menuPadding + rowHeight);

    var noteRect = new SKRect(
        itemRect.Left,
        itemRect.Bottom,
        itemRect.Right,
        itemRect.Bottom + rowHeight);

    var taskListRect = new SKRect(
        itemRect.Left,
        noteRect.Bottom,
        itemRect.Right,
        noteRect.Bottom + rowHeight);

    if (MenuItem(canvas, itemRect, "Item", "add-menu-item"))
    {
        return "Item";
    }

    if (MenuItem(canvas, noteRect, "Note", "add-menu-note"))
    {
        return "Note";
    }

    if (MenuItem(canvas, taskListRect, "Task List", "add-menu-task-list"))
    {
        return "TaskList";
    }

    return null;
}

bool MenuItem(SKCanvas canvas, SKRect rect, string text, string id)
{
    bool hovered = rect.Contains(MousePosition.X, MousePosition.Y);

    if (hovered && LeftMousePressed)
    {
        ActiveElementId = id;
    }

    bool active = ActiveElementId == id;
    bool pressed = active && LeftMouseDown;
    bool clicked = active && hovered && LeftMouseReleased;

    var backgroundColor = pressed
        ? new SKColor(220, 220, 220, 255)
        : hovered
            ? new SKColor(235, 235, 235, 255)
            : SKColors.Transparent;

    using var backgroundPaint = new SKPaint
    {
        IsAntialias = true,
        Color = backgroundColor
    };

    canvas.DrawRoundRect(rect, 8, 8, backgroundPaint);

    using var textPaint = new SKPaint
    {
        IsAntialias = true,
        Color = new SKColor(32, 32, 32, 255)
    };

    using var textFont = new SKFont
    {
        Size = 16,
        Typeface = SKTypeface.FromFamilyName("Segoe UI")
    };

    canvas.DrawText(text, rect.Left + 12, rect.MidY + 6, textFont, textPaint);

    if (active && (LeftMouseReleased || RightMouseReleased))
    {
        ActiveElementId = null;
    }

    return clicked;
}

SKRect GetContextMenuRect(Vector2 position)
{
    const float menuWidth = 140;
    const float rowHeight = 40;
    const float menuPadding = 6;

    // Position menu so it stays within window bounds if possible
    float x = position.X;
    float y = position.Y;
    
    if (x + menuWidth > lastFramebufferSize.X)
    {
        x = lastFramebufferSize.X - menuWidth - 4;
    }
    
    if (y + rowHeight + menuPadding * 2 > lastFramebufferSize.Y)
    {
        y = lastFramebufferSize.Y - (rowHeight + menuPadding * 2) - 4;
    }

    ContextMenuRect = new SKRect(
        x,
        y,
        x + menuWidth,
        y + rowHeight + menuPadding * 2);

    return ContextMenuRect;
}

string? DrawContextMenu(SKCanvas canvas)
{
    const float rowHeight = 40;
    const float menuPadding = 6;
    
    ContextMenuRect = GetContextMenuRect(ContextMenuPosition);

    using var menuPaint = new SKPaint
    {
        IsAntialias = true,
        Color = new SKColor(255, 255, 255, 245)
    };

    canvas.DrawRoundRect(ContextMenuRect, 12, 12, menuPaint);

    var removeRect = new SKRect(
        ContextMenuRect.Left + menuPadding,
        ContextMenuRect.Top + menuPadding,
        ContextMenuRect.Right - menuPadding,
        ContextMenuRect.Top + menuPadding + rowHeight);

    if (MenuItem(canvas, removeRect, "Remove", "context-menu-remove"))
    {
        return "Remove";
    }

    return null;
}

#endregion

#region Methods
void KeyDown(IKeyboard keyboard, Key key, int keyCode)
{
    // Quit
    if (key == Key.Escape)
    {
        DismissDock();
        return;
    }
    
    if (FocusedItemId != null)
    {
        if (key == Key.Backspace)
        {
            int index = int.Parse(FocusedItemId.Substring(10));
            var item = DockItems[index];
            if (item.Type == ItemType.Note)
            {
                var note = GetNote(item.Value);
                if (note.Content.Length > 0)
                {
                    note.Content = note.Content.Substring(0, note.Content.Length - 1);
                    _noteCache[item.Value] = note;
                    storageService.SaveNote(note);
                }
            }
            else if (item.Type == ItemType.TaskList)
            {
                var list = GetTaskList(item.Value);
                if (list.Tasks.Count > 0)
                {
                    var lastTask = list.Tasks[^1];
                    if (lastTask.Text.Length > 0)
                    {
                        lastTask.Text = lastTask.Text.Substring(0, lastTask.Text.Length - 1);
                        list.Tasks[^1] = lastTask;
                    }
                    else
                    {
                        list.Tasks.RemoveAt(list.Tasks.Count - 1);
                    }
                    _taskListCache[item.Value] = list;
                    storageService.SaveTaskList(list);
                }
            }
        }
        else if (key == Key.Enter)
        {
            int index = int.Parse(FocusedItemId.Substring(10));
            var item = DockItems[index];
            if (item.Type == ItemType.Note)
            {
                var note = GetNote(item.Value);
                note.Content += "\n";
                _noteCache[item.Value] = note;
                storageService.SaveNote(note);
            }
            else if (item.Type == ItemType.TaskList)
            {
                var list = GetTaskList(item.Value);
                list.Tasks.Add(new StorageService.TaskItem("", false));
                _taskListCache[item.Value] = list;
                storageService.SaveTaskList(list);
            }
        }
    }
}

void KeyChar(IKeyboard keyboard, char character)
{
    if (FocusedItemId == null) return;
    
    if (!FocusedItemId.StartsWith("dock-item-")) return;
    int index = int.Parse(FocusedItemId.Substring(10));
    if (index < 0 || index >= DockItems.Count) return;
    
    var item = DockItems[index];
    if (item.Type == ItemType.Note)
    {
        var note = GetNote(item.Value);
        note.Content += character;
        _noteCache[item.Value] = note;
        storageService.SaveNote(note);
    }
    else if (item.Type == ItemType.TaskList)
    {
        var list = GetTaskList(item.Value);
        if (list.Tasks.Count == 0)
        {
            list.Tasks.Add(new StorageService.TaskItem("", false));
        }
        
        var lastTask = list.Tasks[^1];
        lastTask.Text += character;
        list.Tasks[^1] = lastTask;
        _taskListCache[item.Value] = list;
        storageService.SaveTaskList(list);
    }
}


void SummonDock()
{
    // Summon window
    window.TopMost = true;
    WindowLayout();
    window.Focus();
    
    SummonCalled = false;
}

void DismissDock()
{
    // Remove active element
    ActiveElementId = null;
    IsAddMenuOpen = false;
    // No longer top most
    window.TopMost = false;
    
    // Move dock off screen
    var monitorSize = window.Monitor.Bounds.Size;
    window.Size = new Vector2D<int>(monitorSize.X / 3, monitorSize.Y / 3);
    var centerX = monitorSize.X / 2; // middle of screen
    var bottomY = monitorSize.Y; // bottom of screen

    // minus half our window width so the center point is the centre of the window.
    // add window Height to ensure we are off screen
    window.Position = new Vector2D<int>(centerX - window.Size.X / 2, bottomY + window.Size.Y);
    
    // Ensure a render happens before we move off screen
    // this ensures the popups are hidden
    window.DoRender();
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

static unsafe Tuple<string,string>? OpenFilePicker(HWND owner)
{
    // COM must be initialized on the thread that shows the dialog.
    HRESULT hr = PInvoke.CoInitializeEx(
        null,
        COINIT.COINIT_APARTMENTTHREADED | COINIT.COINIT_DISABLE_OLE1DDE);

    bool shouldUninitialize = hr.Succeeded;

    try
    {
        IFileOpenDialog dialog = FileOpenDialog.CreateInstance<IFileOpenDialog>();

        // Optional: set title
        dialog.SetTitle("Choose a file");

        // Optional: only allow existing files
        FILEOPENDIALOGOPTIONS options;
        dialog.GetOptions(&options);
        dialog.SetOptions(options | FILEOPENDIALOGOPTIONS.FOS_FILEMUSTEXIST);

        try
        {
            dialog.Show(owner);
        }
        catch (Exception ex)
        {
            // cancelled by user
            return null;
        }

        // // User cancelled
        // if (hr == HRESULT_FROM_WIN32(WIN32_ERROR.ERROR_CANCELLED))
        // {
        //     return null;
        // }
        //
        // hr.ThrowOnFailure();

        dialog.GetResult(out IShellItem result);

        PWSTR pathPtr;
        PWSTR fileNmPtr;
        result.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, &pathPtr);
        result.GetDisplayName(SIGDN.SIGDN_NORMALDISPLAY, &fileNmPtr);
        try
        {
            return new Tuple<string, string>(fileNmPtr.ToString(), pathPtr.ToString());
        }
        finally
        {
            PInvoke.CoTaskMemFree(pathPtr);
            PInvoke.CoTaskMemFree(fileNmPtr);
        }
    }
    finally
    {
        if (shouldUninitialize)
        {
            PInvoke.CoUninitialize();
        }
    }

    // static HRESULT HRESULT_FROM_WIN32(WIN32_ERROR error)
    // {
    //     return (HRESULT)(int)(0x80070000u | (uint)error);
    // }
}


enum ViewMode { All, Grouped }

#endregion