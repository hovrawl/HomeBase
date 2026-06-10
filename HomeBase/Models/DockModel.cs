namespace HomeBase.Models;

public struct DockModel
{
    public List<StorageService.DockItem> Items { get; set; } = [];
    public Dictionary<string, StorageService.Note> NoteCache { get; } = new();
    public Dictionary<string, StorageService.TaskList> TaskListCache { get; } = new();
    
    public DockModel()
    {
    }

    public StorageService.Note GetNote(string id, StorageService storage)
    {
        if (NoteCache.TryGetValue(id, out var note)) return note;
        note = storage.LoadNote(id);
        NoteCache[id] = note;
        return note;
    }

    public StorageService.TaskList GetTaskList(string id, StorageService storage)
    {
        if (TaskListCache.TryGetValue(id, out var list)) return list;
        list = storage.LoadTaskList(id);
        TaskListCache[id] = list;
        return list;
    }
}
