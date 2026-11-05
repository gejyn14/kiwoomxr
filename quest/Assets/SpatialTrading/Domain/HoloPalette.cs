namespace SpatialTrading.Domain
{
    /// <summary>An sRGB colour, each channel 0–1. Pure: no Unity types, so the domain harness can test the palette.</summary>
    public readonly struct Rgba
    {
        public readonly float R, G, B, A;

        public Rgba(float r, float g, float b, float a = 1f) { R = r; G = g; B = b; A = a; }

        public static Rgba Bytes(byte r, byte g, byte b) => new Rgba(r / 255f, g / 255f, b / 255f);
    }

    /// <summary>Raw values behind <c>HoloStyle</c>'s muted text and panel fill (the colours the contrast test checks).</summary>
    public static class HoloPalette
    {
        /// <summary>Muted text, #86BAC6: about 6:1 over a bright wall seen through <see cref="PanelFill"/>.</summary>
        public static readonly Rgba Muted = Rgba.Bytes(0x86, 0xBA, 0xC6);

        /// <summary>Dark translucent panel fill (top of the gradient).</summary>
        public static readonly Rgba PanelFill = new Rgba(0.016f, 0.07f, 0.095f, 0.82f);
    }
}
