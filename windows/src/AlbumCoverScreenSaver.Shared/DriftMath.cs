namespace AlbumCoverScreenSaver.Shared;

/// <summary>
/// The rest of Drifting Float's arithmetic: cover sizes, the edge fade, and the
/// blurred backdrop's radius.
/// </summary>
public static class DriftMath
{
    /// <summary>The longest edge the backdrop art is reduced to before blurring.</summary>
    public const int BackdropTarget = 480;

    /// <summary>Seconds the backdrop takes to cross from one album to the next.</summary>
    public const double BackdropFade = 1.4;

    /// <summary>Black over the backdrop at full coverage.</summary>
    public const float BackdropScrim = 0.38f;

    /// <summary>The backdrop art is drawn oversized from the centre by this much.</summary>
    public const float BackdropOversize = 1.15f;

    /// <summary>
    /// The blur radius in reduced pixels, from the 0 to 1 setting.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Expressed as a fraction of the reduced size, because the fraction is what
    /// the eye reads: about 0.4% is essentially the cover itself, slightly soft,
    /// and 6% is a single wash of its colour. A radius in pixels would mean
    /// something different at every target size.
    /// </para>
    /// <para>
    /// Snapped to even numbers because the radius is part of the blur cache key.
    /// A continuous slider would otherwise fill the cache with copies that
    /// differ by a pixel of blur and are indistinguishable on screen.
    /// </para>
    /// <para>
    /// Worked values: 0 gives 2, the default 0.3 gives 10, and 1 gives 28.
    /// </para>
    /// </remarks>
    public static int BlurRadius(double backdropBlur)
    {
        var fraction = 0.004 + (0.056 * Ease.Clamp((float)backdropBlur));
        var pixels = BackdropTarget * fraction;
        return (int)Math.Max(1, Math.Round(pixels / 2.0, MidpointRounding.AwayFromZero) * 2);
    }

    /// <summary>The side of a cover, and how fast it goes, for a given depth.</summary>
    /// <remarks>
    /// The size band is deliberately narrow: far covers have to stay readable
    /// and near ones must not dominate. The speed band is not, because parallax
    /// is what sells the depth, and a near cover has to visibly outrun a far
    /// one.
    /// </remarks>
    public static (float Side, double BaseSpeed) SizeForDepth(float height, double depth) =>
        (height * (float)(0.12 + (0.20 * depth)), 4.0 + (26.0 * depth));

    /// <summary>
    /// How visible a cover is, from how far its centre has passed beyond the
    /// edges of the screen.
    /// </summary>
    /// <remarks>
    /// <b>Both axes.</b> An earlier version measured the overshoot on x alone,
    /// and every cover went transparent halfway up the screen whenever the wind
    /// happened to blow vertically. Using the distance of the combined
    /// overshoot means a cover arrives and leaves the same way on every edge,
    /// and across a corner at a diagonal.
    ///
    /// A cover is fully opaque until its centre leaves the frame, and fully
    /// transparent one of its own side-lengths beyond it.
    /// </remarks>
    public static float EdgeFade(
        float centreX, float centreY, float width, float height, float side)
    {
        var outX = Math.Max(0f, Math.Abs(centreX - (width / 2f)) - (width / 2f));
        var outY = Math.Max(0f, Math.Abs(centreY - (height / 2f)) - (height / 2f));
        var beyond = MathF.Sqrt((outX * outX) + (outY * outY));

        return Math.Max(0f, 1f - (beyond / Math.Max(1f, side)));
    }

    /// <summary>
    /// Distant covers sit back into the dark. Only in random-size mode: with
    /// every cover at the same depth this would dim the whole field evenly for
    /// no reason.
    /// </summary>
    public static float DepthAlpha(double depth) => 0.55f + (0.45f * (float)depth);

    /// <summary>Beyond this distance from the centre a cover is off screen at any angle.</summary>
    public static float Reach(float width, float height) =>
        MathF.Sqrt((width * width) + (height * height)) / 2f;
}
