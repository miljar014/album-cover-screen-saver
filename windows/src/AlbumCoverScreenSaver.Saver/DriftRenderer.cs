using AlbumCoverScreenSaver.Shared;
using SkiaSharp;

namespace AlbumCoverScreenSaver.Saver;

/// <summary>
/// Drifting Float. Covers sliding across black on a shared wind, with the
/// playing album filling the screen behind them, heavily blurred.
/// </summary>
/// <remarks>
/// <para>
/// The wind holds a direction for half a minute or so and then swings somewhere
/// genuinely different. The covers already on screen do not snap round together:
/// each one turns on its own slice of a five second window, the heavy near ones
/// leaning into it first and the light far ones stopping dead in the middle of
/// their turn and setting off again. The flock swings round as a ripple.
/// </para>
/// <para>
/// The angles and the stagger are in <see cref="Wind"/> and the sizes and fades
/// in <see cref="DriftMath"/>, because every one of them is the sort of mistake
/// that looks fine until you watch for two minutes. This file is what is left:
/// state, and drawing.
/// </para>
/// </remarks>
internal sealed class DriftRenderer : IStyleRenderer, IDisposable
{
    private readonly SaverData _data;
    private readonly AlbumPicker _picker;
    private readonly Random _random;
    private readonly BlurStore _blur;
    private readonly bool _isPreview;

    private readonly List<Floater> _floaters = [];

    // Everything below is held rather than made each frame. Thirty three covers
    // at thirty frames a second turns a fresh list, a fresh set and a fresh
    // paint per cover into thousands of short-lived objects a second, and the
    // collector pauses that causes are exactly what "jumpy" looks like.
    private readonly List<Floater> _drawOrder = [];
    private readonly HashSet<int> _onScreen = [];
    private readonly ShadowSprite _shadows = new();
    private readonly SKPaint _shadowPaint = new() { IsAntialias = true, FilterQuality = SKFilterQuality.Low };
    private readonly SKPaint _backdropPaint = new() { IsAntialias = true, FilterQuality = SKFilterQuality.Medium };
    private bool _orderStale = true;

    private double _windAngle;
    private double _windShiftStart = -1;
    private double _windNextShift;

    private int? _backIndex;
    private int? _backPreviousIndex;
    private double _backChangedAt = -100;

    public DriftRenderer(
        SaverData data, AlbumPicker picker, Random random, BlurStore blur, bool isPreview)
    {
        _data = data;
        _picker = picker;
        _random = random;
        _blur = blur;
        _isPreview = isPreview;
    }

    public void BuildLayout(float width, float height)
    {
        _floaters.Clear();

        if (_data.Albums.Count == 0 || width <= 10 || height <= 10) return;

        _windAngle = _random.NextDouble() * Wind.Tau;
        _windShiftStart = -1;
        _windNextShift = Hold();

        var wanted = _isPreview
            ? Math.Max(4, _data.Settings.DriftCount / 3)
            : _data.Settings.DriftCount;

        var used = new HashSet<int>();
        for (var i = 0; i < wanted; i++)
        {
            var index = _picker.PickAvoiding(_data.Albums.Count, _data.Settings.RecencyBias, used);
            used.Add(index);
            _floaters.Add(MakeFloater(index, width, height, seeded: true));
        }

        _orderStale = true;

        Log.Write($"drift layout: {_floaters.Count} covers across {width:0}x{height:0} points");
    }

    private double Hold() =>
        Wind.HoldMinimum + (_random.NextDouble() * (Wind.HoldMaximum - Wind.HoldMinimum));

