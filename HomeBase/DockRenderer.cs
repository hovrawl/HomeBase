using System.Collections.Concurrent;
using HomeBase.Models;
using HomeBase.Render;
using Silk.NET.Maths;
using SkiaSharp;

namespace HomeBase;

public sealed class DockRenderer
{
    private UIState _ui;
    private InputState _input;
    private readonly StorageService _storage;
    private readonly ConcurrentQueue<Action> _actionQueue;

    public DockRenderer(
        UIState ui,
        InputState input,
        StorageService storage,
        ConcurrentQueue<Action> actionQueue)
    {
        _ui = ui;
        _input = input;
        _storage = storage;
        _actionQueue = actionQueue;
    }

    private List<StorageService.DockItem> DockItems => _storage.Items;

    public void Render(SKCanvas canvas)
    {
        // Clear canvas with transparent background
        canvas.Clear(SKColors.Transparent);
        
        SKRect panelRect = DrawBackground(canvas);
        DrawDock(canvas, panelRect);

        canvas.Flush();
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

        float radius = HomeBase.Render.Helpers.LogicalToFramebufferPixels(_ui.BufferScale, 12);
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
        float maxScroll = Math.Max(0, _ui.TotalContentHeight - viewportRect.Bottom);
        _ui.ScrollOffsetY = Math.Clamp(_ui.ScrollOffsetY, 0, maxScroll);

        canvas.Save();
        canvas.ClipRect(viewportRect);
        canvas.Translate(0, -_ui.ScrollOffsetY);

        var originalMousePos = _input.MousePosition;
        if (viewportRect.Contains(originalMousePos.X, originalMousePos.Y))
        {
            _input.MousePosition = new Vector2D<int>(originalMousePos.X, originalMousePos.Y + (int)_ui.ScrollOffsetY);
        }
        else
        {
            // Move mouse away so items don't hover
            _input.MousePosition = new Vector2D<int>(-10000, -10000);
        }

        _ui.TotalContentHeight = DrawDockItems(canvas, panelRect);

        _input.MousePosition = originalMousePos;
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
        if (TextButton(canvas, modeBtnRect, _ui.CurrentViewMode == ViewMode.All ? "Mode: All" : "Mode: Grouped", "view-mode-btn"))
        {
            _ui.CurrentViewMode = _ui.CurrentViewMode == ViewMode.All ? ViewMode.Grouped : ViewMode.All;
        }

        // Scale Controls
        var scaleLabelRect = new SKRect(modeBtnRect.Right + 12, footerRect.Top, modeBtnRect.Right + 80, footerRect.Bottom);
        using var labelPaint = new SKPaint { IsAntialias = true, Color = SKColors.DimGray };
        using var labelFont = new SKFont { Size = 12, Typeface = SKTypeface.FromFamilyName("Segoe UI") };
        canvas.DrawText($"Size: {(int)(_ui.ItemScale * 100)}%", scaleLabelRect.Left, footerRect.MidY + 5, labelFont, labelPaint);

        var minusBtnRect = new SKRect(scaleLabelRect.Right, footerRect.Top + 12, scaleLabelRect.Right + 32, footerRect.Bottom - 12);
        if (TextButton(canvas, minusBtnRect, "-", "scale-minus-btn"))
        {
            _ui.ItemScale = Math.Max(0.5f, _ui.ItemScale - 0.1f);
        }

        var plusBtnRect = new SKRect(minusBtnRect.Right + 4, footerRect.Top + 12, minusBtnRect.Right + 36, footerRect.Bottom - 12);
        if (TextButton(canvas, plusBtnRect, "+", "scale-plus-btn"))
        {
            _ui.ItemScale = Math.Min(3.0f, _ui.ItemScale + 0.1f);
        }
        
        var addButtonRect = new SKRect(
            footerRect.Right - 56,
            footerRect.Top + 8,
            footerRect.Right - 8,
            footerRect.Bottom - 8);
        
        var addClicked = AddButton(canvas, addButtonRect, "add-item");
        if (addClicked)
        {
            _ui.IsAddMenuOpen = !_ui.IsAddMenuOpen;
        }

        _ui.AddMenuRect = GetAddMenuRect(addButtonRect);

        // Extra hit test to hide menu if we clicked elsewhere
        if (_input.LeftMouseReleased && !addClicked)
        {
            if (_ui.IsAddMenuOpen && !_ui.AddMenuRect.Contains(_input.MousePosition.X, _input.MousePosition.Y))
            {
                _ui.IsAddMenuOpen = false;
                _ui.ActiveElementId = null;
            }
        }

        // Ensure we have the context menu rect
        _ui.ContextMenuRect = GetContextMenuRect(_ui.ContextMenuPosition);

        // Extra hit test to hide context menu if we clicked elsewhere
        if (_ui.IsContextMenuOpen &&
            (_input.LeftMouseReleased || _input.RightMouseReleased) &&
            !_ui.ContextMenuRect.Contains(_input.MousePosition.X, _input.MousePosition.Y))
        {
            _ui.IsContextMenuOpen = false;
            _ui.ActiveElementId = null;
        }
        
        if (_ui.IsAddMenuOpen)
        {
            string? selectedAddMenuItem = DrawAddMenu(canvas, addButtonRect);

            if (selectedAddMenuItem == "Item")
            {
                _ui.IsAddMenuOpen = false;
                _actionQueue.Enqueue(AppAction.OpenAddItemDialog);
            }
            else if (selectedAddMenuItem == "Note")
            {
                _ui.IsAddMenuOpen = false;
                _actionQueue.Enqueue(AppAction.CreateNote);
            }
            else if (selectedAddMenuItem == "TaskList")
            {
                _ui.IsAddMenuOpen = false;
                _actionQueue.Enqueue(AppAction.CreateTaskList);
            }    
        }

        if (_ui.IsContextMenuOpen)
        {
            string? selectedContextAction = DrawContextMenu(canvas);

            if (selectedContextAction == "Remove")
            {
                _ui.IsContextMenuOpen = false;
                int indexToRemove = _ui.ContextMenuItemIndex;
                _actionQueue.Enqueue(() => AppAction.RemoveDockItem(indexToRemove));
            }
            else if (selectedContextAction == "Rename")
            {
                _ui.IsContextMenuOpen = false;
                int indexToRename = _ui.ContextMenuItemIndex;
                _ui.RenamingItemId = $"dock-item-{indexToRename}";
                _ui.RenamingCaretIndex = DockItems[indexToRename].Name.Length;
            }
        }
    }

