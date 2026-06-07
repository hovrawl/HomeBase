using System.Numerics;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Glfw;
using SkiaSharp;
using Windows.Win32.Foundation;
using Silk.NET.Direct2D;
using WinDock;
using WindowState = Silk.NET.Windowing.WindowState;

// Window handle
HWND hwnd = HWND.Null;
// Skia
GRBackendRenderTarget? skiaRenderTarget = null;
SKSurface? skiaSurface = null;
Vector2D<int> lastFramebufferSize = new(0, 0);

IInputContext WindowInput = null;

// Mouse Globals
Vector2 MousePosition = new Vector2(0, 0);
bool LeftMouseDown = false;
bool LeftMousePressed = false;
bool LeftMouseReleased = false;
bool RightMouseDown = false;
bool RightMousePressed = false;
bool RightMouseReleased = false;

string? ActiveElementId = null;
Queue<Action> ActionQueue = new();

// State bools
bool didInitialLayout = false;
bool SummonCalled = false;

//Storage
StorageService storageService = new StorageService();
List<StorageService.DockItem> DockItems = storageService.Load();



// setup window
var windowOptions = WindowOptions.Default;
windowOptions.Title = "WinDock";
windowOptions.TopMost = true;
windowOptions.WindowBorder = WindowBorder.Hidden;
windowOptions.WindowState = WindowState.Normal;
windowOptions.WindowClass = "Hovrawl.WinDock";
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

void DrawStartMenuMockContent(SKCanvas canvas, SKRect panelRect)
{
    using var titlePaint = new SKPaint
    {
        IsAntialias = true,
        Color = new SKColor(32, 32, 32, 255),
        TextSize = 20
    };

    canvas.DrawText("Pinned", panelRect.Left + 32, panelRect.Top + 48, titlePaint);

    using var cardPaint = new SKPaint
    {
        IsAntialias = true,
        Color = new SKColor(255, 255, 255, 130)
    };

    using var iconPaint = new SKPaint
    {
        IsAntialias = true,
        Color = new SKColor(80, 120, 220, 255)
    };

    const float paddingX = 32;
    const float startY = 72;
    const float cellSize = 72;
    const float cardSize = 56;
    const float iconSize = 38;
    const int itemCount = 18;

    float availableWidth = panelRect.Width - paddingX * 2;
    int columns = Math.Max(1, (int)Math.Floor(availableWidth / cellSize));
    int rows = (int)Math.Ceiling(itemCount / (float)columns);

    for (int index = 0; index < itemCount; index++)
    {
        int row = index / columns;
        int column = index % columns;

        float x = panelRect.Left + paddingX + column * cellSize;
        float y = panelRect.Top + startY + row * cellSize;

        var cardRect = new SKRect(x, y, x + cardSize, y + cardSize);
        canvas.DrawRoundRect(cardRect, 12, 12, cardPaint);

        float iconX = x + (cardSize - iconSize) / 2;
        float iconY = y + (cardSize - iconSize) / 2;
        var iconRect = new SKRect(iconX, iconY, iconX + iconSize, iconY + iconSize);
        canvas.DrawRoundRect(iconRect, 9, 9, iconPaint);
    }

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
}

void DrawDock(SKCanvas canvas, SKRect panelRect)
{
    using var titlePaint = new SKPaint
    {
        IsAntialias = true,
        Color = new SKColor(32, 32, 32, 255),
        //TextSize = 20,
    };
    using var titleFont = new SKFont
    {
        Size = 20,
        Typeface = SKTypeface.FromFamilyName("Segoe UI"),
    };
    
    const string DockText = "Docked Items";

    canvas.DrawText(DockText, panelRect.Left + 32, panelRect.Top + 48, titleFont, titlePaint);

    DrawDockItems(canvas, panelRect);
    
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
    
    var addButtonRect = new SKRect(
        footerRect.Right - 56,
        footerRect.Top + 8,
        footerRect.Right - 8,
        footerRect.Bottom - 8);
    
    if (AddButton(canvas, addButtonRect, "add-item"))
    {
        ActionQueue.Enqueue(OpenAddItemDialog);
    }
}

void DrawDockItems(SKCanvas canvas, SKRect dockPanel)
{
    const float paddingX = 32;
    const float startY = 72;
    const float cellSize = 72;
    const float cardSize = 56;
    const float iconSize = 38;
    var itemCount = DockItems.Count();

    
    using var cardPaint = new SKPaint
    {
        IsAntialias = true,
        Color = new SKColor(255, 255, 255, 130)
    };

    using var iconPaint = new SKPaint
    {
        IsAntialias = true,
        Color = new SKColor(80, 120, 220, 255)
    };
    
    float availableWidth = dockPanel.Width - paddingX * 2;
    int columns = Math.Max(1, (int)Math.Floor(availableWidth / cellSize));

    for (int index = 0; index < itemCount; index++)
    {
        StorageService.DockItem item = DockItems[index];
        int row = index / columns;
        int column = index % columns;

        float x = dockPanel.Left + paddingX + column * cellSize;
        float y = dockPanel.Top + startY + row * cellSize;

        
        var cardRect = new SKRect(x, y, x + cardSize, y + cardSize);
        
        string itemId = $"dock-item-{index}";

        if (DockItemButton(canvas, cardRect, item, itemId))
        {
            ActionQueue.Enqueue(() => OnDockItemClicked(item));
        }
    }
}

bool DockItemButton(SKCanvas canvas, SKRect rect, StorageService.DockItem item, string id)
{
    bool hovered = rect.Contains(MousePosition.X, MousePosition.Y);

    if (hovered && LeftMousePressed)
    {
        ActiveElementId = id;
    }

    bool active = ActiveElementId == id;
    bool pressed = active && LeftMouseDown;
    bool clicked = active && hovered && LeftMouseReleased;

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

    canvas.DrawRoundRect(rect, 12, 12, cardPaint);

    if (active && LeftMouseReleased)
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

    if (active && LeftMouseReleased)
    {
        ActiveElementId = null;
    }

    return clicked;
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
    
    // Navigation
    
    // Activate Selection
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
    window.TopMost = false;
    
    // Move dock off screen
    var monitorSize = window.Monitor.Bounds.Size;
    window.Size = new Vector2D<int>(monitorSize.X / 3, monitorSize.Y / 3);
    var centerX = monitorSize.X / 2; // middle of screen
    var bottomY = monitorSize.Y; // bottom of screen

    // minus half our window width so the center point is the centre of the window.
    // add window Height to ensure we are off screen
    window.Position = new Vector2D<int>(centerX - window.Size.X / 2, bottomY + window.Size.Y);
}

void OnDockItemClicked(StorageService.DockItem dockItem)
{
    // click dock item
    // for programs we will launch
    // other bespoke items will have special actions
    Console.WriteLine("Dock Item Pressed");
}

void OpenAddItemDialog()
{
    // show file picker dialog
    Console.WriteLine("Add Pressed");
    
}
#endregion