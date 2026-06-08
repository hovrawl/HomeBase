using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace HomeBase;

public class StorageService
{
    private static readonly StorageService _instance = new();
    private readonly string _filePath;
    private readonly string _notesPath;
    private readonly string _taskListsPath;

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
    }

    public List<DockItem> Load()
    {
        if (!File.Exists(_filePath))
            return new List<DockItem>();

        string json = File.ReadAllText(_filePath);
        return JsonSerializer.Deserialize(json, DockItemSerializerContext.Default.ListDockItem) ?? new List<DockItem>();
    }

    public void Save(List<DockItem> items)
    {
        string json = JsonSerializer.Serialize(items, DockItemSerializerContext.Default.ListDockItem);
        File.WriteAllText(_filePath, json);
    }

    public Note LoadNote(string id)
    {
        string path = Path.Combine(_notesPath, $"{id}.json");
        if (!File.Exists(path)) return new Note(id, "");
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize(json, DockItemSerializerContext.Default.Note);
    }

    public void SaveNote(Note note)
    {
        string path = Path.Combine(_notesPath, $"{note.Id}.json");
        string json = JsonSerializer.Serialize(note, DockItemSerializerContext.Default.Note);
        File.WriteAllText(path, json);
    }

    public TaskList LoadTaskList(string id)
    {
        string path = Path.Combine(_taskListsPath, $"{id}.json");
        if (!File.Exists(path)) return new TaskList(id, new List<TaskItem>());
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize(json, DockItemSerializerContext.Default.TaskList);
    }

    public void SaveTaskList(TaskList taskList)
    {
        string path = Path.Combine(_taskListsPath, $"{taskList.Id}.json");
        string json = JsonSerializer.Serialize(taskList, DockItemSerializerContext.Default.TaskList);
        File.WriteAllText(path, json);
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

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<StorageService.DockItem>))]
[JsonSerializable(typeof(StorageService.Note))]
[JsonSerializable(typeof(StorageService.TaskList))]
[JsonSerializable(typeof(List<StorageService.TaskItem>))]
internal partial class DockItemSerializerContext : JsonSerializerContext
{
}