    float DrawDockItems(SKCanvas canvas, SKRect dockPanel)
    {
        float currentY = 16;
        if (_ui.CurrentViewMode == ViewMode.All)
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
        float cellWidth = baseCellSize * _ui.ItemScale;
        float labelHeight = 20 * _ui.ItemScale;
        float cellHeight = cellWidth + labelHeight;
        float cardSize = (baseCellSize - 16) * _ui.ItemScale;

        float availableWidth = dockPanel.Width - paddingX * 2;
        int columns = Math.Max(1, (int)Math.Floor(availableWidth / cellWidth));
        int rows = (int)Math.Ceiling((float)items.Count / columns);

        bool anyHovered = false;

        for (int i = 0; i < items.Count; i++)
        {
            var (item, originalIndex) = items[i];
            int row = i / columns;
            int column = i % columns;

            float x = dockPanel.Left + paddingX + column * cellWidth;
            float y = dockPanel.Top + startY + row * cellHeight;

            var cardRect = new SKRect(x, y, x + cardSize, y + cardSize);
            if (cardRect.Contains(_input.MousePosition.X, _input.MousePosition.Y)) anyHovered = true;

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

            var labelRect = new SKRect(x, y + cardSize + 2 * _ui.ItemScale, x + cardSize, y + cardSize + labelHeight);
            if (labelRect.Contains(_input.MousePosition.X, _input.MousePosition.Y)) anyHovered = true;
            DrawItemLabel(canvas, labelRect, item, stableId);

            if (clicked)
            {
                if (_ui.RenamingItemId != null && _ui.RenamingItemId != stableId)
                {
                    SaveRenaming();
                }

                if (item.Type == ItemType.File)
                {
                    _actionQueue.Enqueue(() => OnDockItemClicked(item));
                }
                else
                {
                    if (_ui.FocusedItemId != stableId)
                    {
                        _ui.FocusedItemId = stableId;
                        if (item.Type == ItemType.Note)
                        {
                            _ui.CaretIndex = GetNote(item.Value).Content.Length;
                        }
                        else if (item.Type == ItemType.TaskList)
                        {
                            var list = GetTaskList(item.Value);
                            _ui.FocusedTaskListIndex = list.Tasks.Count - 1;
                            _ui.CaretIndex = _ui.FocusedTaskListIndex >= 0 ? list.Tasks[_ui.FocusedTaskListIndex].Text.Length : 0;
                        }
                    }
                }
            }

            if (rightClicked)
            {
                if (_ui.RenamingItemId != null) SaveRenaming();
                
                _ui.IsContextMenuOpen = true;
                _ui.ContextMenuItemIndex = originalIndex;
                _ui. ContextMenuPosition = _input.MousePosition;
                _ui.IsAddMenuOpen = false;
            }
        }

        if (_input.LeftMouseReleased && !anyHovered && !_ui.IsAddMenuOpen && !_ui.ContextMenuRect.Contains(_input.MousePosition.X, _input.MousePosition.Y))
        {
            if (dockPanel.Contains(_input.MousePosition.X, _input.MousePosition.Y))
            {
                _ui.FocusedItemId = null;
                if (_ui.RenamingItemId != null) SaveRenaming();
            }
        }

        return rows * cellHeight;
    }

