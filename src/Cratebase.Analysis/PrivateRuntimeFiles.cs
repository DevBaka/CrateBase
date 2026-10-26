namespace Cratebase.Analysis;
internal static class PrivateRuntimeFiles
{
    public static void DeleteChildDirectory(string parent,string child)
    {
        var root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent))+Path.DirectorySeparatorChar;
        var target=Path.GetFullPath(child);
        if(!target.StartsWith(root,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))throw new IOException("Aufräumpfad liegt außerhalb des privaten Laufzeitordners.");
        if(Directory.Exists(target))Directory.Delete(target,true);
    }
}
