namespace AlbumCoverScreenSaver.Saver;

/// <summary>One drifting cover.</summary>
internal sealed class Floater
{
    public float CentreX { get; set; }

    public float CentreY { get; set; }

    /// <summary>
    /// Points per second <em>along its own heading</em>: a scalar, not a
    /// velocity. The wind turns underneath these, and each one carries only its
    /// own pace.
    /// </summary>
    public double Speed { get; set; }

    /// <summary>A few degrees off the wind, so the flock does not move in lockstep.</summary>
    public double Spread { get; set; }

    public double Heading { get; set; }

    /// <summary>Where this cover was heading when the current turn began.</summary>
    public double HeadingFrom { get; set; }

    /// <summary>
    /// 0 is shoved around by a change of wind: it comes to a stop and sets off
    /// again. 1 is heavy enough to lean into it and carry on.
    /// </summary>
    public double Agility { get; set; }

    public float Side { get; set; }

    public int Index { get; set; }

    /// <summary>A static tilt of a degree or two, not a rotation over time.</summary>
    public float Angle { get; set; }

    /// <summary>0 is far: small, slow, hazy. 1 is near: large, fast, crisp.</summary>
    public double Depth { get; set; } = 0.5;
}
