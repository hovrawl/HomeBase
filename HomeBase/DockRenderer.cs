using System.Collections.Concurrent;
using System.Numerics;
using HomeBase.Models;
using SkiaSharp;

namespace HomeBase;

public sealed class DockRenderer
{
    private readonly UIState _state;
    private readonly InputState _input;
    private readonly StorageService _storage;
    private readonly ConcurrentQueue<Action> _actions;

    public DockRenderer(
        UIState state,
        InputState input,
        StorageService storage,
        ConcurrentQueue<Action> actions)
    {
        _state = state;
        _input = input;
        _storage = storage;
        _actions = actions;
    }

    public void Render(SKCanvas canvas)
    {
        SKRect panelRect = DrawBackground(canvas);
        DrawDock(canvas, panelRect);
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
            else if (selectedContextAction == "Rename")
            {
                IsContextMenuOpen = false;
                int indexToRename = ContextMenuItemIndex;
                RenamingItemId = $"dock-item-{indexToRename}";
                RenamingCaretIndex = DockItems[indexToRename].Name.Length;
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
        float cellWidth = baseCellSize * ItemScale;
        float labelHeight = 20 * ItemScale;
        float cellHeight = cellWidth + labelHeight;
        float cardSize = (baseCellSize - 16) * ItemScale;

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

            var labelRect = new SKRect(x, y + cardSize + 2 * ItemScale, x + cardSize, y + cardSize + labelHeight);
            if (labelRect.Contains(MousePosition.X, MousePosition.Y)) anyHovered = true;
            DrawItemLabel(canvas, labelRect, item, stableId);

            if (clicked)
            {
                if (RenamingItemId != null && RenamingItemId != stableId)
                {
                    SaveRenaming();
                }

                if (item.Type == ItemType.File)
                {
                    ActionQueue.Enqueue(() => OnDockItemClicked(item));
                }
                else
                {
                    if (FocusedItemId != stableId)
                    {
                        FocusedItemId = stableId;
                        if (item.Type == ItemType.Note)
                        {
                            CaretIndex = GetNote(item.Value).Content.Length;
                        }
                        else if (item.Type == ItemType.TaskList)
                        {
                            var list = GetTaskList(item.Value);
                            FocusedTaskListIndex = list.Tasks.Count - 1;
                            CaretIndex = FocusedTaskListIndex >= 0 ? list.Tasks[FocusedTaskListIndex].Text.Length : 0;
                        }
                    }
                }
            }

            if (rightClicked)
            {
                if (RenamingItemId != null) SaveRenaming();
                
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
                if (RenamingItemId != null) SaveRenaming();
            }
        }

        return rows * cellHeight;
    }

