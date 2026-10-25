using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cratebase.Analysis;
using Cratebase.App.Desktop.ViewModels;
using Cratebase.Core.Abstractions;
using Cratebase.Core.Models;
using Cratebase.Data;
using Cratebase.Data.Repositories;
using Cratebase.Import;

internal static class WaveformChecks
{
    private static void Check(bool condition,string message)
    { if(!condition) throw new Exception("FAIL: "+message); Console.WriteLine("PASS: "+message); }

    public static async Task RunAsync()
    {
        var directory=Path.Combine(Path.GetTempPath(),"Cratebase-waveforms-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var audio=Path.Combine(directory,"tone.wav"); WriteTone(audio,3);
        var audioHash=SHA256.HashData(await File.ReadAllBytesAsync(audio));
        var generator=new FfmpegWaveformGenerator();
        var generated=await generator.GenerateAsync(audio);
        Check(generated?.HasData==true&&generated.Columns.Any(c=>c.Low>0)&&generated.Columns.Any(c=>c.Mid>0)&&generated.Columns.Any(c=>c.High>0),"Installed FFmpeg generates real RGB peaks using supported s16le output");
        Check(generated!.Columns.All(c=>float.IsFinite(c.Low)&&float.IsFinite(c.Mid)&&float.IsFinite(c.High)&&c.Low is >=0 and <=1&&c.Mid is >=0 and <=1&&c.High is >=0 and <=1),"Generated RGB values are finite and normalized");
        var audioAfter=await File.ReadAllBytesAsync(audio);
        Check(audioHash.SequenceEqual(SHA256.HashData(audioAfter)),"Waveform generation never changes the source audio");
        var cachePath=Path.Combine(directory,"CratebaseCache");var cache=new FileWaveformCache(cachePath);
        var info=new FileInfo(audio); var track=new Track { Id=1,LibraryRootId=1,RelativePath="tone.wav",FileSizeBytes=info.Length,FileModifiedAtUtc=info.LastWriteTimeUtc };
        await cache.SaveAsync(track,generated);
        Check((await cache.TryLoadAsync(track))?.Columns.SequenceEqual(generated.Columns)==true,"RGB waveform round-trips through the Cratebase cache");
        var revised=new Track { Id=track.Id,LibraryRootId=1,RelativePath=track.RelativePath,FileSizeBytes=track.FileSizeBytes+1,FileModifiedAtUtc=track.FileModifiedAtUtc };
        Check(await cache.TryLoadAsync(revised)==null,"Changed audio fingerprint invalidates cached waveform");
        using(var canceled=new CancellationTokenSource())
        {
            canceled.Cancel();
            try { await cache.SaveAsync(track,generated,canceled.Token); throw new Exception("Cancellation ignored"); } catch(OperationCanceledException) { }
            Check((await cache.TryLoadAsync(track))?.HasData==true&&!Directory.GetFiles(cachePath,"*.tmp").Any(),"Canceled cache write preserves previous waveform and leaves no partial file");
            try { await generator.GenerateAsync(audio,canceled.Token); throw new Exception("Cancellation ignored"); } catch(OperationCanceledException) { Console.WriteLine("PASS: Waveform generation honors cancellation"); }
        }
        var cacheFile=Directory.GetFiles(cachePath,"*.cwf").Single();
        await File.WriteAllBytesAsync(cacheFile,[0x32,0x46,0x57,0x43,0xff,0xff,0xff,0x7f]);
        Check(await cache.TryLoadAsync(track)==null,"Corrupt cache counts are rejected before allocating memory");
        byte[] legacy;
        using(var buffer=new MemoryStream())
        {
            using(var writer=new BinaryWriter(buffer,Encoding.UTF8,true)) { writer.Write((byte)6);writer.Write("PRTS"u8);writer.Write(2);writer.Write(3.5f);writer.Write(2);writer.Write(2);writer.Write(4);writer.Write(new byte[] {1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16}); }
            legacy=buffer.ToArray();
        }
        var decoded=TraktorStripeWaveformSource.Decode(legacy);
        Check(decoded?.Raster is { Width:2,Height:2 } raster&&raster.Rgba.SequenceEqual(new byte[] {1,2,3,4,9,10,11,12,5,6,7,8,13,14,15,16}),"Legacy Traktor PRTS column-major texture is decoded without inventing a waveform");
        var modern=Encoding.ASCII.GetBytes("22 serialization::archive 20 0 1 0 0 5 0 1 1 22529 0 0 0 0 0 0 2 1 0 1 16384 8192 4096 20000 8192 16384 32768 40000");
        var spectrum=TraktorStripeWaveformSource.Decode(modern);
        Check(spectrum?.Columns.Count==2&&spectrum.Columns[0]==new WaveformColumn(.5f,.25f,.125f),"Traktor spectrum stripe archive decodes RGB band peaks");
        var optionalSpectrum=Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(modern).Replace("22529 0 0 0 0 0 0 2","22529 1 25008.5 1 15075.5 0 0 0 0 2"));
        Check(TraktorStripeWaveformSource.Decode(optionalSpectrum)?.Columns.SequenceEqual(spectrum!.Columns)==true,"Spectrum stripes with optional Traktor analysis fields use the correct payload offset");
        Check(TraktorStripeWaveformSource.Decode(legacy[..^1])==null&&TraktorStripeWaveformSource.Decode(modern[..^6])==null,"Truncated Traktor stripes are rejected for RGB fallback");
        Check(TraktorStripeName.FromAudioId("not-base64")==null&&TraktorStripeName.FromAudioId(Convert.ToBase64String(new byte[8]))==null,"Invalid Traktor Audio IDs cannot produce arbitrary file paths");

        var factory=new SqliteConnectionFactory(Path.Combine(directory,"state.db"));DatabaseInitializer.EnsureCreated(factory);
        var roots=new LibraryRootRepository(factory); await roots.AddOrGetAsync(directory,"Test");
        var audioPlayer=new FakeAudio(); var stripes=new FakeStripes { Value=decoded };
        var countedGenerator=new CountingGenerator { Value=generated };
        var player=new WorkspacePlayer(audioPlayer,roots,countedGenerator,new FileWaveformCache(Path.Combine(directory,"PlayerCache")),stripes);
        await player.PlayAsync(track,[track]);
        Check(player.Waveform?.Origin==WaveformOrigin.TraktorStripe&&countedGenerator.Calls==0,"Player prefers existing Traktor stripe without regenerating");
        stripes.Value=null; await player.PlayAsync(track,[track]);
        Check(player.Waveform?.Origin==WaveformOrigin.GeneratedRgb&&countedGenerator.Calls==1,"Missing stripe automatically generates RGB waveform during playback");
        await player.PlayAsync(track,[track]);
        Check(countedGenerator.Calls==1&&player.WaveformStatus.Contains("Cache"),"Subsequent playback reuses generated Cratebase waveform");
        player.Dispose();
        var delayed=new DelayedGenerator { FirstValue=generated,SecondValue=new WaveformData { Columns=[new(0,1,0)] } };
        using var switchingPlayer=new WorkspacePlayer(new FakeAudio(),roots,delayed,new FileWaveformCache(Path.Combine(directory,"SwitchCache")),new FakeStripes());
        var firstLoad=switchingPlayer.PlayAsync(track,[track]); await delayed.Started.Task;
        var secondTrack=new Track { Id=2,LibraryRootId=track.LibraryRootId,RelativePath=track.RelativePath };
        await switchingPlayer.PlayAsync(secondTrack,[secondTrack]);
        delayed.FinishFirst.TrySetResult();await firstLoad;
        Check(switchingPlayer.CurrentTrack?.Id==2&&ReferenceEquals(switchingPlayer.Waveform,delayed.SecondValue)&&!switchingPlayer.IsWaveformLoading,"Late waveform result cannot overwrite a newer track after cancellation");
        Console.WriteLine("Waveform regression checks passed.");
    }