    void DrawItemLabel(SKCanvas canvas, SKRect rect, StorageService.DockItem item, string id)
    {
        bool isRenaming = _ui.RenamingItemId == id;
        string text = item.Name;
        
        using var paint = new SKPaint 
        { 
            IsAntialias = true, 
            Color = SKColors.Black, 
            TextSize = 11 * _ui.ItemScale,
            Typeface = SKTypeface.FromFamilyName("Segoe UI")
        };

        if (isRenaming)
        {
            // Draw background for editing
            using var bgPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
            canvas.DrawRoundRect(rect, 4 * _ui.ItemScale, 4 * _ui.ItemScale, bgPaint);
            using var borderPaint = new SKPaint { Color = SKColors.DodgerBlue, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1 * _ui.ItemScale };
            canvas.DrawRoundRect(rect, 4 * _ui.ItemScale, 4 * _ui.ItemScale, borderPaint);

            float textX = rect.Left + 4 * _ui.ItemScale;
            float textY = rect.MidY + paint.TextSize / 3;
            canvas.DrawText(text, textX, textY, paint);

            // Draw caret
            float caretX = textX + paint.MeasureText(text.Substring(0, Math.Clamp(_ui.RenamingCaretIndex, 0, text.Length)));
            using var caretPaint = new SKPaint { Color = SKColors.Black, StrokeWidth = 1 * _ui.ItemScale };
            canvas.DrawLine(caretX, rect.Top + 4 * _ui.ItemScale, caretX, rect.Bottom - 4 * _ui.ItemScale, caretPaint);

            // Handle click to move caret
            if (_input.LeftMousePressed && rect.Contains(_input.MousePosition.X, _input.MousePosition.Y))
            {
                float localX = _input.MousePosition.X - textX;
                int bestIdx = 0;
                float minDist = float.MaxValue;
                for (int j = 0; j <= text.Length; j++)
                {
                    float px = paint.MeasureText(text.Substring(0, j));
                    float d = Math.Abs(px - localX);
                    if (d < minDist) { minDist = d; bestIdx = j; }
                    else break;
                }
                _ui.RenamingCaretIndex = bestIdx;
            }
        }
        else
        {
            // Draw normal label
            float textWidth = paint.MeasureText(text);
            // Center text in rect
            float textX = rect.MidX - textWidth / 2;
            float textY = rect.MidY + paint.TextSize / 3;
            
            // Simple eliding if too long
            if (textWidth > rect.Width)
            {
                text = text.Substring(0, Math.Max(0, text.Length - 3)) + "...";
                textWidth = paint.MeasureText(text);
                textX = rect.MidX - textWidth / 2;
            }

            canvas.DrawText(text, textX, textY, paint);
        }
    }

