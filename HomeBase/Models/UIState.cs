using Silk.NET.Maths;
using Silk.NET.Windowing;
using SkiaSharp;

namespace HomeBase.Models;

public struct UIState
{
    public bool DidInitialLayout { get; set; }
    
    public string? ActiveElementId { get; set; }

    public float BufferScale { get; private set; } = 1.0f;
    public Vector2D<int> LastFramebufferSize { get; set; }
    
    public bool IsAddMenuOpen { get; set; }
    public bool IsContextMenuOpen { get; set; }

    public string? FocusedItemId { get; set; }
    public string? RenamingItemId { get; set; }

    public int CaretIndex { get; set; }
    public int RenamingCaretIndex { get; set; }
    public int FocusedTaskListIndex { get; set; } = -1;

    public float ItemScale { get; set; } = 1.2f;
    public float ScrollOffsetY { get; set; }
    public float TotalContentHeight { get; set; }

    public ViewMode CurrentViewMode { get; set; } = ViewMode.All;

    public SKRect AddMenuRect { get; set; }
    public SKRect ContextMenuRect { get; set; }
    public Vector2D<int> ContextMenuPosition { get; set; }
    public int ContextMenuItemIndex { get; set; }

    public Vector2D<int> DockHiddenPosition { get; set; } = new(0, 0);

    public UIState()
    {
    }

    public void SetFramebufferScale(IWindow window)
    {
        if (window.Size.X <= 0 || window.FramebufferSize.X <= 0)
        {
            BufferScale = 1.0f;
            return;
        }

        BufferScale = window.FramebufferSize.X / (float)window.Size.X;
    }
}
