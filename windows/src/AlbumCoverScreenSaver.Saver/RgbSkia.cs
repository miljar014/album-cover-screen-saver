using AlbumCoverScreenSaver.Shared;
using SkiaSharp;

namespace AlbumCoverScreenSaver.Saver;
/// <summary>Turns an <see cref="Rgb"/> into something Skia can paint with.</summary>
internal static class RgbSkia
{
    public static SKColor ToSkia(this Rgb colour) => new(
        (byte)Math.Clamp(colour.R * 255f, 0f, 255f),
        (byte)Math.Clamp(colour.G * 255f, 0f, 255f),
        (byte)Math.Clamp(colour.B * 255f, 0f, 255f),
        (byte)Math.Clamp(colour.A * 255f, 0f, 255f));

    /// <summary>The same colour at a different opacity, multiplying any it has.</summary>
    public static SKColor ToSkia(this Rgb colour, float alpha) =>
        colour.WithAlpha(colour.A * alpha).ToSkia();
}