    void SaveRenaming()
    {
        if (_ui.RenamingItemId != null && _ui.RenamingItemId.StartsWith("dock-item-"))
        {
            _storage.Save();
        }
        _ui.RenamingItemId = null;
    }

    bool NoteDockItem(SKCanvas canvas, SKRect rect, StorageService.DockItem item, string id, out bool rightClicked)
    {
        rightClicked = false;
        bool hovered = rect.Contains(_input.MousePosition.X, _input.MousePosition.Y);
        bool focused = _ui.FocusedItemId == id;

        if (hovered && (_input.LeftMousePressed || _input.RightMousePressed))
        {
            _ui.ActiveElementId = id;
        }

        bool active = _ui.ActiveElementId == id;
        bool pressed = active && (_input.LeftMouseDown || _input.RightMouseDown);
        bool clicked = active && hovered && _input.LeftMouseReleased;
        rightClicked = active && hovered && _input.RightMouseReleased;

        var bgColor = focused ? new SKColor(255, 255, 180) : new SKColor(255, 255, 220);
        if (pressed) bgColor = new SKColor(240, 240, 160);

        using var paint = new SKPaint { IsAntialias = true, Color = bgColor };
        float radius = 4 * _ui.ItemScale;
        canvas.DrawRoundRect(rect, radius, radius, paint);

        var note = GetNote(item.Value);
        using var textPaint = new SKPaint { IsAntialias = true, Color = SKColors.Black, TextSize = 11 * _ui.ItemScale };
        var textPadding = 6 * _ui.ItemScale;
        var textRect = new SKRect(rect.Left + textPadding, rect.Top + textPadding, rect.Right - textPadding, rect.Bottom - textPadding);
        DrawTextInRect(canvas, note.Content, textRect, textPaint, focused);

        if (focused)
        {
            using var borderPaint = new SKPaint { IsAntialias = true, Color = new SKColor(255, 165, 0), Style = SKPaintStyle.Stroke, StrokeWidth = 2 * _ui.ItemScale };
            canvas.DrawRoundRect(rect, radius, radius, borderPaint);
        }

        if (active && (_input.LeftMouseReleased || _input.RightMouseReleased)) _ui.ActiveElementId = null;
        return clicked;
    }

