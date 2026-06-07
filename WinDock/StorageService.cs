using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace WinDock;

public class StorageService
{
    private static readonly StorageService _instance = new();
    private readonly string _filePath;

    public static StorageService Instance => _instance;

    internal StorageService()
    {
        string appDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WinDock"
        );
        Directory.CreateDirectory(appDataFolder);
        _filePath = Path.Combine(appDataFolder, "storage.json");
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

    public record struct DockItem(string Name, string Path);
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<StorageService.DockItem>))]
internal partial class DockItemSerializerContext : JsonSerializerContext
{
}