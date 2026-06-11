namespace HomeBase.Render;

public sealed class SavedTheme
{
    public string? Name { get; set; } = "Default Light";

    public string CanvasClear { get; set; } = "#00000000";

    public string PanelBackground { get; set; } = "#E8F2F2F2";
    public string PanelOuterBorder { get; set; } = "#78FFFFFF";
    public string PanelInnerBorder { get; set; } = "#18000000";

    public string FooterBackground { get; set; } = "#69FFFFFF";

    public string TextPrimary { get; set; } = "#FF000000";
    public string TextSecondary { get; set; } = "#FF696969";
    public string TextOnPrimary { get; set; } = "#FFFFFFFF";
    public string Caret { get; set; } = "#FF000000";

    public string ButtonBackground { get; set; } = "#FFC8C8C8";
    public string ButtonBackgroundHover { get; set; } = "#FFDCDCDC";
    public string ButtonBackgroundPressed { get; set; } = "#FFB4B4B4";
    public string ButtonText { get; set; } = "#FF000000";

    public string PrimaryButtonBackground { get; set; } = "#FF5078DC";
    public string PrimaryButtonBackgroundHover { get; set; } = "#FF5F87EB";
    public string PrimaryButtonBackgroundPressed { get; set; } = "#FF3C64C8";
    public string PrimaryButtonForeground { get; set; } = "#FFFFFFFF";

    public string FileCardBackground { get; set; } = "#82FFFFFF";
    public string FileCardBackgroundHover { get; set; } = "#B4FFFFFF";
    public string FileCardBackgroundPressed { get; set; } = "#B4E6E6E6";

    public string NoteBackground { get; set; } = "#FFFFFFDC";
    public string NoteBackgroundFocused { get; set; } = "#FFFFFFB4";
    public string NoteBackgroundPressed { get; set; } = "#FFF0F0A0";
    public string NoteText { get; set; } = "#FF000000";
    public string NoteFocusBorder { get; set; } = "#FFFFA500";

    public string TaskListBackground { get; set; } = "#FFFFFFFF";
    public string TaskListBackgroundFocused { get; set; } = "#FFF0F0F0";
    public string TaskListBackgroundPressed { get; set; } = "#FFDCDCDC";
    public string TaskListText { get; set; } = "#FF000000";
    public string TaskListFocusBorder { get; set; } = "#FF1E90FF";
    public string TaskCircleBorder { get; set; } = "#FF808080";
    public string TaskCompletedFill { get; set; } = "#FF008000";

    public string RenameBackground { get; set; } = "#FFFFFFFF";
    public string RenameBorder { get; set; } = "#FF1E90FF";

    public string MenuBackground { get; set; } = "#F5FFFFFF";
    public string MenuItemBackground { get; set; } = "#00000000";
    public string MenuItemBackgroundHover { get; set; } = "#FFEBEBEB";
    public string MenuItemBackgroundPressed { get; set; } = "#FFDCDCDC";
    public string MenuItemText { get; set; } = "#FF202020";
}