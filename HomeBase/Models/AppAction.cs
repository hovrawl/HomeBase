namespace HomeBase.Models;

public abstract record AppAction
{
    public sealed record ShowAbout : AppAction;
    public sealed record OpenAddItemDialog : AppAction;
    public sealed record CreateNote : AppAction;
    public sealed record CreateTaskList : AppAction;
    public sealed record RemoveDockItem(int Index) : AppAction;
    public sealed record OpenDockItem(int Index) : AppAction;
    public sealed record SummonDock : AppAction;
    public sealed record DismissDock : AppAction;
    public sealed record OpenFile(string Path) : AppAction;
    public sealed record RenameItem(int Index, string NewName) : AppAction;
}
