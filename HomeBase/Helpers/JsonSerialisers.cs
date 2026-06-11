using System.Text.Json.Serialization;
using HomeBase.Services;

namespace HomeBase.Helpers;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<StorageService.DockItem>))]
[JsonSerializable(typeof(StorageService.Note))]
[JsonSerializable(typeof(StorageService.TaskList))]
[JsonSerializable(typeof(List<StorageService.TaskItem>))]
internal partial class DockItemSerializerContext : JsonSerializerContext
{
}