    /// <summary>
    /// A new cover, either scattered over the view at build time or arriving
    /// from upwind.
    /// </summary>
    /// <remarks>
    /// Its heading and headingFrom are both the current wind, so a cover
    /// arriving mid-turn is already on the new direction: it was never caught by
    /// the gust that turned the others.
    /// </remarks>
    private Floater MakeFloater(int index, float width, float height, bool seeded)
    {
        double depth;
        float side;
        double baseSpeed;

        if (_data.Settings.DriftRandomSize)
        {
            depth = _random.NextDouble();
            (side, baseSpeed) = DriftMath.SizeForDepth(height, depth);
        }
        else
        {
            depth = 0.5;
            side = height * (float)(0.16 + (_random.NextDouble() * 0.18))
                   * (float)_data.Settings.DriftScale;
            baseSpeed = 6.0 + (_random.NextDouble() * 14.0);
        }

        float x, y;
        if (seeded)
        {
            x = (float)(_random.NextDouble() * width);
            y = (float)(_random.NextDouble() * height);
        }
        else
        {
            (x, y) = Wind.SpawnUpwind(
                width, height, _windAngle, side, (_random.NextDouble() * 2.0) - 1.0);
        }

        var spread = (_random.NextDouble() * 0.28) - 0.14;

        return new Floater
        {
            Index = index,
            Depth = depth,
            Side = side,
            Speed = baseSpeed * _data.Settings.DriftSpeed,
            CentreX = x,
            CentreY = y,
            Spread = spread,
            Heading = _windAngle + spread,
            HeadingFrom = _windAngle + spread,

            // Depth when covers vary in size, so the visual hierarchy and the
            // turn hierarchy agree. Random otherwise, so the turn still
            // staggers in fixed-size mode instead of moving as one sheet.
            Agility = _data.Settings.DriftRandomSize ? depth : _random.NextDouble(),
            Angle = (float)((_random.NextDouble() * 0.08) - 0.04),
        };
    }

    public void Advance(double phase, float width, float height)
    {
        if (_floaters.Count == 0) return;

        AdvanceWind(phase);

        var turning = _windShiftStart >= 0;
        var u = turning ? (phase - _windShiftStart) / Wind.TurnSeconds : 1.0;
        var reach = DriftMath.Reach(width, height);

        for (var i = 0; i < _floaters.Count; i++)
        {
            var floater = _floaters[i];
            var target = _windAngle + floater.Spread;
            var pace = 1.0;

            if (turning)
            {
                var (local, turnPace) = Wind.TurnOf(u, floater.Agility);
                floater.Heading = Wind.Lerp(floater.HeadingFrom, target, Ease.InOut(local));
                pace = turnPace;
            }
            else
            {
                floater.Heading = target;
            }

            var velocity = floater.Speed * pace;
            floater.CentreX += (float)(Math.Cos(floater.Heading) * velocity * TileField.FrameInterval);
            floater.CentreY += (float)(Math.Sin(floater.Heading) * velocity * TileField.FrameInterval);

            // Fully clear of the screen, and moving. Never recycle a cover while
            // it is at its slowest: that is exactly when the eye is resting on
            // it, and swapping it then is the one change a person notices.
            var fromCentre = Math.Sqrt(
                Math.Pow(floater.CentreX - (width / 2f), 2) +
                Math.Pow(floater.CentreY - (height / 2f), 2));

            if (pace > 0.25 && fromCentre > reach + floater.Side)
            {
                _onScreen.Clear();
                foreach (var other in _floaters) _onScreen.Add(other.Index);

                var index = _picker.PickAvoiding(_data.Albums.Count, _data.Settings.RecencyBias, _onScreen);
                _floaters[i] = MakeFloater(index, width, height, seeded: false);

                // The only moment a depth changes, so the only moment the
                // painter's order can be wrong.
                _orderStale = true;
            }
        }
    }

    /// <summary>
    /// Holds a direction, then swings to a genuinely different one.
    /// </summary>
    /// <remarks>
    /// The new direction is authoritative the moment it is picked: arrivals
    /// spawn upwind of it and travel along it straight away, while the covers
    /// already on screen are eased round to it one at a time. Snapshotting each
    /// cover's heading at the start of the turn is what lets it interpolate from
    /// where it actually was rather than from the old wind.
    /// </remarks>
    private void AdvanceWind(double phase)
    {
        if (_windShiftStart >= 0)
        {
            if (phase - _windShiftStart > Wind.TurnSeconds)
            {
                _windShiftStart = -1;
                _windNextShift = phase + Hold();
            }
            return;
        }

        if (phase < _windNextShift) return;

        _windShiftStart = phase;
        _windAngle = Wind.PickNewDirection(_random, _windAngle);

        foreach (var floater in _floaters) floater.HeadingFrom = floater.Heading;
    }

