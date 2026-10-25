using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using Cratebase.Core.Models;
using Cratebase.Data;
using Cratebase.Data.Repositories;
using Cratebase.Scanning;
using Cratebase.Server;
using Cratebase.Tagging;

internal static class ServerChecks
{
    public static async Task ServeDeviceFixtureAsync()
    {
        var directory=Path.Combine(Path.GetTempPath(),"Cratebase-phone-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var music=Path.Combine(directory,"Techno");Directory.CreateDirectory(music);
        var factory=new SqliteConnectionFactory(Path.Combine(directory,"library.db"));DatabaseInitializer.EnsureCreated(factory);
        var tracks=new TrackRepository(factory);var roots=new LibraryRootRepository(factory);var playlists=new PlaylistRepository(factory);var query=new LibraryQuery(factory);
        var root=await roots.AddOrGetAsync(directory,"USB-Testbibliothek");
        var group=await playlists.CreateAsync("Techno",PlaylistNodeType.Folder,null);var playlist=await playlists.CreateAsync("Hard Techno",PlaylistNodeType.Playlist,group.Id);
        var ids=new List<long>();for(var i=0;i<3;i++)
        {
            var path=Path.Combine(music,$"Testton-{i+1}.wav");WriteTone(path);
            ids.Add(await tracks.UpsertAsync(new(){LibraryRootId=root.Id,RelativePath=$"Techno/Testton-{i+1}.wav",Title=$"USB-Testton {i+1}",Artist="Cratebase · Testbibliothek",DurationMs=2000,GenreRaw="Hard Techno",Format="wav",FileSizeBytes=new FileInfo(path).Length}));
        }
        await playlists.AddTracksAsync(playlist.Id,ids.ToArray());
        var store=new JsonFavoriteProfileStore(Path.Combine(directory,"favorites.json"));await store.SaveAsync(new(){ActiveProfileName="USB-Test",Profiles=[new(){Name="USB-Test",Targets=new(){[0]=new(LibraryTargetKind.Playlist,"Hard Techno",playlist.Id)}}]});
        await using var server=new LibraryServer(tracks,roots,playlists,query,new TargetActions(tracks,roots,playlists,new TagLibTagReader(),query),store,Path.Combine(directory,"cache"),new LibraryRevision(factory));
        await server.StartAsync(new(){Port=5875,AccessKey="cratebase-usb-test-only-not-a-real-library-key"});
        Console.WriteLine("Isolated USB fixture running at http://127.0.0.1:5875; "+directory);
        await Task.Delay(Timeout.InfiniteTimeSpan);
    }

    public static async Task RunAsync()
    {
        var directory=Path.Combine(Path.GetTempPath(),"Cratebase-server-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var music=Path.Combine(directory,"music");Directory.CreateDirectory(Path.Combine(music,"Techno"));
        var factory=new SqliteConnectionFactory(Path.Combine(directory,"library.db"));DatabaseInitializer.EnsureCreated(factory);
        var tracks=new TrackRepository(factory);var roots=new LibraryRootRepository(factory);var playlists=new PlaylistRepository(factory);var query=new LibraryQuery(factory);
        var favorites=new JsonFavoriteProfileStore(Path.Combine(directory,"favorites.json"));var root=await roots.AddOrGetAsync(music,"Musik");
        var node=await playlists.CreateAsync("Hard Techno",PlaylistNodeType.Playlist,null);
        var tone=Path.Combine(music,"Techno","tone.wav");WriteTone(tone);var before=SHA256.HashData(await File.ReadAllBytesAsync(tone));
        var drive=Path.GetPathRoot(tone)!;
        Check(LibraryServer.ContainedPath(drive,Path.GetRelativePath(drive,tone))==tone,"Drive-root paths resolve without walking past the volume root");
        Check(LibraryServer.ContainedPath(drive,"")==Path.GetFullPath(drive),"Browsing the drive root itself is supported");
        var track=new Track{LibraryRootId=root.Id,RelativePath="Techno/tone.wav",Title="Test",Artist="Cratebase",GenreRaw="Hard Techno",DurationMs=2000,Format="wav",FileSizeBytes=new FileInfo(tone).Length};track.Id=await tracks.UpsertAsync(track);
        var absent=new Track{LibraryRootId=root.Id,RelativePath="absent.flac",Title="Missing"};absent.Id=await tracks.UpsertAsync(absent);
        using(var old=factory.CreateConnection()){using var cmd=old.CreateCommand();cmd.CommandText="ALTER TABLE track DROP COLUMN trash; PRAGMA user_version=6;";cmd.ExecuteNonQuery();}
        DatabaseInitializer.EnsureCreated(factory);DatabaseInitializer.EnsureCreated(factory);
        Check(!(await tracks.GetByIdAsync(track.Id))!.Trash&&(await query.TotalsAsync(new(new(LibraryTargetKind.Collection,"All")))).Count==2,"Trash migration is additive and repeatable; existing tracks default to unmarked");
        await favorites.SaveAsync(new(){Profiles=[new(){Name="Test",Targets=new(){[0]=new(LibraryTargetKind.Playlist,node.Name,node.Id),[1]=new(LibraryTargetKind.Directory,"Techno",Path:Path.Combine(music,"Techno"))}}]});
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
        var settings=new ServerSettings{Port=port,FfmpegPath=Environment.GetEnvironmentVariable("CRATEBASE_TEST_FFMPEG")??"ffmpeg"};
        var remotePlayer=new RemotePlayer();
        await using var server=new LibraryServer(tracks,roots,playlists,query,new TargetActions(tracks,roots,playlists,new TagLibTagReader(),query),favorites,Path.Combine(directory,"cache"),new LibraryRevision(factory),remotePlayer);
        var events=0;server.Changed+=_=>events++;
        await server.StartAsync(settings);
        using var client=new HttpClient{BaseAddress=new Uri($"http://127.0.0.1:{port}"),Timeout=TimeSpan.FromSeconds(60)};
        Check((await client.GetAsync("/api/status")).StatusCode==HttpStatusCode.Unauthorized,"Unauthenticated API is rejected");
        client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",settings.AccessKey);
        (await client.PostAsJsonAsync("/api/player/load",new Cratebase.Core.Abstractions.RemotePlaybackLoad([track.Id],PositionMs:1200))).EnsureSuccessStatusCode();
        var playback=await client.GetFromJsonAsync<JsonElement>("/api/player");
        Check(playback.GetProperty("track").GetProperty("id").GetInt64()==track.Id&&playback.GetProperty("positionMs").GetInt64()==1200,"Authenticated desktop playback API loads queue and reports current metadata");
        Check(playback.GetProperty("queue").ValueKind==JsonValueKind.Null,"Frequent playback polling does not resend the full queue");
        var handoff=await client.GetFromJsonAsync<JsonElement>("/api/player?includeQueue=true");Check(handoff.GetProperty("queue").GetArrayLength()==1,"A device handoff can explicitly request queue metadata");
        (await client.PostAsJsonAsync("/api/player/command",new Cratebase.Core.Abstractions.RemotePlaybackCommand("pause"))).EnsureSuccessStatusCode();
        Check(!remotePlayer.State.Playing,"Desktop commands are forwarded to the playback bridge");
        Check((await client.PostAsJsonAsync("/api/player/load",new Cratebase.Core.Abstractions.RemotePlaybackLoad([track.Id],Index:9))).StatusCode==HttpStatusCode.BadRequest,"Invalid remote queue indexes are rejected");
        Check((await client.PostAsJsonAsync("/api/player/command",new Cratebase.Core.Abstractions.RemotePlaybackCommand("volume",Volume:101))).StatusCode==HttpStatusCode.BadRequest,"Invalid remote volume is rejected");
        var status=await client.GetFromJsonAsync<JsonElement>("/api/status");var initialRevision=status.GetProperty("revision").GetInt64();
        Check(status.GetProperty("qualities").EnumerateArray().Select(x=>x.GetInt32()).SequenceEqual(new[]{96,160,256,320}),"Only compressed bitrate profiles are advertised");
        var response=await client.PostAsJsonAsync("/api/tracks/query",new TrackQuery(new(LibraryTargetKind.Collection,"Collection"),Limit:1));response.EnsureSuccessStatusCode();
        var page=await response.Content.ReadFromJsonAsync<JsonElement>();Check(page.GetProperty("tracks").GetArrayLength()==1&&page.GetProperty("totals").GetProperty("count").GetInt32()==2,"Queries paginate while reporting full totals");
        response=await client.PostAsJsonAsync("/api/tracks/query",new TrackQuery(new(LibraryTargetKind.Directory,"Escape",root.Id,"../")));
        Check(response.StatusCode==HttpStatusCode.BadRequest,"Traversal outside the shared root is rejected");
        response=await client.GetAsync($"/api/folders?rootId={root.Id}&path=");response.EnsureSuccessStatusCode();Check((await response.Content.ReadAsStringAsync()).Contains("Techno"),"Folder hierarchy is browsable");
        foreach(var flag in new[]{"club","extended","intro","trash"})
        {
            (await client.PutAsJsonAsync("/api/flags",new FlagChange([track.Id],flag,true))).EnsureSuccessStatusCode();
            (await client.PutAsJsonAsync("/api/flags",new FlagChange([track.Id],flag,true))).EnsureSuccessStatusCode();
        }
        var saved=await tracks.GetByIdAsync(track.Id);Check(saved!.ClubReady&&saved.Extended&&saved.Intro&&saved.Trash&&events==8,"Phone flags including Trash persist in desktop repositories; retrying a set is idempotent");
        response=await client.PatchAsJsonAsync($"/api/tracks/{track.Id}",new MetadataChange(new(){["genre"]="Techno",["rating"]="4"}));response.EnsureSuccessStatusCode();
        saved=await tracks.GetByIdAsync(track.Id);Check(saved!.GenreRaw=="Techno"&&saved.Rating==4&&saved.ClubReady,"Metadata patches preserve unrelated flags");
        // A stale editor must not overwrite flags changed since it loaded.
        var stale=await tracks.GetByIdAsync(track.Id);await query.SetFlagAsync([track.Id],"intro",false);stale!.Title="Patched";
        await tracks.UpdateMetadataAsync(stale,["title"]);Check(!(await tracks.GetByIdAsync(track.Id))!.Intro,"Narrow metadata updates preserve concurrent flag changes");
        status=await client.GetFromJsonAsync<JsonElement>("/api/status");Check(status.GetProperty("revision").GetInt64()!=initialRevision,"Changes from desktop connections update the remote revision");
        for(var i=0;i<2;i++)(await client.PostAsJsonAsync("/api/favorites/Test/0",new AddTracks([track.Id]))).EnsureSuccessStatusCode();
        Check((await query.QueryAsync(new(new(LibraryTargetKind.Playlist,"Test",node.Id)))).Count==1,"Favorite slots use shared target actions without duplicate memberships");
        (await client.PostAsJsonAsync("/api/favorites/Test/1",new AddTracks([track.Id]))).EnsureSuccessStatusCode();
        Check(Directory.GetFiles(Path.Combine(music,"Techno")).Length==2,"Folder favorites safely create copies through the desktop transfer service");
        Check((await query.QueryAsync(new(new(LibraryTargetKind.Collection,"Trash"),Filters:[new("trash",MetadataOperator.Equals,"ja")]))).Count==2,"File copies preserve Trash without altering the source mark");
        response=await client.GetAsync($"/api/stream/{absent.Id}?bitrate=160");Check(response.StatusCode==HttpStatusCode.NotFound&&(await tracks.GetByIdAsync(absent.Id))!.MissingSinceUtc!=null,"Missing streaming sources are marked in the shared library");
        response=await client.GetAsync($"/api/stream/{track.Id}?bitrate=999");Check(response.StatusCode==HttpStatusCode.BadRequest,"Invalid stream qualities cannot request an original FLAC fallback");
        foreach(var bitrate in StreamCache.Bitrates)
        {
            response=await client.GetAsync($"/api/stream/{track.Id}?bitrate={bitrate}");
            if(!response.IsSuccessStatusCode)throw new Exception("Stream failed: "+await response.Content.ReadAsStringAsync());
            var bytes=await response.Content.ReadAsByteArrayAsync();Check(response.Content.Headers.ContentType?.MediaType=="audio/mpeg"&&bytes.Length>1000&&bytes.Length<before.Length+200000,"Actual audio transcoding succeeds at "+bitrate+" kbit/s");
        }
        using var range=new HttpRequestMessage(HttpMethod.Get,$"/api/stream/{track.Id}?bitrate=160");range.Headers.Range=new RangeHeaderValue(0,99);
        response=await client.SendAsync(range);Check(response.StatusCode==HttpStatusCode.PartialContent&&(await response.Content.ReadAsByteArrayAsync()).Length==100,"Cached streams support byte ranges for seeking");
        var sourceAfter=await File.ReadAllBytesAsync(tone);
        Check(before.SequenceEqual(SHA256.HashData(sourceAfter)),"All quality conversions leave the source audio untouched");
        Check(!Directory.GetFiles(Path.Combine(directory,"cache"),"*.part").Any(),"No unfinished transcodes remain in the cache");
        await server.StopAsync();Check(!server.IsRunning,"Server disables cleanly");settings.AllowLan=true;await server.StartAsync(settings);Check((await client.GetAsync("/api/status")).IsSuccessStatusCode,"Dual-stack listener accepts IPv4 after restart on the same port");
        using var ipv6=new HttpClient{BaseAddress=new Uri($"http://[::1]:{port}")};ipv6.DefaultRequestHeaders.Authorization=client.DefaultRequestHeaders.Authorization;
        Check((await ipv6.GetAsync("/api/status")).IsSuccessStatusCode,"Dual-stack listener also accepts IPv6");
        await server.StopAsync();settings.AllowLan=false;
        using var rsa=RSA.Create(2048);
        var certificateRequest=new System.Security.Cryptography.X509Certificates.CertificateRequest("CN=localhost",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        using var certificate=certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1),DateTimeOffset.UtcNow.AddHours(1));
        settings.CertificatePath=Path.Combine(directory,"test.pfx");settings.CertificatePassword="isolated-fixture";
        await File.WriteAllBytesAsync(settings.CertificatePath,certificate.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pfx,settings.CertificatePassword));
        await server.StartAsync(settings);
        using var handler=new HttpClientHandler{ServerCertificateCustomValidationCallback=(_,peer,_,_)=>peer?.Thumbprint==certificate.Thumbprint};
        using var secure=new HttpClient(handler){BaseAddress=new Uri($"https://127.0.0.1:{port}")};secure.DefaultRequestHeaders.Authorization=client.DefaultRequestHeaders.Authorization;
        Check((await secure.GetAsync("/api/status")).IsSuccessStatusCode,"Direct HTTPS with a PFX certificate serves the authenticated API");
        Console.WriteLine("Server checks passed. Isolated fixture: "+directory);
    }
    private static void Check(bool value,string description){if(!value)throw new Exception(description);Console.WriteLine("PASS: "+description);}
    private sealed class RemotePlayer:Cratebase.Core.Abstractions.IRemotePlayback
    {
        public Cratebase.Core.Abstractions.RemotePlaybackState State=new(null,false,0,0,80,[],0);
        public Task<Cratebase.Core.Abstractions.RemotePlaybackState> ReadAsync(CancellationToken ct)=>Task.FromResult(State);
        public Task LoadAsync(IReadOnlyList<Track> queue,int index,long position,bool playing,CancellationToken ct){State=new(queue[index].Id,playing,position,queue[index].DurationMs,80,queue.Select(t=>t.Id).ToArray(),index);return Task.CompletedTask;}
        public Task CommandAsync(Cratebase.Core.Abstractions.RemotePlaybackCommand command,CancellationToken ct){State=State with{Playing=command.Action=="play"};return Task.CompletedTask;}
    }
    private static void WriteTone(string path)
    {
        const int samples=44100*2;using var w=new BinaryWriter(File.Create(path));w.Write("RIFF"u8);w.Write(36+samples*2);w.Write("WAVEfmt "u8);w.Write(16);w.Write((short)1);w.Write((short)1);w.Write(44100);w.Write(88200);w.Write((short)2);w.Write((short)16);w.Write("data"u8);w.Write(samples*2);for(var i=0;i<samples;i++)w.Write((short)(12000*Math.Sin(i*2*Math.PI*440/44100)));
    }
}
