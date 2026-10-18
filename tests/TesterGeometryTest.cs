using System;
using System.Collections.Generic;
using Godot;
using JumpRun.Game;
using JumpRun.Items;
using JumpRun.World;

namespace JumpRun.Tests;

/// <summary>
/// Tester: geometrische Lösbarkeit über viele Seeds und hohe Schwierigkeit (reine Layout-Daten, keine Physik).
/// Prüft Sprungdistanz in XZ (inkl. Seitversatz) gegen eine ballistische Obergrenze, Kistenauflage,
/// Brückenlänge vs. BigGap und Item-Verfügbarkeit vor jedem Item-Chunk.
/// v2: Formgeometrie über ChunkLayout.WalkableGap / EntryTopY / SupportsPoint, Innenradius ≥ 1,2 m,
/// Rampen ≤ 25°, |SpinSpeed| ≤ 0,9, Drehungen nur bei quadratischer Bounding-Box, Kisten nie auf Ramp/drehend/bewegt.
/// Start: godot --headless --path . res://tests/TesterGeometryTest.tscn → GEOMETRY_TEST_DONE ...
/// </summary>
public partial class TesterGeometryTest : Node
{
    private readonly Dictionary<string, (int Count, string Example)> _issues = new();

    public override void _Ready()
    {
        int chunks = 0;
        for (int seed = 1; seed <= 60; seed++)
        {
            var b = new ChunkBuilder(seed * 104729);
            Vector3 entry = Vector3.Zero;
            PatternType? prev = null;
            int streak = 0;
            ChunkLayout? prevL = null;
            for (int i = 0; i < 300; i++)
            {
                bool prevItem = prev.HasValue && prev.Value.IsItemRequired();
                ChunkLayout l = b.Plan(i, entry, prev, streak, prevItem, ChunkBuilder.DifficultyForIndex(i));
                CheckChunk(seed, l, prevL);
                chunks++;
                streak = prev.HasValue && prev.Value == l.Pattern ? streak + 1 : 1;
                prev = l.Pattern;
                entry = l.ExitPoint;
                prevL = l;
            }
        }
        foreach (var kv in _issues)
            GD.Print($"[Geometry] ISSUE {kv.Key}: {kv.Value.Count}× z. B. {kv.Value.Example}");
        GD.Print($"GEOMETRY_TEST_DONE chunks={chunks} issueTypes={_issues.Count}");
        GetTree().Quit(_issues.Count == 0 ? 0 : 1); // nach Fix-Runde: Befunde lassen den Lauf scheitern
    }

    private void Issue(string key, string example)
    {
        _issues[key] = _issues.TryGetValue(key, out var v) ? (v.Count + 1, v.Example) : (1, example);
    }

    /// <summary>Max. horizontale Sprungweite (Sprint, kein Anlauf-Bonus) bei Höhendifferenz dy (positiv = nach oben).</summary>
    private static float MaxJump(float dy)
    {
        float v = GameConstants.JumpVelocity, g = GameConstants.Gravity;
        float disc = v * v - 2f * g * dy;
        if (disc < 0f) return -1f;
        float t = (v + Mathf.Sqrt(disc)) / g;
        return GameConstants.SprintSpeed * t;
    }

    private static float RectGap(PlatformSpec a, PlatformSpec b) => ChunkLayout.WalkableGap(a, b);

