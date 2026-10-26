using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Cratebase.Analysis;

namespace Cratebase.App.Desktop.Views;

public sealed partial class SetArtworkWindow
{
    private readonly ComboBox _font=new(){Name="ArtworkFont",HorizontalAlignment=HorizontalAlignment.Stretch};
    private readonly ComboBox _preset=new(){Name="ArtworkPreset",ItemsSource=new[]{"Eigene Gestaltung","Neon Club","Industrial / Techno","Emotional / Atmospheric","Hands Up / Festival","Minimal / Foto","Retro / VHS"},SelectedIndex=0};
    private readonly ComboBox _weight=new(){ItemsSource=new[]{"Normal","SemiBold","Bold","Black"},SelectedIndex=3};
    private readonly CheckBox _italic=new(){Content="Kursiv"};
    private readonly ComboBox _effect=new(){Name="ArtworkEffect",ItemsSource=new[]{"Keine","Schatten","Neon","Kontur","3D"},SelectedIndex=1};
    private readonly ComboBox _frame=new(){Name="ArtworkFrame",ItemsSource=new[]{"Keiner","Linie","Neon","Ecken","Letterbox"},SelectedIndex=0};
    private readonly ComboBox _textBackground=new(){ItemsSource=new[]{"Verlauf","Ohne","Textbox","Band"},SelectedIndex=0};
    private readonly TextBox _textColor=new(){Name="ArtworkTextColor",Text="#FFFFFF",MaxLength=9};
    private readonly Slider _headlineSize=DesignSlider("ArtworkHeadlineSize",30,160,100);
    private readonly Slider _subtitleSize=DesignSlider("ArtworkSubtitleSize",16,75,31);
    private readonly Slider _brandSize=DesignSlider("ArtworkBrandSize",14,60,27);
    private readonly Slider _scale=DesignSlider("ArtworkScale",40,150,100);
    private readonly Slider _spacing=DesignSlider("ArtworkSpacing",0,55,24);
    private readonly Slider _effectSize=DesignSlider("ArtworkEffectSize",0,30,8);
    private readonly Slider _frameSize=DesignSlider("ArtworkFrameSize",1,30,8);
    private readonly Slider _shade=DesignSlider("ArtworkShade",0,100,80);
    private readonly Slider _imageZoom=DesignSlider("ArtworkZoom",100,200,100);
    private readonly Slider _imageX=DesignSlider("ArtworkImageX",0,100,50);
    private readonly Slider _imageY=DesignSlider("ArtworkImageY",0,100,50);
    private readonly Slider _brightness=DesignSlider("ArtworkBrightness",-25,25,0);
    private readonly Slider _contrast=DesignSlider("ArtworkContrast",50,180,100);
    private readonly Slider _saturation=DesignSlider("ArtworkSaturation",0,250,100);
    private readonly Slider _gamma=DesignSlider("ArtworkGamma",60,180,100);
    private readonly TextBox _imagePrompt=new(){Name="ArtworkImagePrompt",AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=80,PlaceholderText="z. B. cinematic nightclub lighting, cyan and violet neon, photographic"};
    private readonly Slider _aiStrength=DesignSlider("ArtworkAiStrength",5,85,25);
    private readonly TextBox _seed=new(){Text="42",PlaceholderText="Seed (-1 = zufällig)"};
    private readonly CheckBox _imageCpu=new(){Content="Bild-KI auf CPU (langsamer)"};
    private string? _displayPath;
    private static Slider DesignSlider(string name,double min,double max,double value)=>new(){Name=name,Minimum=min,Maximum=max,Value=value};
    private void BuildDesignTools(StackPanel editor)
    {
        _font.ItemsSource=FontManager.Current.SystemFonts.Select(f=>f.Name).Prepend("Inter").Distinct().Order().ToArray();_font.SelectedItem="Inter";
        editor.Children.Add(new TextBlock{Text="Stilvorlage"});editor.Children.Add(_preset);
        var type=new StackPanel{Spacing=6};type.Children.Add(new TextBlock{Text="Schriftart"});type.Children.Add(_font);type.Children.Add(_weight);type.Children.Add(_italic);type.Children.Add(new TextBlock{Text="Schriftfarbe (#RRGGBB)"});type.Children.Add(_textColor);
        AddSlider(type,"Headline-Größe",_headlineSize);AddSlider(type,"Untertitel-Größe",_subtitleSize);AddSlider(type,"Kanalname-Größe",_brandSize);AddSlider(type,"Text-Skalierung (%)",_scale);AddSlider(type,"Zeilenabstand / Abstand",_spacing);
        editor.Children.Add(new Expander{Header="Schrift & Skalierung",Content=type,HorizontalAlignment=HorizontalAlignment.Stretch});
        var effects=new StackPanel{Spacing=6};effects.Children.Add(new TextBlock{Text="Texteffekt"});effects.Children.Add(_effect);AddSlider(effects,"Effektstärke",_effectSize);effects.Children.Add(new TextBlock{Text="Rahmen / Schimmer"});effects.Children.Add(_frame);AddSlider(effects,"Rahmenbreite",_frameSize);effects.Children.Add(new TextBlock{Text="Texthintergrund"});effects.Children.Add(_textBackground);AddSlider(effects,"Hintergrund-Abdunklung (%)",_shade);
        editor.Children.Add(new Expander{Header="Effekte, Rahmen & Hintergrund",Content=effects,HorizontalAlignment=HorizontalAlignment.Stretch});
        var image=new StackPanel{Spacing=6};image.Children.Add(Action("Eigenes Bild laden …",PickImageAsync));AddSlider(image,"Bildausschnitt / Zoom (%)",_imageZoom);AddSlider(image,"Bildausschnitt horizontal",_imageX);AddSlider(image,"Bildausschnitt vertikal",_imageY);
        AddSlider(image,"Helligkeit",_brightness);AddSlider(image,"Kontrast (%)",_contrast);AddSlider(image,"Sättigung (%)",_saturation);AddSlider(image,"Gamma (%)",_gamma);image.Children.Add(Action("Bildanpassungen anwenden",AdjustImageAsync));image.Children.Add(Action("Originalframe wiederherstellen",RestoreImageAsync));
        editor.Children.Add(new Expander{Header="Bild bearbeiten & zuschneiden",Content=image,HorizontalAlignment=HorizontalAlignment.Stretch});
        var ai=new StackPanel{Spacing=6};ai.Children.Add(new TextBlock{Text="Echte lokale Bildbearbeitung (SD 1.5). Kleine Stärke erhält mehr vom Frame; hohe Stärke kann Personen und Details verändern. Englisch funktioniert am besten.",TextWrapping=TextWrapping.Wrap});ai.Children.Add(new TextBlock{Text="SD 1.5: CreativeML Open RAIL-M · Laufzeit: stable-diffusion.cpp (MIT). Lizenztexte liegen im privaten Bild-KI-Ordner.",TextWrapping=TextWrapping.Wrap,FontSize=11});ai.Children.Add(_imagePrompt);AddSlider(ai,"Veränderungsstärke (%)",_aiStrength);ai.Children.Add(_seed);ai.Children.Add(_imageCpu);ai.Children.Add(Action("Bild-KI installieren (ca. 2,2 GB)",()=>new LocalArtworkImageAi(Path.Combine(_directory,"artwork-ai")).InstallAsync(new Progress<string>(s=>_status.Text=s),_job!.Token)));ai.Children.Add(Action("KI-Bildvariante generieren",GenerateImageAsync));
        editor.Children.Add(new Expander{Header="Lokale Bild-KI · Frame umgestalten",Content=ai,HorizontalAlignment=HorizontalAlignment.Stretch});
        foreach(var slider in new[]{_headlineSize,_subtitleSize,_brandSize,_scale,_spacing,_effectSize,_frameSize,_shade,_imageZoom,_imageX,_imageY})slider.PropertyChanged+=(_,e)=>{if(e.Property==Slider.ValueProperty)Refresh();};
        foreach(var choice in new[]{_font,_weight,_effect,_frame,_textBackground})choice.SelectionChanged+=(_,_)=>Refresh();_textColor.TextChanged+=(_,_)=>Refresh();_italic.IsCheckedChanged+=(_,_)=>Refresh();_preset.SelectionChanged+=(_,_)=>ApplyPreset();
    }
    private static void AddSlider(StackPanel panel,string label,Slider slider)
    {
        var value=new TextBlock();void Show()=>value.Text=label+": "+slider.Value.ToString("0",CultureInfo.CurrentCulture);Show();slider.PropertyChanged+=(_,e)=>{if(e.Property==Slider.ValueProperty)Show();};panel.Children.Add(value);panel.Children.Add(slider);
    }
    private ArtworkDesign CurrentDesign()=>new(){Font=_font.SelectedItem as string??"Inter",HeadlineSize=_headlineSize.Value,SubtitleSize=_subtitleSize.Value,BrandSize=_brandSize.Value,Scale=_scale.Value/100,Spacing=_spacing.Value,TextColor=_textColor.Text??"#FFFFFF",Weight=_weight.SelectedItem as string??"Black",Italic=_italic.IsChecked==true,Effect=_effect.SelectedItem as string??"Keine",EffectSize=_effectSize.Value,Frame=_frame.SelectedItem as string??"Keiner",FrameSize=_frameSize.Value,Background=_textBackground.SelectedItem as string??"Verlauf",Shade=_shade.Value/100,ImageZoom=_imageZoom.Value/100,ImageX=_imageX.Value/100,ImageY=_imageY.Value/100};
    private void ApplyPreset()
    {
        if(_preset.SelectedIndex==0)return;
        _font.SelectedItem="Inter";_headlineSize.Value=100;_weight.SelectedItem="Black";_italic.IsChecked=false;
        _effectSize.Value=8;_frameSize.Value=8;_shade.Value=80;
        switch(_preset.SelectedIndex)
        {
            case 1:_accent.Text="#B56CFF";_effect.SelectedItem="Neon";_frame.SelectedItem="Neon";_textBackground.SelectedItem="Verlauf";_italic.IsChecked=false;break;
            case 2:_accent.Text="#FF7249";_effect.SelectedItem="Kontur";_frame.SelectedItem="Ecken";_textBackground.SelectedItem="Band";_weight.SelectedItem="Black";break;
            case 3:_accent.Text="#CAB0FF";_effect.SelectedItem="Schatten";_frame.SelectedItem="Letterbox";_textBackground.SelectedItem="Verlauf";_weight.SelectedItem="SemiBold";break;
            case 4:_accent.Text="#FFE65C";_effect.SelectedItem="3D";_frame.SelectedItem="Linie";_textBackground.SelectedItem="Verlauf";_italic.IsChecked=true;break;
            case 5:_accent.Text="#F4F4F4";_effect.SelectedItem="Schatten";_frame.SelectedItem="Keiner";_textBackground.SelectedItem="Ohne";_headlineSize.Value=72;_italic.IsChecked=false;break;
            case 6:_accent.Text="#FF72BE";_effect.SelectedItem="3D";_frame.SelectedItem="Ecken";_textBackground.SelectedItem="Textbox";_font.SelectedItem="Consolas";break;
        }
        Refresh();
    }
    private async Task PickImageAsync()
    {
        var files=await StorageProvider.OpenFilePickerAsync(new(){Title="Eigenes Thumbnail-Bild",AllowMultiple=false,FileTypeFilter=[new("Bild"){Patterns=["*.png","*.jpg","*.jpeg","*.webp"]}]});
        if(files.FirstOrDefault()?.TryGetLocalPath() is {} path){var frame=new SetVideoFrame(path,0,0,path);AddFrame(frame);SelectFrame(frame);}
    }
    private async Task AdjustImageAsync()
    {
        if(_selected==null)throw new IOException("Zuerst ein Frame oder Bild auswählen.");
        var output=await _frames.AdjustImageAsync(_selected.Path,Path.Combine(_directory,"artwork","adjustments"),_brightness.Value/100,_contrast.Value/100,_saturation.Value/100,_gamma.Value/100,_job!.Token);
        SetDisplayImage(output);_status.Text="Bild angepasst. Erneutes Anwenden startet vom gewählten Frame, ohne die Korrekturen aufzusummieren.";
    }
    private Task RestoreImageAsync()
    {
        if(_selected==null)return Task.CompletedTask;var original=_selected.OriginalPath??_selected.Path;SelectFrame(new(original,_selected.Seconds,_selected.Score,_selected.Source));_status.Text="Originalframe wiederhergestellt.";return Task.CompletedTask;
    }
    private async Task GenerateImageAsync()
    {
        if(_selected==null||_displayPath==null)throw new IOException("Zuerst ein Frame oder Bild auswählen.");
        if(!long.TryParse(_seed.Text,out var seed))throw new ArgumentException("Seed muss eine ganze Zahl sein; -1 wählt einen zufälligen Wert.");
        _ai.Stop(); // Free the editor's text-model VRAM before image diffusion.
        var selected=_selected;var strength=_aiStrength.Value/100;var prompt=_imagePrompt.Text??"";
        _status.Text="Lokale Bild-KI wird vorbereitet …";
        var output=await new LocalArtworkImageAi(Path.Combine(_directory,"artwork-ai")).GenerateAsync(_displayPath,prompt,strength,seed,new Progress<string>(s=>_status.Text=s),_job!.Token,_imageCpu.IsChecked==true);
        var variant=new SetVideoFrame(output,selected.Seconds,selected.Score,selected.Source,true,selected.OriginalPath??selected.Path);AddFrame(variant);SelectFrame(variant);_status.Text="KI-Variante als neues Bild hinzugefügt. Originalframe bleibt über die Bildauswahl oder Wiederherstellen verfügbar.";
    }
    private void SetDisplayImage(string path){_displayPath=path;_image=new Avalonia.Media.Imaging.Bitmap(path);_images.Add(_image);Refresh();}
}
