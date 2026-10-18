using System;
using System.Collections.Generic;
using Godot;
using JumpRun.Items;

namespace JumpRun.World;

/// <summary>
/// Plattform-Beschreibung in Chunk-lokalen Koordinaten (Center = Mitte der achsenparallelen Bounding-Box).
/// v2: Form, Drehung, Rotation (CONTRACT_V2 §4.2). Defaults entsprechen exakt v1.
/// </summary>
public sealed class PlatformSpec
{
    public Vector3 Center;
    /// <summary>Achsenparallele Bounding-Box. Rund/Polygon: Size.X == Size.Z == Umkreisdurchmesser.</summary>
    public Vector3 Size;
    public string Material = MaterialNames.PlatformGap;
    public Vector3 MoveOffset;
    public float MoveSpeed;
    public float Phase;
    public PlatformShape Shape = PlatformShape.Box;
    /// <summary>Statische Drehung um die Plattformmitte (rad); nur bei Size.X == Size.Z oder Cross.</summary>
    public float RotationY;
    /// <summary>Drehgeschwindigkeit (rad/s); ≠ 0 → RotatingPlatform.</summary>
    public float SpinSpeed;
    /// <summary>Nur Ramp: Höhenunterschied der Oberfläche (Size.Y = Rise + RampThickness).</summary>
    public float Rise;

    public const float RampThickness = 0.6f;

    public bool IsMoving => MoveOffset.LengthSquared() > 0.0001f;
    public bool IsRotating => Mathf.Abs(SpinSpeed) > 0.0001f;
    public bool IsRoundOrPolygon => Shape.IsRoundOrPolygon() || Shape == PlatformShape.Diamond;
    /// <summary>Höchster Punkt der Oberfläche (Ramp: am −Z-Ende).</summary>
    public float TopY => Center.Y + Size.Y * 0.5f;
    /// <summary>Oberfläche am +Z-Ende (Einstieg); = TopY außer bei Ramp.</summary>
    public float EntryTopY => Shape == PlatformShape.Ramp ? TopY - Rise : TopY;

    /// <summary>Innenkreisradius der Grundfläche (drehinvariant begehbar).</summary>
    public float InnerRadius
    {
        get
        {
            float r = Size.X * 0.5f;
            if (Shape.IsRound()) return r;
            if (Shape == PlatformShape.Diamond) return r * Mathf.Cos(Mathf.Pi / 4f);
            int n = Shape.PolygonSides();
            if (n > 0) return r * Mathf.Cos(Mathf.Pi / n);
            if (Shape == PlatformShape.Cross)
                return Mathf.Min(Size.X, Size.Z) * PlatformShapeExtensions.CrossArmRatio * 0.5f;
            return Mathf.Min(Size.X, Size.Z) * 0.5f;
        }
    }

    /// <summary>Oberflächenhöhe an einer lokalen Z-Position (Ramp interpoliert, sonst TopY).</summary>
    public float SurfaceYAt(float localZ)
    {
        if (Shape != PlatformShape.Ramp || Size.Z <= 0f) return TopY;
        float t = Mathf.Clamp((Center.Z + Size.Z * 0.5f - localZ) / Size.Z, 0f, 1f); // 0 = +Z-Ende, 1 = −Z-Ende
        return EntryTopY + Rise * t;
    }

    public PlatformSpec Clone() => (PlatformSpec)MemberwiseClone();
}

/// <summary>Kisten-Beschreibung (Position = Kistenmitte, lokal im Chunk).</summary>
public sealed class CrateSpec
{
    public Vector3 Position;
    public ItemType? ForcedDrop;
    public bool IsSupply;
    public int DropSeed;
}

/// <summary>Reine Daten eines Chunks (ohne Nodes) – testbar.</summary>
public sealed class ChunkLayout
{
    public int Index;
    public PatternType Pattern;
    public PatternType? NextPattern;
    public Vector3 EntryPoint;            // global
    public Vector3 ExitLocal;
    public Vector3 CheckpointLocal;
    public readonly List<PlatformSpec> Platforms = new();
    public readonly List<CrateSpec> Crates = new();
    public readonly List<Vector3> Anchors = new();
    /// <summary>Alle normalen (nicht Item-pflichtigen) Sprunglücken entlang des Pfads.</summary>
    public readonly List<float> NormalGaps = new();
    /// <summary>Alle normalen Höhenstufen nach oben entlang des Pfads.</summary>
    public readonly List<float> NormalSteps = new();
    /// <summary>Lücke/Wand des Item-Abschnitts (0, wenn keiner).</summary>
    public float ItemObstacleSize;

    public Vector3 ExitPoint => EntryPoint + ExitLocal;
    public Vector3 CheckpointPosition => EntryPoint + CheckpointLocal;

    // ------------------------------------------------------------------ Geometrie-Helfer (CONTRACT_V2 §4.2)

    /// <summary>Begehbare Fläche als Rechteck(e) oder Kreis (lokal XZ).</summary>
    private static bool UsesCircle(PlatformSpec p) =>
        p.IsRotating || p.IsRoundOrPolygon
        || (Mathf.Abs(p.RotationY) > 0.0001f && p.Shape != PlatformShape.Cross);

