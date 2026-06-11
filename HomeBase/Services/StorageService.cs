using System.Text.Json;
using System.Text.Json.Serialization;
using SkiaSharp;

namespace HomeBase.Services;

public class StorageService
{
    private static readonly StorageService _instance = new();
    private readonly string _filePath;
    private readonly string _notesPath;
    private readonly string _taskListsPath;

    
    private readonly Dictionary<string, Note> _noteCache = new();
    private readonly Dictionary<string, TaskList> _taskListCache = new();
    private readonly Dictionary<string, SKImage> _iconCache = new();

    public List<DockItem> Items { get; private set; }
    
    public static StorageService Instance => _instance;

    internal StorageService()
    {
        string appDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HomeBase"
        );
        Directory.CreateDirectory(appDataFolder);
        _filePath = Path.Combine(appDataFolder, "storage.json");

        _notesPath = Path.Combine(appDataFolder, "Notes");
        Directory.CreateDirectory(_notesPath);

        _taskListsPath = Path.Combine(appDataFolder, "TaskLists");
        Directory.CreateDirectory(_taskListsPath);

        Items = LoadItems();
    }

    private List<DockItem> LoadItems()
    {
        if (!File.Exists(_filePath))
            return new List<DockItem>();

        string json = File.ReadAllText(_filePath);
        return JsonSerializer.Deserialize(json, Helpers.DockItemSerializerContext.Default.ListDockItem) ?? new List<DockItem>();
    }

    public void Save()
    {
        string json = JsonSerializer.Serialize(Items, Helpers.DockItemSerializerContext.Default.ListDockItem);
        File.WriteAllText(_filePath, json);
    }

    public Note GetNote(string id)
    {
        if (_noteCache.TryGetValue(id, out var note)) return note;
        
        string path = Path.Combine(_notesPath, $"{id}.json");
        if (!File.Exists(path))
        {
            note = new Note(id, "");
        }
        else
        {
            string json = File.ReadAllText(path);
            note = JsonSerializer.Deserialize(json, Helpers.DockItemSerializerContext.Default.Note);
        }
        
        _noteCache[id] = note;
        return note;
    }

    public void SaveNote(Note note)
    {
        _noteCache[note.Id] = note;
        string path = Path.Combine(_notesPath, $"{note.Id}.json");
        string json = JsonSerializer.Serialize(note, Helpers.DockItemSerializerContext.Default.Note);
        File.WriteAllText(path, json);
    }

    public TaskList GetTaskList(string id)
    {
        if (_taskListCache.TryGetValue(id, out var list)) return list;

        string path = Path.Combine(_taskListsPath, $"{id}.json");
        if (!File.Exists(path))
        {
            list = new TaskList(id, new List<TaskItem>());
        }
        else
        {
            string json = File.ReadAllText(path);
            list = JsonSerializer.Deserialize(json, Helpers.DockItemSerializerContext.Default.TaskList);
        }
        
        _taskListCache[id] = list;
        return list;
    }

    public void SaveTaskList(TaskList taskList)
    {
        _taskListCache[taskList.Id] = taskList;
        string path = Path.Combine(_taskListsPath, $"{taskList.Id}.json");
        string json = JsonSerializer.Serialize(taskList, Helpers.DockItemSerializerContext.Default.TaskList);
        File.WriteAllText(path, json);
    }
    
    internal SKImage? GetDockItemIcon(string path)
    {
        if (_iconCache.TryGetValue(path, out SKImage? image))
        {
            return image;
        }

        image = Windows.Windows.LoadIconImageForFile(path);


        if (image is not null)
        {
            _iconCache[path] = image;
        }

        return image;
    }

    public record struct DockItem(string Name, string Value, ItemType Type);
    public record struct Note(string Id, string Content);
    public record struct TaskItem(string Text, bool IsCompleted);
    public record struct TaskList(string Id, List<TaskItem> Tasks);
}

public enum ItemType
{
    File,
    Note,
    TaskList
}