    public static async Task RealStripesAsync(string nml)
    {
        var directory=Path.Combine(Path.GetTempPath(),"Cratebase-real-stripes-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var factory=new SqliteConnectionFactory(Path.Combine(directory,"test.db"));DatabaseInitializer.EnsureCreated(factory);
        var roots=new LibraryRootRepository(factory);var state=new WorkspaceStateStore(factory);
        await state.WriteAsync("traktor-nml-source",JsonSerializer.Serialize(Path.GetFullPath(nml)));
        var lookup=new TraktorAnalysisLookup(state,roots);var provider=new TraktorStripeWaveformSource(lookup);
        var count=0;var types=new HashSet<string>();
        foreach(var entry in new TraktorNmlReader().ReadCollection(nml))
        {
            if(entry.AudioId==null||TraktorStripeName.FromAudioId(entry.AudioId) is not { } relative) continue;
            var original=Path.Combine(Path.GetDirectoryName(nml)!,"Stripes",relative);
            var files=new[] { original,original+".stripe" }.Where(File.Exists).ToArray(); if(files.Length==0) continue;
            foreach(var file in files)
            {
                var before=await File.ReadAllBytesAsync(file);var data=TraktorStripeWaveformSource.Decode(before);
                Check(data?.HasData==true,"Actual "+(Path.GetExtension(file)==".stripe"?"spectrum":"legacy")+" Traktor stripe decodes: "+Path.GetFileName(file));
                types.Add(data!.Raster==null?"spectrum":"legacy");
                var after=await File.ReadAllBytesAsync(file);
                Check(SHA256.HashData(before).SequenceEqual(SHA256.HashData(after)),"Original Traktor stripe remains byte-identical");
            }
            var root=await roots.AddOrGetAsync(entry.Path.Volume+Path.DirectorySeparatorChar,entry.Path.Volume);
            await roots.SetTraktorMappingAsync(root.Id,entry.Path.Volume,"");
            var track=new Track { Id=++count,LibraryRootId=root.Id,RelativePath=string.Join('/',entry.Path.DirSegments.Append(entry.Path.FileName)) };
            Check((await provider.TryLoadAsync(track))?.HasData==true,"NML Audio ID resolves to its actual Traktor stripe through player lookup");
            if(count>=8&&types.Count==2) break;
        }
        Check(count>0,"Real NML hash lookup matches existing Traktor cache files");
        Console.WriteLine($"Verified {count} actual track stripe lookups; formats: {string.Join(", ",types)}.");
    }

    private static void WriteTone(string path,int seconds)
    {
        const int rate=22050;var count=rate*seconds;
        using var writer=new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8);writer.Write(36+count*2);writer.Write("WAVEfmt "u8);writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(rate);writer.Write(rate*2);writer.Write((short)2);writer.Write((short)16);writer.Write("data"u8);writer.Write(count*2);
        for(var i=0;i<count;i++) { var t=(double)i/rate;var sample=.2*(Math.Sin(2*Math.PI*80*t)+Math.Sin(2*Math.PI*1000*t)+Math.Sin(2*Math.PI*4500*t)); writer.Write((short)(sample*short.MaxValue)); }
    }
    private sealed class FakeStripes : ITrackWaveformSource
    { public WaveformData? Value;public Task<WaveformData?> TryLoadAsync(Track track,CancellationToken ct=default)=>Task.FromResult(Value); }
    private sealed class CountingGenerator : IWaveformGenerator
    { public WaveformData? Value; public int Calls;public Task<WaveformData?> GenerateAsync(string path,CancellationToken ct=default) { Calls++;return Task.FromResult(Value); } }
    private sealed class DelayedGenerator : IWaveformGenerator
    {
        public WaveformData? FirstValue,SecondValue;private int _calls;
        public TaskCompletionSource Started { get; }=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FinishFirst { get; }=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<WaveformData?> GenerateAsync(string path,CancellationToken ct=default)
        { if(Interlocked.Increment(ref _calls)!=1) return SecondValue; Started.TrySetResult();await FinishFirst.Task;return FirstValue; }
    }
    private sealed class FakeAudio : IAudioPlayer
    {
        public PlaybackState State { get; private set; } public TimeSpan Position=>TimeSpan.Zero;public TimeSpan Duration=>TimeSpan.FromSeconds(3);public float Volume { get;set; }
        public event EventHandler? PlaybackEnded { add { } remove { } } public event EventHandler? PositionChanged { add { } remove { } }
        public void Load(string path) { } public void Play()=>State=PlaybackState.Playing;public void Pause()=>State=PlaybackState.Paused;public void Stop()=>State=PlaybackState.Stopped;public void Seek(TimeSpan position) { }public void Dispose() { }
    }
}
