namespace Cratebase.App.Desktop.Views;

public sealed record ArtworkDesign
{
    public string Font {get;init;}="Inter";
    public double HeadlineSize {get;init;}=100;
    public double SubtitleSize {get;init;}=31;
    public double BrandSize {get;init;}=27;
    public double Scale {get;init;}=1;
    public double Spacing {get;init;}=24;
    public string TextColor {get;init;}="#FFFFFF";
    public string Weight {get;init;}="Black";
    public bool Italic {get;init;}
    public string Effect {get;init;}="Schatten";
    public double EffectSize {get;init;}=8;
    public string Frame {get;init;}="Keiner";
    public double FrameSize {get;init;}=8;
    public string Background {get;init;}="Verlauf";
    public double Shade {get;init;}=.8;
    public double ImageZoom {get;init;}=1;
    public double ImageX {get;init;}=.5;
    public double ImageY {get;init;}=.5;
}