    bool TaskListDockItem(SKCanvas canvas, SKRect rect, StorageService.DockItem item, string id, out bool rightClicked)
    {
        rightClicked = false;
        bool hovered = rect.Contains(_input.MousePosition.X, _input.MousePosition.Y);
        bool focused = _ui.FocusedItemId == id;

        if (hovered && (_input.LeftMousePressed || _input.RightMousePressed))
        {
            _ui.ActiveElementId = id;
        }

        bool active = _ui.ActiveElementId == id;
        bool pressed = active && (_input.LeftMouseDown || _input.RightMouseDown);
        bool clicked = active && hovered && _input.LeftMouseReleased;
        rightClicked = active && hovered && _input.RightMouseReleased;

        var bgColor = focused ? new SKColor(240, 240, 240) : new SKColor(255, 255, 255);
        if (pressed) bgColor = new SKColor(220, 220, 220);

        using var paint = new SKPaint { IsAntialias = true, Color = bgColor };
        float radius = 4 * _ui.ItemScale;
        canvas.DrawRoundRect(rect, radius, radius, paint);

        var list = GetTaskList(item.Value);
        using var textPaint = new SKPaint { IsAntialias = true, Color = SKColors.Black, TextSize = 10 * _ui.ItemScale };
        
        float itemHeight = 14 * _ui.ItemScale;
        float circleSize = 10 * _ui.ItemScale;
        float padding = 6 * _ui.ItemScale;

        for (int i = 0; i < list.Tasks.Count; i++)
        {
            var task = list.Tasks[i];
            float ty = rect.Top + padding + i * itemHeight;
            if (ty + itemHeight > rect.Bottom) break;

            var circleRect = new SKRect(rect.Left + padding, ty + (itemHeight - circleSize)/2, rect.Left + padding + circleSize, ty + (itemHeight + circleSize)/2);
            
            if (_input.LeftMousePressed && circleRect.Contains(_input.MousePosition.X, _input.MousePosition.Y))
            {
                task.IsCompleted = !task.IsCompleted;
                list.Tasks[i] = task;
                _storage.SaveTaskList(list);
            }

            using var circlePaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = SKColors.Gray, StrokeWidth = 1 * _ui.ItemScale };
            canvas.DrawOval(circleRect, circlePaint);
            
            if (task.IsCompleted)
            {
                using var checkPaint = new SKPaint { IsAntialias = true, Color = SKColors.Green, Style = SKPaintStyle.Fill };
                circleRect.Inflate(-1 * _ui.ItemScale, -1 * _ui.ItemScale);
                canvas.DrawOval(circleRect, checkPaint);
            }

            float textX = rect.Left + padding + circleSize + 6 * _ui.ItemScale;
            float textY = ty + itemHeight * 0.75f;
            canvas.DrawText(task.Text, textX, textY, textPaint);

            if (focused && _ui.FocusedTaskListIndex == i)
            {
                float caretX = textX + textPaint.MeasureText(task.Text.Substring(0, Math.Clamp(_ui.CaretIndex, 0, task.Text.Length)));
                using var caretPaint = new SKPaint { Color = SKColors.Black, StrokeWidth = 1 * _ui.ItemScale };
                canvas.DrawLine(caretX, ty + itemHeight * 0.2f, caretX, ty + itemHeight * 0.8f, caretPaint);
            }

            var rowRect = new SKRect(textX, ty, rect.Right - padding, ty + itemHeight);
            if (focused && _input.LeftMousePressed && rowRect.Contains(_input.MousePosition.X, _input.MousePosition.Y))
            {
                _ui.FocusedTaskListIndex = i;
                float localX = _input.MousePosition.X - textX;
                int bestIdx = 0;
                float minDist = float.MaxValue;
                for (int j = 0; j <= task.Text.Length; j++)
                {
                    float px = textPaint.MeasureText(task.Text.Substring(0, j));
                    float d = Math.Abs(px - localX);
                    if (d < minDist) { minDist = d; bestIdx = j; }
                    else break;
                }
                _ui.CaretIndex = bestIdx;
            }
        }

        if (focused)
        {
            using var borderPaint = new SKPaint { IsAntialias = true, Color = SKColors.DodgerBlue, Style = SKPaintStyle.Stroke, StrokeWidth = 2 * _ui.ItemScale };
            canvas.DrawRoundRect(rect, radius, radius, borderPaint);
        }

