namespace Cratebase.Core.Models;

/// <summary>
/// Ein vom Nutzer in den Einstellungen angelegter Ordner-Shortcut (z.B. "M:/Musik" oder
/// "/home/djbaka/Music"), der im Sidebar-Baum direkt neben den Playlisten auftaucht und dort
/// ausklappbar ist - unabhängig davon, ob der Ordner schon als LibraryRoot gescannt wurde
/// (das Browsen/Ausklappen selbst braucht keine DB, nur "Diesen Ordner scannen" später).
/// </summary>
public sealed record QuickAccessFolder(string Name, string Path);
