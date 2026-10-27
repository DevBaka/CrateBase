using System.Text.Json;
namespace Cratebase.Beatport;
public sealed class ListeningHistory(string path)
{
    private HashSet<string> heard = [];
    public async Task LoadAsync() { if (File.Exists(path)) heard = JsonSerializer.Deserialize<HashSet<string>>(await File.ReadAllTextAsync(path)) ?? []; }
    public bool Contains(Track t) => heard.Contains($"id:{t.Id}") || heard.Contains(Deduplication.Key(t));
    public async Task MarkAsync(Track t)
    {
        heard.Add($"id:{t.Id}"); heard.Add(Deduplication.Key(t)); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp"; await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(heard)); File.Move(temp, path, true);
    }
}