        if (active && (_input.LeftMouseReleased || _input.RightMouseReleased)) _ui.ActiveElementId = null;
        return clicked;
    }

    public static List<(string text, int startIdx)> GetNoteVisualLines(string content, float width, SKPaint paint)
    {
        var visualLines = new List<(string text, int startIdx)>();
        int pos = 0;
        while (pos <= content.Length)
        {
            int nextNewline = content.IndexOf('\n', pos);
            string logicalLine;
            int nextPos;
            if (nextNewline == -1)
            {
                logicalLine = content.Substring(pos);
                nextPos = content.Length + 1;
            }
            else
            {
                logicalLine = content.Substring(pos, nextNewline - pos);
                nextPos = nextNewline + 1;
            }

            var words = logicalLine.Split(' ');
            var currentLineText = "";
            int lineStartIndex = pos;

            for (int i = 0; i < words.Length; i++)
            {
                var word = words[i];
                var testLine = currentLineText == "" ? word : currentLineText + " " + word;
                
                if (paint.MeasureText(testLine) > width && currentLineText != "")
                {
                    visualLines.Add((currentLineText, lineStartIndex));
                    lineStartIndex += currentLineText.Length + 1;
                    currentLineText = word;
                }
                else
                {
                    currentLineText = testLine;
                }
            }
            visualLines.Add((currentLineText, lineStartIndex));
            
            pos = nextPos;
            if (pos > content.Length) break;
        }
        return visualLines;
    }

    void DrawTextInRect(SKCanvas canvas, string text, SKRect rect, SKPaint paint, bool isFocused = false)
    {
        if (text == null) text = "";
        
        var visualLines = GetNoteVisualLines(text, rect.Width, paint);
        float y = rect.Top + paint.TextSize;
        float lineHeight = paint.TextSize + 2;

        if (isFocused && text == "")
        {
            using var caretPaint = new SKPaint { Color = SKColors.Black, StrokeWidth = 1 * _ui.ItemScale };
            canvas.DrawLine(rect.Left, y - paint.TextSize, rect.Left, y + 2, caretPaint);
        }

        foreach (var line in visualLines)
        {
            if (y - paint.TextSize > rect.Bottom) break;
            RenderLine(line.text, rect.Left, y, line.startIdx);
            y += lineHeight;
        }

        void RenderLine(string lineText, float lx, float ly, int startIdx)
        {
            canvas.DrawText(lineText, lx, ly, paint);
            
            if (isFocused)
            {
                var lineRect = new SKRect(lx, ly - paint.TextSize, rect.Right, ly + 2);
                if (_input.LeftMousePressed && lineRect.Contains(_input.MousePosition.X, _input.MousePosition.Y))
                {
                    float localX = _input.MousePosition.X - lx;
                    int bestIdx = 0;
                    float minDist = float.MaxValue;
                    for (int j = 0; j <= lineText.Length; j++)
                    {
                        float px = paint.MeasureText(lineText.Substring(0, j));
                        float d = Math.Abs(px - localX);
                        if (d < minDist) { minDist = d; bestIdx = j; }
                        else break;
                    }
                    _ui.CaretIndex = startIdx + bestIdx;
                }
                
                if (_ui.CaretIndex >= startIdx && _ui.CaretIndex <= startIdx + lineText.Length)
                {
                    float caretX = lx + paint.MeasureText(lineText.Substring(0, _ui.CaretIndex - startIdx));
                    using var caretPaint = new SKPaint { Color = SKColors.Black, StrokeWidth = 1 * _ui.ItemScale };
                    canvas.DrawLine(caretX, ly - paint.TextSize, caretX, ly + 2, caretPaint);
                }
            }
        }
    }

    StorageService.Note GetNote(string id)
    {
        return _storage.GetNote(id);
    }

    StorageService.TaskList GetTaskList(string id)
    {
        return _storage.GetTaskList(id);
    }

    bool TextButton(SKCanvas canvas, SKRect rect, string text, string id)
    {
        bool hovered = rect.Contains(_input.MousePosition.X, _input.MousePosition.Y);

        if (hovered && _input.LeftMousePressed)
        {
            _ui.ActiveElementId = id;
        }

        bool active = _ui.ActiveElementId == id;
        bool pressed = active && _input.LeftMouseDown;
        bool clicked = active && hovered && _input.LeftMouseReleased;

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

        if (active && (_input.LeftMouseReleased || _input.RightMouseReleased))
        {
            _ui.ActiveElementId = null;
        }

        return clicked;
    }

    bool DockItemButton(SKCanvas canvas, SKRect rect, StorageService.DockItem item, string id, out bool rightClicked)
    {
        rightClicked = false;
        bool hovered = rect.Contains(_input.MousePosition.X, _input.MousePosition.Y);

        if (hovered && (_input.LeftMousePressed || _input.RightMousePressed))
        {
            _ui.ActiveElementId = id;
        }

        bool active = _ui.ActiveElementId == id;
        bool pressed = active && (_input.LeftMouseDown || _input.RightMouseDown);
        bool clicked = active && hovered && _input.LeftMouseReleased;
        rightClicked = active && hovered && _input.RightMouseReleased;

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

        float radius = 12 * _ui.ItemScale;
        canvas.DrawRoundRect(rect, radius, radius, cardPaint);

        SKImage? icon = _storage.GetDockItemIcon(item.Value);

        if (icon is not null)
        {
            // icon rect is inner rect with small padding
            var iconPadding = 1 * _ui.ItemScale;
            SKRect iconRect = new SKRect(
                rect.Left + iconPadding,
                rect.Top + iconPadding,
                rect.Right - iconPadding,
                rect.Bottom - iconPadding
            );
            
            canvas.DrawImage(icon, iconRect);
        }
        
        if (active && (_input.LeftMouseReleased || _input.RightMouseReleased))
        {
            _ui.ActiveElementId = null;
        }

        return clicked;
    }

    bool AddButton(SKCanvas canvas, SKRect rect, string id)
    {
        bool hovered = rect.Contains(_input.MousePosition.X, _input.MousePosition.Y);

        if (hovered && _input.LeftMousePressed)
        {
            _ui.ActiveElementId = id;
        }

        bool active = _ui.ActiveElementId == id;
        bool pressed = active && _input.LeftMouseDown;
        bool clicked = active && hovered && _input.LeftMouseReleased;

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

        if (active && (_input.LeftMouseReleased || _input.RightMouseReleased))
        {
            _ui.ActiveElementId = null;
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

        _ui.AddMenuRect = GetAddMenuRect(addButtonRect);


        using var menuPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(255, 255, 255, 245)
        };

        canvas.DrawRoundRect(_ui.AddMenuRect, 12, 12, menuPaint);

        var itemRect = new SKRect(
            _ui.AddMenuRect.Left + menuPadding,
            _ui.AddMenuRect.Top + menuPadding,
            _ui.AddMenuRect.Right - menuPadding,
            _ui.AddMenuRect.Top + menuPadding + rowHeight);

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
        bool hovered = rect.Contains(_input.MousePosition.X, _input.MousePosition.Y);

        if (hovered && _input.LeftMousePressed)
        {
            _ui.ActiveElementId = id;
        }

        bool active = _ui.ActiveElementId == id;
        bool pressed = active && _input.LeftMouseDown;
        bool clicked = active && hovered && _input.LeftMouseReleased;

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

        if (active && (_input.LeftMouseReleased || _input.RightMouseReleased))
        {
            _ui.ActiveElementId = null;
        }

        return clicked;
    }

    SKRect GetContextMenuRect(Vector2D<int> position)
    {
        const float menuWidth = 140;
        const float rowHeight = 40;
        const float menuPadding = 6;

        // Position menu so it stays within window bounds if possible
        float x = position.X;
        float y = position.Y;
        
        if (x + menuWidth > _ui.LastFramebufferSize.X)
        {
            x = _ui.LastFramebufferSize.X - menuWidth - 4;
        }
        
        int rowCount = 2; // Remove, Rename
        float totalHeight = (rowHeight * rowCount) + menuPadding * 2;

        if (y + totalHeight > _ui.LastFramebufferSize.Y)
        {
            y = _ui.LastFramebufferSize.Y - totalHeight - 4;
        }

        _ui.ContextMenuRect = new SKRect(
            x,
            y,
            x + menuWidth,
            y + totalHeight);

        return _ui.ContextMenuRect;
    }

    string? DrawContextMenu(SKCanvas canvas)
    {
        const float rowHeight = 40;
        const float menuPadding = 6;
        
        _ui.ContextMenuRect = GetContextMenuRect(_ui.ContextMenuPosition);

        using var menuPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(255, 255, 255, 245)
        };

        canvas.DrawRoundRect(_ui.ContextMenuRect, 12, 12, menuPaint);

        var renameRect = new SKRect(
            _ui.ContextMenuRect.Left + menuPadding,
            _ui.ContextMenuRect.Top + menuPadding,
            _ui.ContextMenuRect.Right - menuPadding,
            _ui.ContextMenuRect.Top + menuPadding + rowHeight);

        var removeRect = new SKRect(
            renameRect.Left,
            renameRect.Bottom,
            renameRect.Right,
            renameRect.Bottom + rowHeight);

        if (MenuItem(canvas, renameRect, "Rename", "context-menu-rename"))
        {
            return "Rename";
        }

        if (MenuItem(canvas, removeRect, "Remove", "context-menu-remove"))
        {
            return "Remove";
        }

        return null;
    }
}