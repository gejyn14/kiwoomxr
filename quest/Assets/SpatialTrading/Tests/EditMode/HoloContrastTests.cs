using System;
using NUnit.Framework;
using SpatialTrading.Domain;

namespace SpatialTrading.Tests
{
    public sealed class HoloContrastTests
    {
        /// <summary>A bright passthrough wall (sRGB 0.8 grey) seen through the translucent panel fill.</summary>
        private static readonly Rgba BrightWall = new Rgba(0.8f, 0.8f, 0.8f);

        [Test] public void MutedTextMeetsFourPointFiveOverBrightPassthrough()
        {
            var background = Over(HoloPalette.PanelFill, BrightWall);
            Assert.That(Contrast(HoloPalette.Muted, background), Is.GreaterThanOrEqualTo(5.0));
        }

        /// <summary>Alpha compositing in sRGB values (what the eye-buffer blend does for a UI canvas).</summary>
        private static Rgba Over(Rgba top, Rgba bottom) => new Rgba(
            top.R * top.A + bottom.R * (1f - top.A),
            top.G * top.A + bottom.G * (1f - top.A),
            top.B * top.A + bottom.B * (1f - top.A));

        /// <summary>WCAG 2 contrast ratio from relative luminance.</summary>
        private static double Contrast(Rgba a, Rgba b)
        {
            double la = Luminance(a), lb = Luminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        private static double Luminance(Rgba c) => 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);

        private static double Linear(float channel) =>
            channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
    }
}
