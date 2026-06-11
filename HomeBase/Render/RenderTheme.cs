using SkiaSharp;

namespace HomeBase.Render;

public sealed class RenderTheme : IDisposable
{
    public SurfacePalette Surfaces { get; }
    public TextPalette Text { get; }
    public ButtonPalette Buttons { get; }
    public PrimaryButtonPalette PrimaryButton { get; }
    public CardPalette Cards { get; }
    public RenamePalette Rename { get; }
    public MenuPalette Menu { get; }

    public SKTypeface RegularTypeface { get; }
    public SKTypeface SemiboldTypeface { get; }

    public SKPaint PanelBackgroundPaint { get; }
    public SKPaint PanelOuterBorderPaint { get; }
    public SKPaint PanelInnerBorderPaint { get; }
    public SKPaint FooterBackgroundPaint { get; }

    public SKPaint TextPrimaryPaint { get; }
    public SKPaint TextSecondaryPaint { get; }
    public SKPaint CaretPaint { get; }
    public SKPaint MenuBackgroundPaint { get; }

    private bool _disposed;

    public RenderTheme(SavedTheme? savedTheme)
    {
        savedTheme ??= new SavedTheme();

        Surfaces = new SurfacePalette(
            Parse(savedTheme.CanvasClear),
            Parse(savedTheme.PanelBackground),
            Parse(savedTheme.PanelOuterBorder),
            Parse(savedTheme.PanelInnerBorder),
            Parse(savedTheme.FooterBackground));

        Text = new TextPalette(
            Parse(savedTheme.TextPrimary),
            Parse(savedTheme.TextSecondary),
            Parse(savedTheme.TextOnPrimary),
            Parse(savedTheme.Caret));

        Buttons = new ButtonPalette(
            Parse(savedTheme.ButtonBackground),
            Parse(savedTheme.ButtonBackgroundHover),
            Parse(savedTheme.ButtonBackgroundPressed),
            Parse(savedTheme.ButtonText));

        PrimaryButton = new PrimaryButtonPalette(
            Parse(savedTheme.PrimaryButtonBackground),
            Parse(savedTheme.PrimaryButtonBackgroundHover),
            Parse(savedTheme.PrimaryButtonBackgroundPressed),
            Parse(savedTheme.PrimaryButtonForeground));

        Cards = new CardPalette(
            new FileCardPalette(
                Parse(savedTheme.FileCardBackground),
                Parse(savedTheme.FileCardBackgroundHover),
                Parse(savedTheme.FileCardBackgroundPressed)),
            new NoteCardPalette(
                Parse(savedTheme.NoteBackground),
                Parse(savedTheme.NoteBackgroundFocused),
                Parse(savedTheme.NoteBackgroundPressed),
                Parse(savedTheme.NoteText),
                Parse(savedTheme.NoteFocusBorder)),
            new TaskListCardPalette(
                Parse(savedTheme.TaskListBackground),
                Parse(savedTheme.TaskListBackgroundFocused),
                Parse(savedTheme.TaskListBackgroundPressed),
                Parse(savedTheme.TaskListText),
                Parse(savedTheme.TaskListFocusBorder),
                Parse(savedTheme.TaskCircleBorder),
                Parse(savedTheme.TaskCompletedFill)));

        Rename = new RenamePalette(
            Parse(savedTheme.RenameBackground),
            Parse(savedTheme.RenameBorder));

        Menu = new MenuPalette(
            Parse(savedTheme.MenuBackground),
            Parse(savedTheme.MenuItemBackground),
            Parse(savedTheme.MenuItemBackgroundHover),
            Parse(savedTheme.MenuItemBackgroundPressed),
            Parse(savedTheme.MenuItemText));

        RegularTypeface = SKTypeface.FromFamilyName("Segoe UI");
        SemiboldTypeface = SKTypeface.FromFamilyName("Segoe UI Semibold");

        PanelBackgroundPaint = Fill(Surfaces.PanelBackground);
        PanelOuterBorderPaint = Stroke(Surfaces.PanelOuterBorder, 1);
        PanelInnerBorderPaint = Stroke(Surfaces.PanelInnerBorder, 1);
        FooterBackgroundPaint = Fill(Surfaces.FooterBackground);

        TextPrimaryPaint = Fill(Text.Primary);
        TextSecondaryPaint = Fill(Text.Secondary);
        CaretPaint = Stroke(Text.Caret, 1);
        MenuBackgroundPaint = Fill(Menu.Background);
    }