    public void Draw(SKCanvas canvas, float width, float height, double phase)
    {
        DrawBackdrop(canvas, width, height, phase);

        // Painter's algorithm: far covers first, so near ones pass in front.
        // Sorted only when a cover has actually been replaced, because a depth
        // never changes while a cover is on screen.
        if (_orderStale)
        {
            _drawOrder.Clear();
            _drawOrder.AddRange(_floaters);
            _drawOrder.Sort(static (a, b) => a.Depth.CompareTo(b.Depth));
            _orderStale = false;
        }

        var sprite = _shadows.Get(0.03f);

        foreach (var floater in _drawOrder)
        {
            var alpha = DriftMath.EdgeFade(
                floater.CentreX, floater.CentreY, width, height, floater.Side);

            // Only in random-size mode. With every cover at the same depth this
            // would dim the whole field evenly for no reason at all.
            if (_data.Settings.DriftRandomSize) alpha *= DriftMath.DepthAlpha(floater.Depth);

            if (alpha <= 0.01f) continue;

            var rect = SKRect.Create(
                floater.CentreX - (floater.Side / 2f),
                floater.CentreY - (floater.Side / 2f),
                floater.Side, floater.Side);

            canvas.Save();
            canvas.RotateRadians(floater.Angle, floater.CentreX, floater.CentreY);

            // The shadow is a stretched sprite, not a filter. See ShadowSprite:
            // thirty three blur filters a frame is what made this style jump.
            _shadowPaint.Color = SKColors.White.WithAlpha(
                (byte)Math.Clamp(ShadowSprite.Strength * alpha * 255f, 0f, 255f));
            canvas.DrawBitmap(sprite, ShadowSprite.Placement(rect), _shadowPaint);

            // Never quite opaque, so a cover always sits slightly into the black
            // field rather than on top of it.
            Drawing.DrawImage(
                canvas, _data.ImageFor(floater.Index), rect,
                alpha * 0.92f, floater.Side * 0.03f, shadow: false);

            canvas.Restore();
        }

        Drawing.DrawArchiveLabel(canvas, _data, width, height, _isPreview);
    }

    /// <summary>
    /// The playing cover, blurred to a wash and darkened, filling the screen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Shown only while something is actually playing. With nothing on, the
    /// current album fades out over 1.4 seconds and the field stays pure black,
    /// which is what the style looks like at rest.
    /// </para>
    /// <para>
    /// The comparison is between nullable indices, so <em>stopping</em> is a
    /// state change that starts a fade out exactly as a track change starts a
    /// crossfade. It is crossfaded, never cut.
    /// </para>
    /// </remarks>
    private void DrawBackdrop(SKCanvas canvas, float width, float height, double phase)
    {
        var target = _data.LiveAlbumIndex;

        if (target != _backIndex)
        {
            _backPreviousIndex = _backIndex;
            _backIndex = target;
            _backChangedAt = phase;
        }

        var t = Ease.InOut((float)((phase - _backChangedAt) / DriftMath.BackdropFade));

        var coverage = Paint(_backPreviousIndex, 1f - t) + Paint(_backIndex, t);
        if (coverage <= 0.005f) return;

        // Tied to coverage rather than fixed, so a backdrop that is fading out
        // takes its own darkening with it and lands on true black instead of on
        // a grey wash. During a crossfade the two alphas sum to one, so this
        // stays steady.
        Drawing.Scrim(canvas, width, height, DriftMath.BackdropScrim * Math.Min(1f, coverage));

        float Paint(int? index, float alpha)
        {
            if (alpha <= 0.005f || index is not { } i) return 0f;

            var albums = _data.Albums;

            // Indices are invalidated by an archive reload. This is what stops
            // that from being a crash.
            if (i < 0 || i >= albums.Count) return 0f;

            var blurred = _blur.Blurred(
                albums[i].Id, _data.ImageFor(i),
                DriftMath.BackdropTarget, DriftMath.BlurRadius(_data.Settings.DriftBackdropBlur));

            if (blurred is null) return 0f;

            // Oversized from the centre, so it fills any aspect ratio without
            // letterboxing and the overhang hides the blur's soft edges.
            var side = Math.Max(width, height) * DriftMath.BackdropOversize;
            var rect = SKRect.Create(
                (width / 2f) - (side / 2f), (height / 2f) - (side / 2f), side, side);

            // Medium rather than High: this is a 480 pixel bitmap stretched
            // across the whole screen, and it has already been blurred to a
            // wash. Bicubic sampling of it every frame buys nothing anyone
            // could see and costs a great deal.
            _backdropPaint.Color = SKColors.White.WithAlpha((byte)Math.Clamp(alpha * 255f, 0f, 255f));
            canvas.DrawBitmap(blurred, rect, _backdropPaint);

            return alpha;
        }
    }

    public void Dispose()
    {
        _shadows.Dispose();
        _shadowPaint.Dispose();
        _backdropPaint.Dispose();
    }
}
