namespace JumpRun.World;

/// <summary>Plattform-Grundformen (CONTRACT_V2 §4.1, Reihenfolge verbindlich).</summary>
public enum PlatformShape
{
    Box = 0,       // v1 (BoxMesh + BoxShape3D)
    Square = 1,    // Box mit Size.X == Size.Z
    Diamond = 2,   // Quadrat um 45° gedreht (Size.X == Size.Z)
    Cylinder = 3,  // rund, hoher Körper (Size.Y >= 2), Säule
    Disc = 4,      // rund, flach (Size.Y <= 0.6)
    Triangle = 5,  // aufrechtes Dreiecksprisma
    Pentagon = 6,
    Hexagon = 7,
    Octagon = 8,
    Ramp = 9,      // Keil, steigt in -Z (Laufrichtung)
    Cross = 10,    // Plus-Form aus zwei Balken
}

public static class PlatformShapeExtensions
{
    public const int Count = 11;

    /// <summary>Eckenzahl des Polygons (0 = kein reguläres Polygon).</summary>
    public static int PolygonSides(this PlatformShape s) => s switch
    {
        PlatformShape.Triangle => 3,
        PlatformShape.Pentagon => 5,
        PlatformShape.Hexagon => 6,
        PlatformShape.Octagon => 8,
        _ => 0,
    };

    public static bool IsRound(this PlatformShape s) => s is PlatformShape.Cylinder or PlatformShape.Disc;

    public static bool IsRoundOrPolygon(this PlatformShape s) => s.IsRound() || s.PolygonSides() > 0;

    /// <summary>Für Rund-/Polygonformen: Segmentzahl der Mantelfläche im Mesh.</summary>
    public const int RoundSegments = 24;

    /// <summary>Kreuzbalken-Breite relativ zur Bounding-Box-Kante.</summary>
    public const float CrossArmRatio = 0.4f;
}
