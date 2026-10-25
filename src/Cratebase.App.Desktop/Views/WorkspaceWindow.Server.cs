using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Threading;
using Cratebase.Server;

namespace Cratebase.App.Desktop.Views;

public sealed partial class WorkspaceWindow
{
    private readonly LibraryServer? _server;
    private ServerSettings _serverSettings=new();
    private CancellationTokenSource? _remoteRefresh;
    private async Task InitializeServerAsync()
    {
        _serverSettings=JsonSerializer.Deserialize<ServerSettings>(await _state.ReadAsync("mobile-server-v1")??"{}")??new();
        _serverSettings.CertificatePassword=Environment.GetEnvironmentVariable("CRATEBASE_TLS_PASSWORD")??"";
        if(_serverSettings.Enabled&&_server!=null)await _server.StartAsync(_serverSettings);
    }
    private void OnRemoteLibraryChanged(RemoteChange change)
    {
        Dispatcher.UIThread.Post(()=>
        {
            _remoteRefresh?.Cancel();var request=_remoteRefresh=new();
            _=Safe(async ()=>
            {
                try
                {
                    await Task.Delay(300,request.Token);
                    if(_closing)return;
                    // Queue objects survive list navigation; refresh the loaded track's flags explicitly.
                    if(_player.CurrentTrack is {} loaded&&await _tracks.GetByIdAsync(loaded.Id) is {} updated)
                    {loaded.ClubReady=updated.ClubReady;loaded.Extended=updated.Extended;loaded.Intro=updated.Intro;loaded.Trash=updated.Trash;}
                    await ReloadBrowsers();await RefreshLists();await RefreshClub();Status("Änderungen vom Handy übernommen.");
                }
                catch(OperationCanceledException){}
                finally {if(ReferenceEquals(_remoteRefresh,request))_remoteRefresh=null;request.Dispose();}
            });
        });
    }
    private async Task StopServerAsync()
    {
        _remoteRefresh?.Cancel();
        if(_server!=null){_server.Changed-=OnRemoteLibraryChanged;await Safe(async()=>await _server.DisposeAsync());}
    }
    private async Task ServerDialogAsync()
    {
        if(_server==null){Status("Server ist in dieser Sitzung nicht verfügbar.");return;}
        var dialog=Dialog("Cratebase · Android-Server",680,650);
        var panel=new StackPanel {Margin=new Thickness(22),Spacing=10};
        var status=new TextBlock{Text=_server.Address,Foreground=Accent};panel.Children.Add(status);
        panel.Children.Add(new TextBlock{Text="Android-App: Server-Adresse und Zugriffsschlüssel eintragen. Für unterwegs HTTPS über ein PFX-Zertifikat oder einen Reverse Proxy verwenden. Bei Reverse Proxy bleibt der Server auf localhost.",TextWrapping=TextWrapping.Wrap});
        var lan=new CheckBox{Content="Im LAN / auf allen IPv4- und IPv6-Adressen lauschen",IsChecked=_serverSettings.AllowLan};panel.Children.Add(lan);
        var auto=new CheckBox{Content="Beim Start von Cratebase aktivieren",IsChecked=_serverSettings.Enabled};panel.Children.Add(auto);
        var port=new NumericUpDown{Minimum=1024,Maximum=65535,Value=_serverSettings.Port};panel.Children.Add(Label("PORT"));panel.Children.Add(port);
        var key=new TextBox{Text=_serverSettings.AccessKey,PasswordChar='●'};panel.Children.Add(Label("ZUGRIFFSSCHLÜSSEL"));panel.Children.Add(key);
        var keyActions=Row();keyActions.Children.Add(Button("Anzeigen / Verbergen",()=>{key.PasswordChar=key.PasswordChar=='\0'?'●':'\0';return Task.CompletedTask;}));
        keyActions.Children.Add(Button("Kopieren",async ()=>{if(Clipboard!=null)await Clipboard.SetTextAsync(key.Text??"");}));
        keyActions.Children.Add(Button("Neuer Schlüssel",()=>{key.Text=new ServerSettings().AccessKey;return Task.CompletedTask;}));panel.Children.Add(keyActions);
        var ffmpeg=new TextBox{Text=_serverSettings.FfmpegPath};panel.Children.Add(Label("FFMPEG MIT LIBMP3LAME (PFAD ODER FFMPEG)"));panel.Children.Add(ffmpeg);
        var cache=new NumericUpDown{Minimum=64,Maximum=65536,Value=_serverSettings.CacheMegabytes};panel.Children.Add(Label("STREAMING-CACHE IN MB · 96 / 160 / 256 / 320 KBIT/S"));panel.Children.Add(cache);
        var pfx=new TextBox{Text=_serverSettings.CertificatePath,PlaceholderText="Optional: vollständiger Pfad zur PFX-Datei"};panel.Children.Add(pfx);
        var password=new TextBox{PasswordChar='●',Text=_serverSettings.CertificatePassword,PlaceholderText="PFX-Passwort (nur diese Sitzung; alternativ CRATEBASE_TLS_PASSWORD)"};panel.Children.Add(password);
        panel.Children.Add(new TextBlock{Text="LAN ohne Zertifikat ist unverschlüsselt: nur im eigenen Netz verwenden. Portfreigabe für unterwegs ausschließlich vor HTTPS. Cache liegt unter Cratebase, Originale bleiben unverändert.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.Orange,FontSize=12});
        var buttons=Row();
        buttons.Children.Add(Button("Speichern & starten",async ()=>
        {
            try
            {
                await _server.StopAsync();
                var next=new ServerSettings{Enabled=auto.IsChecked==true,AllowLan=lan.IsChecked==true,Port=(int)(port.Value??5874),AccessKey=key.Text??"",FfmpegPath=ffmpeg.Text??"ffmpeg",CacheMegabytes=(int)(cache.Value??2048),CertificatePath=pfx.Text??"",CertificatePassword=password.Text??""};
                await _server.StartAsync(next);_serverSettings=next;await _state.WriteAsync("mobile-server-v1",JsonSerializer.Serialize(next));status.Text=_server.Address;
            }
            catch(Exception ex){status.Text="Start fehlgeschlagen: "+ex.Message;}
        }));
        buttons.Children.Add(Button("Server deaktivieren",async ()=>{await _server.StopAsync();_serverSettings.Enabled=false;auto.IsChecked=false;await _state.WriteAsync("mobile-server-v1",JsonSerializer.Serialize(_serverSettings));status.Text="Ausgeschaltet";}));
        buttons.Children.Add(Button("Schließen",()=>{dialog.Close();return Task.CompletedTask;}));panel.Children.Add(buttons);
        dialog.Content=new ScrollViewer{Content=panel};await dialog.ShowDialog(this);
    }
}
