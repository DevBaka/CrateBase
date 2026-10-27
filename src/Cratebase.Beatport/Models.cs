using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
namespace Cratebase.Beatport;
public sealed record NamedEntity(long Id, string Name);
public sealed record Track
{
    public long Id { get; init; }
    public string Name { get; init; } = "";
    [JsonPropertyName("mix_name")] public string Mix { get; init; } = "Original Mix";
    public NamedEntity[] Artists { get; init; } = [];
    public string? Isrc { get; init; }
    [JsonPropertyName("length_ms")] public long? LengthMs { get; init; }
    public int? Bpm { get; init; }
    [JsonPropertyName("sub_genre")] public NamedEntity? Subgenre { get; init; }
    [JsonPropertyName("is_available_for_streaming")] public bool? Streamable { get; init; }
    [JsonPropertyName("sample_url")] public string? SampleUrl { get; init; }
    public string ArtistText => string.Join(", ", Artists.Select(a => a.Name));
    public string Display => $"{ArtistText} — {Name} ({Mix})";
}
public sealed record PlaylistItem(long Id, Track Track);
public sealed record DuplicatePlan(IReadOnlyList<Track> Keep, IReadOnlyList<Track> Duplicates);
public static class Deduplication
{
    public static string Normalize(string? s) => Regex.Replace((s ?? "").Normalize().ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();
    public static string Key(Track t) => $"{string.Join("|", t.Artists.Select(a => Normalize(a.Name)).Order())}::{Normalize(t.Name)}::{Normalize(t.Mix)}";
    public static bool Same(Track a, Track b)
    {
        if (a.Id == b.Id) return true;
        if (Normalize(a.Mix) != Normalize(b.Mix)) return false;
        if (!string.IsNullOrWhiteSpace(a.Isrc) && a.Isrc == b.Isrc) return true;
        return a.Artists.Length > 0 && b.Artists.Length > 0 && !string.IsNullOrWhiteSpace(a.Name) && Key(a) == Key(b)
            && a.LengthMs is > 0 && b.LengthMs is > 0 && Math.Abs(a.LengthMs.Value - b.LengthMs.Value) <= 2000;
    }
    public static DuplicatePlan Plan(IEnumerable<Track> source, IEnumerable<Track>? existing = null)
    {
        var buckets = new Dictionary<string, List<Track>>();
        IEnumerable<string> Keys(Track t) => new[] { $"id:{t.Id}", Key(t), string.IsNullOrWhiteSpace(t.Isrc) ? "" : $"isrc:{t.Isrc}" }.Where(k => k.Length > 0);
        void Add(Track t) { foreach (var k in Keys(t)) { if (!buckets.TryGetValue(k, out var list)) buckets[k] = list = []; list.Add(t); } }
        foreach (var t in existing ?? []) Add(t);
        var keep = new List<Track>(); var duplicates = new List<Track>();
        foreach (var t in source) { if (Keys(t).SelectMany(k => buckets.GetValueOrDefault(k) ?? []).Any(x => Same(x, t))) duplicates.Add(t); else { keep.Add(t); Add(t); } }
        return new(keep, duplicates);
    }
}