    public SKPaint CreateFillPaint(SKColor color)
    {
        return Fill(color);
    }

    public SKPaint CreateStrokePaint(SKColor color, float strokeWidth)
    {
        return Stroke(color, strokeWidth);
    }

    public SKColor GetNeutralButtonColor(bool hovered, bool pressed)
    {
        return pressed
            ? Buttons.BackgroundPressed
            : hovered
                ? Buttons.BackgroundHover
                : Buttons.Background;
    }

    public SKColor GetPrimaryButtonColor(bool hovered, bool pressed)
    {
        return pressed
            ? PrimaryButton.BackgroundPressed
            : hovered
                ? PrimaryButton.BackgroundHover
                : PrimaryButton.Background;
    }

    public SKColor GetFileCardColor(bool hovered, bool pressed)
    {
        return pressed
            ? Cards.File.BackgroundPressed
            : hovered
                ? Cards.File.BackgroundHover
                : Cards.File.Background;
    }

    public SKColor GetNoteCardColor(bool focused, bool pressed)
    {
        return pressed
            ? Cards.Note.BackgroundPressed
            : focused
                ? Cards.Note.BackgroundFocused
                : Cards.Note.Background;
    }

    public SKColor GetTaskListCardColor(bool focused, bool pressed)
    {
        return pressed
            ? Cards.TaskList.BackgroundPressed
            : focused
                ? Cards.TaskList.BackgroundFocused
                : Cards.TaskList.Background;
    }

    public SKColor GetMenuItemColor(bool hovered, bool pressed)
    {
        return pressed
            ? Menu.ItemBackgroundPressed
            : hovered
                ? Menu.ItemBackgroundHover
                : Menu.ItemBackground;
    }

    private static SKPaint Fill(SKColor color)
    {
        return new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = color
        };
    }

    private static SKPaint Stroke(SKColor color, float strokeWidth)
    {
        return new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = strokeWidth,
            Color = color
        };
    }

    private static SKColor Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return SKColors.Transparent;
        }

        return SKColor.Parse(value);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        PanelBackgroundPaint.Dispose();
        PanelOuterBorderPaint.Dispose();
        PanelInnerBorderPaint.Dispose();
        FooterBackgroundPaint.Dispose();

        TextPrimaryPaint.Dispose();
        TextSecondaryPaint.Dispose();
        CaretPaint.Dispose();
        MenuBackgroundPaint.Dispose();

        RegularTypeface.Dispose();
        SemiboldTypeface.Dispose();
    }
}

public readonly record struct SurfacePalette(
    SKColor CanvasClear,
    SKColor PanelBackground,
    SKColor PanelOuterBorder,
    SKColor PanelInnerBorder,
    SKColor FooterBackground);

public readonly record struct TextPalette(
    SKColor Primary,
    SKColor Secondary,
    SKColor OnPrimary,
    SKColor Caret);

public readonly record struct ButtonPalette(
    SKColor Background,
    SKColor BackgroundHover,
    SKColor BackgroundPressed,
    SKColor Text);

public readonly record struct PrimaryButtonPalette(
    SKColor Background,
    SKColor BackgroundHover,
    SKColor BackgroundPressed,
    SKColor Foreground);

public readonly record struct CardPalette(
    FileCardPalette File,
    NoteCardPalette Note,
    TaskListCardPalette TaskList);

public readonly record struct FileCardPalette(
    SKColor Background,
    SKColor BackgroundHover,
    SKColor BackgroundPressed);

public readonly record struct NoteCardPalette(
    SKColor Background,
    SKColor BackgroundFocused,
    SKColor BackgroundPressed,
    SKColor Text,
    SKColor FocusBorder);

public readonly record struct TaskListCardPalette(
    SKColor Background,
    SKColor BackgroundFocused,
    SKColor BackgroundPressed,
    SKColor Text,
    SKColor FocusBorder,
    SKColor CircleBorder,
    SKColor CompletedFill);

public readonly record struct RenamePalette(
    SKColor Background,
    SKColor Border);

public readonly record struct MenuPalette(
    SKColor Background,
    SKColor ItemBackground,
    SKColor ItemBackgroundHover,
    SKColor ItemBackgroundPressed,
    SKColor ItemText);