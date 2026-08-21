using System.Globalization;

namespace Cratebase.Data;

/// <summary>
/// SQLite kennt keinen nativen Datumstyp. Damit alle Repositories dasselbe TEXT-Format
/// schreiben (wichtig für spätere Datum-Range-Vergleiche in Smart-Playlist-Queries, wo
/// String-Sortierung auf dem Format beruht), läuft jede Konvertierung explizit hier durch,
/// statt sich auf das Default-Verhalten des Sqlite-Providers zu verlassen. Alle Zeiten
/// werden als UTC ohne Offset-Suffix gespeichert – die Konvention "...Utc" im Modell ist bindend.
/// </summary>
internal static class SqliteDateTime
{
    private const string Format = "yyyy-MM-ddTHH:mm:ss.fffffff";

    public static string ToText(DateTime value) =>
        value.ToUniversalTime().ToString(Format, CultureInfo.InvariantCulture);

    public static string? ToText(DateTime? value) => value.HasValue ? ToText(value.Value) : null;
}