    /// <summary>Begehbare Rechtecke (min/max XZ). Cross → 2 Balken, sonst 1. Rückgabe: Anzahl.</summary>
    private static int Rects(PlatformSpec p, Span<Rect2> r)
    {
        float hx = p.Size.X * 0.5f, hz = p.Size.Z * 0.5f;
        if (p.Shape == PlatformShape.Cross)
        {
            float ax = p.Size.X * PlatformShapeExtensions.CrossArmRatio * 0.5f;
            float az = p.Size.Z * PlatformShapeExtensions.CrossArmRatio * 0.5f;
            r[0] = new Rect2(p.Center.X - hx, p.Center.Z - az, 2f * hx, 2f * az);
            r[1] = new Rect2(p.Center.X - ax, p.Center.Z - hz, 2f * ax, 2f * hz);
            return 2;
        }
        r[0] = new Rect2(p.Center.X - hx, p.Center.Z - hz, 2f * hx, 2f * hz);
        return 1;
    }

    private static float RectRect(Rect2 a, Rect2 b)
    {
        float dx = Mathf.Max(0f, Mathf.Max(b.Position.X - a.End.X, a.Position.X - b.End.X));
        float dz = Mathf.Max(0f, Mathf.Max(b.Position.Y - a.End.Y, a.Position.Y - b.End.Y));
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    private static float PointRect(Vector2 c, Rect2 r)
    {
        float dx = Mathf.Max(0f, Mathf.Max(r.Position.X - c.X, c.X - r.End.X));
        float dz = Mathf.Max(0f, Mathf.Max(r.Position.Y - c.Y, c.Y - r.End.Y));
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>
    /// Konservativer horizontaler Abstand der begehbaren Flächen zweier Plattformen (nie kleiner als der echte
    /// Abstand). Box/Square/Ramp/Cross = Rechteck(e); Diamond, rund, Polygon, gedreht oder drehend = Innenkreis.
    /// </summary>
    public static float WalkableGap(PlatformSpec a, PlatformSpec b)
    {
        bool ca = UsesCircle(a), cb = UsesCircle(b);
        var pa = new Vector2(a.Center.X, a.Center.Z);
        var pb = new Vector2(b.Center.X, b.Center.Z);
        if (ca && cb) return Mathf.Max(0f, pa.DistanceTo(pb) - a.InnerRadius - b.InnerRadius);
        Span<Rect2> ra = stackalloc Rect2[2];
        Span<Rect2> rb = stackalloc Rect2[2];
        if (ca || cb)
        {
            PlatformSpec circ = ca ? a : b, rect = ca ? b : a;
            Vector2 c = ca ? pa : pb;
            int n = Rects(rect, ra);
            float best = float.MaxValue;
            for (int i = 0; i < n; i++) best = Mathf.Min(best, PointRect(c, ra[i]));
            return Mathf.Max(0f, best - circ.InnerRadius);
        }
        int na = Rects(a, ra), nb = Rects(b, rb);
        float m = float.MaxValue;
        for (int i = 0; i < na; i++)
            for (int j = 0; j < nb; j++)
                m = Mathf.Min(m, RectRect(ra[i], rb[j]));
        return m;
    }

    /// <summary>Liegt der Punkt (XZ, lokal im Chunk) mit Rand <paramref name="margin"/> auf der begehbaren Fläche?</summary>
    public static bool SupportsPoint(PlatformSpec p, Vector3 localPos, float margin)
    {
        var c = new Vector2(localPos.X, localPos.Z);
        if (UsesCircle(p))
            return c.DistanceTo(new Vector2(p.Center.X, p.Center.Z)) <= p.InnerRadius - margin;
        Span<Rect2> r = stackalloc Rect2[2];
        int n = Rects(p, r);
        for (int i = 0; i < n; i++)
        {
            Rect2 q = r[i].Grow(-margin);
            if (q.Size.X < 0f || q.Size.Y < 0f) continue;
            if (c.X >= q.Position.X && c.X <= q.End.X && c.Y >= q.Position.Y && c.Y <= q.End.Y) return true;
        }
        return false;
    }

    public Aabb ComputeLocalBounds()
    {
        bool any = false;
        var min = Vector3.Zero;
        var max = Vector3.Zero;
        void Grow(Vector3 a, Vector3 b)
        {
            if (!any) { min = a; max = b; any = true; return; }
            min = new Vector3(Mathf.Min(min.X, a.X), Mathf.Min(min.Y, a.Y), Mathf.Min(min.Z, a.Z));
            max = new Vector3(Mathf.Max(max.X, b.X), Mathf.Max(max.Y, b.Y), Mathf.Max(max.Z, b.Z));
        }
        foreach (PlatformSpec p in Platforms)
        {
            Vector3 h = p.Size * 0.5f;
            Vector3 a = p.Center - h, b = p.Center + h;
            if (p.IsMoving)
            {
                Vector3 a2 = a + p.MoveOffset, b2 = b + p.MoveOffset;
                a = new Vector3(Mathf.Min(a.X, a2.X), Mathf.Min(a.Y, a2.Y), Mathf.Min(a.Z, a2.Z));
                b = new Vector3(Mathf.Max(b.X, b2.X), Mathf.Max(b.Y, b2.Y), Mathf.Max(b.Z, b2.Z));
            }
            Grow(a, b);
        }
        foreach (Vector3 an in Anchors) Grow(an - Vector3.One, an + Vector3.One);
        foreach (CrateSpec c in Crates) Grow(c.Position - Vector3.One, c.Position + Vector3.One);
        // Platz für Flaggen, Pickups und Spieler über den Plattformen.
        max.Y += 4f;
        Vector3 margin = new(1f, 1f, 1f);
        return new Aabb(min - margin, (max - min) + margin * 2f);
    }
}