    private void CheckShapes(string id, ChunkLayout l)
    {
        for (int k = 0; k < l.Platforms.Count; k++)
        {
            PlatformSpec p = l.Platforms[k];
            if (p.IsRoundOrPolygon && p.InnerRadius < GameConstants.MinWalkableInnerRadius - 0.001f)
                Issue("InnerRadius < MinWalkableInnerRadius", $"{id} k={k} {p.Shape} r={p.InnerRadius:F2}");
            if (p.IsRoundOrPolygon && Mathf.Abs(p.Size.X - p.Size.Z) > 0.001f)
                Issue("Rund/Polygon nicht quadratisch", $"{id} k={k} {p.Shape} size={p.Size}");
            if (Mathf.Abs(p.SpinSpeed) > GameConstants.RotatingMaxAngularSpeed + 0.0001f)
                Issue("SpinSpeed zu hoch", $"{id} k={k} spin={p.SpinSpeed:F2}");
            if (p.IsRotating && p.IsMoving)
                Issue("Drehend und bewegt zugleich", $"{id} k={k}");
            if (p.IsRotating && p.Shape is not (PlatformShape.Disc or PlatformShape.Cylinder or PlatformShape.Triangle
                    or PlatformShape.Pentagon or PlatformShape.Hexagon or PlatformShape.Octagon or PlatformShape.Square
                    or PlatformShape.Diamond or PlatformShape.Cross))
                Issue("Drehende Form nicht erlaubt", $"{id} k={k} {p.Shape}");
            if (Mathf.Abs(p.RotationY) > 0.0001f && Mathf.Abs(p.Size.X - p.Size.Z) > 0.001f && p.Shape != PlatformShape.Cross)
                Issue("RotationY bei nicht quadratischer Box", $"{id} k={k} {p.Shape}");
            if (p.Shape == PlatformShape.Ramp)
            {
                float slope = Mathf.RadToDeg(Mathf.Atan2(p.Rise, p.Size.Z));
                if (slope > GameConstants.MaxRampSlopeDeg + 0.01f) Issue("Rampe zu steil", $"{id} k={k} {slope:F1}°");
                if (Mathf.Abs(p.Size.Y - (p.Rise + PlatformSpec.RampThickness)) > 0.001f) Issue("Rampe Size.Y != Rise + 0.6", $"{id} k={k}");
                if (p.Rise <= 0f) Issue("Rampe ohne Steigung", $"{id} k={k}");
            }
            if (p.Shape == PlatformShape.Cylinder && p.Size.Y < 2f - 0.001f) Issue("Cylinder zu flach", $"{id} k={k} h={p.Size.Y:F2}");
            if (p.Shape == PlatformShape.Disc && p.Size.Y > 0.6f + 0.001f) Issue("Disc zu dick", $"{id} k={k} h={p.Size.Y:F2}");
            if (p.Shape != PlatformShape.Box && (l.Pattern is PatternType.Start or PatternType.NarrowBeams or PatternType.BigGap or PatternType.TallWall))
                Issue("Formplattform in reinem Box-Muster", $"{id} k={k} {p.Shape}");
            if (p.IsRotating && l.Index < 4) Issue("Drehplattform vor Index 4", $"{id} k={k}");
        }
        // Statische Plattformen dürfen sich nicht durchdringen (Bounding-Boxen; nur aufeinanderfolgende).
        for (int k = 1; k < l.Platforms.Count; k++)
        {
            PlatformSpec a = l.Platforms[k - 1], b = l.Platforms[k];
            if (a.IsMoving || b.IsMoving || l.Pattern is PatternType.TallWall) continue;
            float zOverlap = Mathf.Min(a.Center.Z + a.Size.Z / 2, b.Center.Z + b.Size.Z / 2) - Mathf.Max(a.Center.Z - a.Size.Z / 2, b.Center.Z - b.Size.Z / 2);
            float xOverlap = Mathf.Min(a.Center.X + a.Size.X / 2, b.Center.X + b.Size.X / 2) - Mathf.Max(a.Center.X - a.Size.X / 2, b.Center.X - b.Size.X / 2);
            if (zOverlap > 0.01f && xOverlap > 0.01f && (a.Shape != PlatformShape.Box || b.Shape != PlatformShape.Box))
                Issue("Formplattformen überlappen", $"{id} k={k} {a.Shape}/{b.Shape} zOv={zOverlap:F2} xOv={xOverlap:F2}");
        }
    }