    void DrawItemLabel(SKCanvas canvas, SKRect rect, StorageService.DockItem item, string id)
    {
        bool isRenaming = RenamingItemId == id;
        string text = item.Name;
        
        using var paint = new SKPaint 
        { 
            IsAntialias = true, 
            Color = SKColors.Black, 
            TextSize = 11 * ItemScale,
            Typeface = SKTypeface.FromFamilyName("Segoe UI")
        };

        if (isRenaming)
        {
            // Draw background for editing
            using var bgPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
            canvas.DrawRoundRect(rect, 4 * ItemScale, 4 * ItemScale, bgPaint);
            using var borderPaint = new SKPaint { Color = SKColors.DodgerBlue, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1 * ItemScale };
            canvas.DrawRoundRect(rect, 4 * ItemScale, 4 * ItemScale, borderPaint);

            float textX = rect.Left + 4 * ItemScale;
            float textY = rect.MidY + paint.TextSize / 3;
            canvas.DrawText(text, textX, textY, paint);

            // Draw caret
            float caretX = textX + paint.MeasureText(text.Substring(0, Math.Clamp(RenamingCaretIndex, 0, text.Length)));
            using var caretPaint = new SKPaint { Color = SKColors.Black, StrokeWidth = 1 * ItemScale };
            canvas.DrawLine(caretX, rect.Top + 4 * ItemScale, caretX, rect.Bottom - 4 * ItemScale, caretPaint);

            // Handle click to move caret
            if (LeftMousePressed && rect.Contains(MousePosition.X, MousePosition.Y))
            {
                float localX = MousePosition.X - textX;
                int bestIdx = 0;
                float minDist = float.MaxValue;
                for (int j = 0; j <= text.Length; j++)
                {
                    float px = paint.MeasureText(text.Substring(0, j));
                    float d = Math.Abs(px - localX);
                    if (d < minDist) { minDist = d; bestIdx = j; }
                    else break;
                }
                RenamingCaretIndex = bestIdx;
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
        if (RenamingItemId != null && RenamingItemId.StartsWith("dock-item-"))
        {
            StorageService.Instance.Save(DockItems);
        }
        RenamingItemId = null;
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
        DrawTextInRect(canvas, note.Content, textRect, textPaint, focused);

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

            float textX = rect.Left + padding + circleSize + 6 * ItemScale;
            float textY = ty + itemHeight * 0.75f;
            canvas.DrawText(task.Text, textX, textY, textPaint);

            if (focused && FocusedTaskListIndex == i)
            {
                float caretX = textX + textPaint.MeasureText(task.Text.Substring(0, Math.Clamp(CaretIndex, 0, task.Text.Length)));
                using var caretPaint = new SKPaint { Color = SKColors.Black, StrokeWidth = 1 * ItemScale };
                canvas.DrawLine(caretX, ty + itemHeight * 0.2f, caretX, ty + itemHeight * 0.8f, caretPaint);
            }

            var rowRect = new SKRect(textX, ty, rect.Right - padding, ty + itemHeight);
            if (focused && LeftMousePressed && rowRect.Contains(MousePosition.X, MousePosition.Y))
            {
                FocusedTaskListIndex = i;
                float localX = MousePosition.X - textX;
                int bestIdx = 0;
                float minDist = float.MaxValue;
                for (int j = 0; j <= task.Text.Length; j++)
                {
                    float px = textPaint.MeasureText(task.Text.Substring(0, j));
                    float d = Math.Abs(px - localX);
                    if (d < minDist) { minDist = d; bestIdx = j; }
                    else break;
                }
                CaretIndex = bestIdx;
            }
        }

        if (focused)
        {
            using var borderPaint = new SKPaint { IsAntialias = true, Color = SKColors.DodgerBlue, Style = SKPaintStyle.Stroke, StrokeWidth = 2 * ItemScale };
            canvas.DrawRoundRect(rect, radius, radius, borderPaint);
        }

        if (active && (LeftMouseReleased || RightMouseReleased)) ActiveElementId = null;
        return clicked;
    }

    List<(string text, int startIdx)> GetNoteVisualLines(string content, float width, SKPaint paint)
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
            using var caretPaint = new SKPaint { Color = SKColors.Black, StrokeWidth = 1 * ItemScale };
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
                if (LeftMousePressed && lineRect.Contains(MousePosition.X, MousePosition.Y))
                {
                    float localX = MousePosition.X - lx;
                    int bestIdx = 0;
                    float minDist = float.MaxValue;
                    for (int j = 0; j <= lineText.Length; j++)
                    {
                        float px = paint.MeasureText(lineText.Substring(0, j));
                        float d = Math.Abs(px - localX);
                        if (d < minDist) { minDist = d; bestIdx = j; }
                        else break;
                    }
                    CaretIndex = startIdx + bestIdx;
                }
                
                if (CaretIndex >= startIdx && CaretIndex <= startIdx + lineText.Length)
                {
                    float caretX = lx + paint.MeasureText(lineText.Substring(0, CaretIndex - startIdx));
                    using var caretPaint = new SKPaint { Color = SKColors.Black, StrokeWidth = 1 * ItemScale };
                    canvas.DrawLine(caretX, ly - paint.TextSize, caretX, ly + 2, caretPaint);
                }
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
        
        int rowCount = 2; // Remove, Rename
        float totalHeight = (rowHeight * rowCount) + menuPadding * 2;

        if (y + totalHeight > lastFramebufferSize.Y)
        {
            y = lastFramebufferSize.Y - totalHeight - 4;
        }

        ContextMenuRect = new SKRect(
            x,
            y,
            x + menuWidth,
            y + totalHeight);

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

        var renameRect = new SKRect(
            ContextMenuRect.Left + menuPadding,
            ContextMenuRect.Top + menuPadding,
            ContextMenuRect.Right - menuPadding,
            ContextMenuRect.Top + menuPadding + rowHeight);

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