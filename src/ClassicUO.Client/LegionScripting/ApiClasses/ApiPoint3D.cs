namespace ClassicUO.LegionScripting.ApiClasses;

/// <summary>
/// Represents a point in a three-dimensional space
/// </summary>
public class ApiPoint3D
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }

    public override string ToString() => $"({X}, {Y}, {Z})";
}

public class ApiPoint2D
{
    public int X { get; set; }
    public int Y { get; set; }

    public override string ToString() => $"({X}, {Y})";

    public bool IsEmpty => X <= 0 && Y <= 0;
}