    private void CheckChunk(int seed, ChunkLayout l, ChunkLayout? prevL)
    {
        string id = $"seed={seed * 104729} idx={l.Index} {l.Pattern}";
        CheckShapes(id, l);
        // Sprünge zwischen aufeinanderfolgenden statischen Plattformen (normale Muster)
        if (!l.Pattern.IsItemRequired() && l.Pattern != PatternType.Start)
        {
            for (int k = 1; k < l.Platforms.Count; k++)
            {
                PlatformSpec a = l.Platforms[k - 1], c = l.Platforms[k];
                if (a.IsMoving || c.IsMoving) continue;
                float gap = RectGap(a, c);
                float dy = c.EntryTopY - a.TopY;
                // 0,8 m Sicherheitsabzug für Kapselradius/Reaktionszeit
                float reach = MaxJump(dy) - 0.8f;
                if (gap > 0.05f && (reach < 0f || gap > reach))
                    Issue("Sprung zu weit/hoch", $"{id} k={k} gapXZ={gap:F2} dy={dy:F2} reach={reach:F2}");
                if (dy > GameConstants.MaxSafeStep + 0.01f && gap > 0.05f)
                    Issue("Stufe > MaxSafeStep", $"{id} k={k} dy={dy:F2}");
            }
            // Bewegliche Plattformen: schlechtester Fall (maximaler Abstand über die Bewegung)
            for (int k = 1; k < l.Platforms.Count; k++)
            {
                PlatformSpec a = l.Platforms[k - 1], c = l.Platforms[k];
                if (!a.IsMoving && !c.IsMoving) continue;
                float best = float.MaxValue;
                for (int s = 0; s <= 10; s++)
                {
                    for (int r = 0; r <= 10; r++)
                    {
                        var aa = Shifted(a, s / 10f); var cc = Shifted(c, r / 10f);
                        float gap = RectGap(aa, cc);
                        float dy = cc.EntryTopY - aa.TopY;
                        float reach = MaxJump(dy) - 0.8f;
                        if (reach >= 0f) best = Mathf.Min(best, gap - reach);
                    }
                }
                if (best > 0f) Issue("Bewegliche Plattform nie erreichbar", $"{id} k={k} miss={best:F2}");
            }
        }
        // Kisten müssen auf einer Plattform stehen
        foreach (CrateSpec cs in l.Crates)
        {
            bool supported = false;
            foreach (PlatformSpec p in l.Platforms)
            {
                if (p.IsMoving || p.IsRotating || p.Shape == PlatformShape.Ramp) continue;
                bool onTop = Mathf.Abs(cs.Position.Y - 0.5f - p.TopY) < 0.05f;
                if (onTop && ChunkLayout.SupportsPoint(p, cs.Position, 0.3f)) { supported = true; break; }
            }
            foreach (PlatformSpec p in l.Platforms)
                if ((p.IsRotating || p.Shape == PlatformShape.Ramp) && Mathf.Abs(cs.Position.Y - 0.5f - p.TopY) < 0.05f
                    && ChunkLayout.SupportsPoint(p, cs.Position, 0f))
                    Issue("Kiste auf Ramp/Drehplattform", $"{id} crate={cs.Position}");
            if (!supported) Issue("Kiste ohne Auflage", $"{id} crate={cs.Position} forced={cs.ForcedDrop}");
        }
        if (l.Pattern == PatternType.BigGap)
        {
            // Spielermitte kann max. ~0,4 m über der Kante stehen → effektiv nutzbare Brückenlänge
            float effective = GameConstants.BridgeLength - 0.4f;
            if (l.ItemObstacleSize > effective)
                Issue("Brücke reicht nicht ganz über BigGap (Restsprung nötig)", $"{id} gap={l.ItemObstacleSize:F2} bridgeEff={effective:F2}");
        }
        if (l.Pattern.IsItemRequired() && prevL != null)
        {
            ItemType[] need = l.Pattern.RequiredItems();
            bool ok = false;
            foreach (CrateSpec cs in prevL.Crates)
                if (cs.ForcedDrop.HasValue && Array.IndexOf(need, cs.ForcedDrop.Value) >= 0) ok = true;
            if (!ok) Issue("Vorgänger ohne Item", id);
        }
    }

    private static PlatformSpec Shifted(PlatformSpec p, float t)
    {
        PlatformSpec c = p.Clone();
        c.Center = p.Center + p.MoveOffset * t;
        c.MoveOffset = Vector3.Zero;
        return c;
    }
}
