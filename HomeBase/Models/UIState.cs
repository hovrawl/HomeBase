namespace HomeBase.Models;

public sealed class UIState
{
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
}