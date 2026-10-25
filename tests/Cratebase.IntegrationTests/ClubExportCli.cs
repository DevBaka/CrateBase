using Cratebase.Data;
using Cratebase.Data.Repositories;
using Cratebase.Import;

internal static class ClubExportCli
{
    public static async Task RunAsync(string database,string target,bool export)
    {
        if(!File.Exists(database))throw new FileNotFoundException("Existing library required",database);
        var factory=new SqliteConnectionFactory(database);var tracks=new TrackRepository(factory);var roots=new LibraryRootRepository(factory);var lists=new PlaylistRepository(factory);var query=new LibraryQuery(factory);var state=new WorkspaceStateStore(factory);
        var sync=new CollectionSync(tracks,roots,lists,state,new WorkspaceCheckpoint(factory));var exporter=new ClubMediaExporter(roots,query,sync);
        var plan=await exporter.PlanAsync();
        Console.WriteLine($"Club export: {plan.Items.Count} tracks, {plan.Bytes:N0} bytes, {plan.Missing} missing. Target: {target}");
        foreach(var group in plan.Groups)Console.WriteLine($"{group.Count} · {group.Genre} -> {group.Folder} · {group.Warning}");
        if(!export)return;
        var result=await exporter.ExportAsync(plan,target,progress:new Progress<ClubExportProgress>(p=> {if(p.Completed==p.Total)Console.WriteLine(p.Message);}));
        Console.WriteLine($"Export complete: {result.Copied} copied, {result.Reused} reused, {result.Skipped} skipped. NML: {result.NmlPath}");
    }
}
