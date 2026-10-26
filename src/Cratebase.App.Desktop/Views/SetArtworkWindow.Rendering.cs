using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Cratebase.App.Desktop.Views;

public sealed partial class SetArtworkWindow
{
    private static Control StyledText(string text,double size,IBrush foreground,int lines,ArtworkDesign design,Color accent)
    {
        var weight=design.Weight switch{"Normal"=>FontWeight.Normal,"SemiBold"=>FontWeight.SemiBold,"Bold"=>FontWeight.Bold,_=>FontWeight.Black};
        TextBlock Label(IBrush color)=>new(){Text=text,FontSize=size,FontFamily=FontFamily.Parse(design.Font),FontWeight=weight,FontStyle=design.Italic?FontStyle.Italic:FontStyle.Normal,Foreground=color,TextWrapping=TextWrapping.Wrap,MaxLines=lines,TextTrimming=TextTrimming.CharacterEllipsis,LineHeight=size*1.08};
        var label=Label(foreground);var strength=Math.Clamp(design.EffectSize,0,30);
        if(strength>0&&design.Effect is "Schatten" or "Neon")label.Effect=new DropShadowEffect{Color=design.Effect=="Neon"?accent:Colors.Black,BlurRadius=design.Effect=="Neon"?strength*2:strength,OffsetX=design.Effect=="Neon"?0:strength*.4,OffsetY=design.Effect=="Neon"?0:strength*.4,Opacity=1};
        if(strength>0&&design.Effect is "Kontur" or "3D")
        {
            var stack=new Grid();
            if(design.Effect=="Kontur")foreach(var (x,y) in new[]{(-1,-1),(0,-1),(1,-1),(-1,0),(1,0),(-1,1),(0,1),(1,1)}){var shadow=Label(Brushes.Black);shadow.RenderTransform=new TranslateTransform(x*strength*.4,y*strength*.4);stack.Children.Add(shadow);}
            else for(var i=(int)Math.Ceiling(strength);i>0;i--){var shadow=Label(new SolidColorBrush(accent));shadow.RenderTransform=new TranslateTransform(i*.65,i*.65);stack.Children.Add(shadow);}
            stack.Children.Add(label);return stack;
        }
        return label;
    }
    private static void AddArtworkFrame(Grid root,ArtworkDesign design,IBrush brush,Color color)
    {
        var width=Math.Clamp(design.FrameSize,1,30);
        if(design.Frame is "Linie" or "Neon")
        {
            var border=new Border{Margin=new Thickness(20),BorderBrush=brush,BorderThickness=new Thickness(width),IsHitTestVisible=false};
            if(design.Frame=="Neon")border.BoxShadow=new BoxShadows(new BoxShadow{Color=color,Blur=width*3,Spread=width*.2});root.Children.Add(border);
        }
        if(design.Frame=="Letterbox")foreach(var vertical in new[]{VerticalAlignment.Top,VerticalAlignment.Bottom})root.Children.Add(new Border{Background=Brushes.Black,Height=width*4,VerticalAlignment=vertical,IsHitTestVisible=false});
        if(design.Frame=="Ecken")foreach(var (h,v) in new[]{(HorizontalAlignment.Left,VerticalAlignment.Top),(HorizontalAlignment.Right,VerticalAlignment.Top),(HorizontalAlignment.Left,VerticalAlignment.Bottom),(HorizontalAlignment.Right,VerticalAlignment.Bottom)})
            root.Children.Add(new Border{Width=120,Height=85,Margin=new Thickness(24),HorizontalAlignment=h,VerticalAlignment=v,BorderBrush=brush,BorderThickness=new Thickness(h==HorizontalAlignment.Left?width:0,v==VerticalAlignment.Top?width:0,h==HorizontalAlignment.Right?width:0,v==VerticalAlignment.Bottom?width:0),IsHitTestVisible=false});
    }
}
