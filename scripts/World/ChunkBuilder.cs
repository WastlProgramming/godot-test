using System;
using Godot;
using JumpRun.Game;
using JumpRun.Items;

namespace JumpRun.World;

/// <summary>Erzeugt deterministisch (Seed + Index) Chunk-Layouts und daraus Nodes.</summary>
public sealed class ChunkBuilder
{
    private readonly int _seed;

    public ChunkBuilder(int seed) { _seed = seed; }

    public static float DifficultyForIndex(int index) =>
        Mathf.Clamp(index * GameConstants.ChunkLength / GameConstants.DifficultyDistance, 0f, 1f);

    public static Random CreateRng(int seed, int index) => new(unchecked(seed * 7919 + index));

    // ------------------------------------------------------------------ Musterwahl

    public static PatternType ChoosePattern(Random rng, int index, float difficulty, PatternType? previous,
        int samePatternStreak, bool previousWasItemChunk)
    {
        if (index <= 0) return PatternType.Start;
        float d = Mathf.Clamp(difficulty, 0f, 1f);
        bool IsBlocked(PatternType p) => previous.HasValue && previous.Value == p && samePatternStreak >= 2;

        double roll = rng.NextDouble();
        bool itemAllowed = index >= 3 && !previousWasItemChunk;
        double itemChance = Mathf.Lerp(0.15f, 0.35f, d);
        double pick = rng.NextDouble();
        if (itemAllowed && roll < itemChance)
        {
            PatternType ip = pick < 0.5 ? PatternType.BigGap : PatternType.TallWall;
            if (IsBlocked(ip)) ip = ip == PatternType.BigGap ? PatternType.TallWall : PatternType.BigGap;
            return ip;
        }

        Span<PatternType> types = stackalloc PatternType[]
        {
            PatternType.GapJump, PatternType.Stairs, PatternType.HeightJump,
            PatternType.ZigZag, PatternType.MovingPlatforms, PatternType.NarrowBeams,
            PatternType.Pillars, PatternType.SpinningDiscs, PatternType.ShapeMix,
        };
        Span<float> weights = stackalloc float[]
        {
            3f - 2f * d, 3f - 2f * d, 2f,
            1f + 2f * d, 1f + 2f * d, 1f + 2f * d,
            1f + 1f * d, index < 4 ? 0f : 0.5f + 1.5f * d, 1.5f,
        };
        float total = 0f;
        for (int i = 0; i < types.Length; i++)
        {
            if (IsBlocked(types[i])) weights[i] = 0f;
            total += weights[i];
        }
        double r = pick * total;
        for (int i = 0; i < types.Length; i++)
        {
            r -= weights[i];
            if (r < 0) return types[i];
        }
        for (int i = types.Length - 1; i >= 0; i--)
            if (weights[i] > 0) return types[i];
        return PatternType.GapJump;
    }

    // ------------------------------------------------------------------ Build

    public Chunk Build(int index, Vector3 entryPoint, PatternType? previousPattern, int samePatternStreak,
        bool previousWasItemChunk, float difficulty)
    {
        ChunkLayout layout = Plan(index, entryPoint, previousPattern, samePatternStreak, previousWasItemChunk, difficulty);
        return Instantiate(layout);
    }

    public ChunkLayout Plan(int index, Vector3 entryPoint, PatternType? previousPattern, int samePatternStreak,
        bool previousWasItemChunk, float difficulty)
    {
        Random rng = CreateRng(_seed, index);
        PatternType pattern = ChoosePattern(rng, index, difficulty, previousPattern, samePatternStreak, previousWasItemChunk);

        // Nächstes Muster vorhersagen (gleicher deterministischer Algorithmus wie beim nächsten Build),
        // damit dieser Chunk eine Kiste mit dem dort benötigten Item enthält.
        int nextStreak = previousPattern.HasValue && previousPattern.Value == pattern ? samePatternStreak + 1 : 1;
        PatternType nextPattern = ChoosePattern(CreateRng(_seed, index + 1), index + 1, DifficultyForIndex(index + 1),
            pattern, nextStreak, pattern.IsItemRequired());

        var ctx = new Ctx(rng, entryPoint, Mathf.Clamp(difficulty, 0f, 1f))
        {
            Layout = new ChunkLayout { Index = index, Pattern = pattern, NextPattern = nextPattern, EntryPoint = entryPoint },
        };

        // Checkpoint-Pad (4 × 4 m) am Anfang jedes Chunks.
        ctx.Add(4f, 4f, 0f, 0f, 0f, MaterialNames.PlatformCheckpoint, record: false);
        ctx.Layout.CheckpointLocal = new Vector3(0f, 0f, -2f);

        switch (pattern)
        {
            case PatternType.Start: BuildStart(ctx); break;
            case PatternType.GapJump: BuildGapJump(ctx); break;
            case PatternType.Stairs: BuildStairs(ctx); break;
            case PatternType.ZigZag: BuildZigZag(ctx); break;
            case PatternType.MovingPlatforms: BuildMoving(ctx); break;
            case PatternType.NarrowBeams: BuildBeams(ctx); break;
            case PatternType.HeightJump: BuildHeight(ctx); break;
            case PatternType.BigGap: BuildBigGap(ctx); break;
            case PatternType.TallWall: BuildTallWall(ctx); break;
            case PatternType.Pillars: BuildPillars(ctx); break;
            case PatternType.SpinningDiscs: BuildSpinning(ctx); break;
            case PatternType.ShapeMix: BuildShapeMix(ctx); break;
        }

        if (!pattern.IsItemRequired() && pattern != PatternType.Start)
            ctx.AddLanding();

        PlaceRandomCrates(ctx, pattern);

        // Lösbarkeitsgarantie: der Chunk vor einem Item-Chunk hat eine Kiste mit dem benötigten Item.
        if (nextPattern.IsItemRequired())
        {
            ItemType need = nextPattern == PatternType.TallWall ? ItemType.Trampoline : ItemType.Bridge;
            ctx.Layout.Crates.Add(new CrateSpec
            {
                Position = new Vector3(1.3f, 0.5f, -3.2f),
                ForcedDrop = need,
                DropSeed = rng.Next(),
            });
        }

        ctx.Layout.ExitLocal = new Vector3(ctx.X, ctx.Y, ctx.Z);
        return ctx.Layout;
    }

    // ------------------------------------------------------------------ Muster

    private static void BuildStart(Ctx c)
    {
        // Großer flacher Startbereich 12 × 30 m mit 2 Tutorial-Kisten.
        c.Add(30f, 12f, 0f, 0f, 0f, MaterialNames.PlatformStart, record: false);
        c.Layout.Crates.Add(new CrateSpec { Position = new Vector3(-3f, 0.5f, -12f), ForcedDrop = ItemType.Bridge, DropSeed = c.Rng.Next() });
        c.Layout.Crates.Add(new CrateSpec { Position = new Vector3(3f, 0.5f, -20f), ForcedDrop = ItemType.Trampoline, DropSeed = c.Rng.Next() });
        c.Layout.Crates.Add(new CrateSpec { Position = new Vector3(0f, 0.5f, -26f), ForcedDrop = ItemType.Helicopter, DropSeed = c.Rng.Next() });
    }

    // ------------------------------------------------------------------ Formen (CONTRACT_V2 §4)

    private static PlatformShape Pick(Ctx c, params PlatformShape[] options) => options[c.Rng.Next(options.Length)];

    /// <summary>Faktor Innenkreis / Umkreisradius einer Form.</summary>
    private static float InnerFactor(PlatformShape s)
    {
        if (s.IsRound()) return 1f;
        if (s == PlatformShape.Diamond) return Mathf.Cos(Mathf.Pi / 4f);
        int n = s.PolygonSides();
        return n > 0 ? Mathf.Cos(Mathf.Pi / n) : 1f;
    }

    /// <summary>Mindest-Umkreisdurchmesser, damit InnerRadius ≥ MinWalkableInnerRadius (+2 cm Reserve).</summary>
    private static float MinDiameter(PlatformShape s) =>
        2f * (GameConstants.MinWalkableInnerRadius + 0.02f) / InnerFactor(s);

    /// <summary>Umkreisdurchmesser für einen gewünschten Innenkreisradius.</summary>
    private static float DiameterForInner(PlatformShape s, float inner) =>
        Mathf.Max(2f * inner / InnerFactor(s), MinDiameter(s));

    private static float RandomYaw(Ctx c, PlatformShape s) =>
        s.PolygonSides() > 0 ? c.Range(0f, Mathf.Tau) : 0f;

    /// <summary>Formplattform (Umkreisdurchmesser D, quadratische Bounding-Box) anhängen.</summary>
    private static PlatformSpec AddShaped(Ctx c, PlatformShape shape, float diameter, float x, float surfaceY, float gap,
        string material, float thickness, float spin = 0f)
    {
        if (shape == PlatformShape.Disc) thickness = Mathf.Min(thickness, 0.6f);
        if (shape == PlatformShape.Cylinder) thickness = Mathf.Max(thickness, 2f);
        float d = shape == PlatformShape.Box ? diameter : Mathf.Max(diameter, shape == PlatformShape.Square || shape == PlatformShape.Cross ? 0f : MinDiameter(shape));
        return c.Add(d, d, x, surfaceY, gap, material, thickness, shape: shape, rotationY: RandomYaw(c, shape), spin: spin);
    }

    /// <summary>Rampe (steigt in −Z) anhängen; Rise auf 0,1 m gerastert, Steigung ≤ 22° (&lt; MaxRampSlopeDeg).</summary>
    private static PlatformSpec AddRamp(Ctx c, float rise, float width, float x, float entryY, float gap, string material)
    {
        rise = Mathf.Max(0.3f, Mathf.Round(rise * 10f) / 10f);
        float len = Mathf.Max(4f, rise / Mathf.Tan(Mathf.DegToRad(22f)));
        len = Mathf.Ceil(len * 10f) / 10f;
        return c.Add(len, width, x, entryY, gap, material, rise + PlatformSpec.RampThickness, shape: PlatformShape.Ramp, rise: rise);
    }

    /// <summary>Höhenänderung, die zum Zielhöhenverlauf des Chunks tendiert.</summary>
    private static float BiasedDy(Ctx c, float down, float up)
    {
        if (c.Y > c.TargetDy + 2f) return c.Range(-up, down * 0.3f);
        if (c.Y < c.TargetDy - 2f) return c.Range(-down * 0.3f, up);
        return c.Range(-down, up);
    }

    private static void BuildPillars(Ctx c)
    {
        int n = c.Rng.Next(5, 9);
        float dia = Mathf.Lerp(3.2f, 2.6f, c.D);
        float gap = Mathf.Lerp(1.8f, 3.2f, c.D);
        for (int i = 0; i < n; i++)
        {
            PlatformShape shape = Pick(c, PlatformShape.Cylinder, PlatformShape.Hexagon, PlatformShape.Octagon);
            float dy = Mathf.Clamp(BiasedDy(c, 1.0f, 1.2f), -GameConstants.MaxSafeStep, GameConstants.MaxSafeStep);
            float h = c.Range(3f, 8f);
            float g = gap * c.Range(0.85f, 1f);
            AddShaped(c, shape, Mathf.Max(dia, MinDiameter(shape)), c.PathX(i + 1, n + 1), c.ClampY(c.Y + dy), g,
                MaterialNames.PlatformPillars, h);
        }
    }

    private static void BuildSpinning(Ctx c)
    {
        int n = c.Rng.Next(3, 6);
        float inner = Mathf.Lerp(1.8f, 1.4f, c.D);
        float gap = Mathf.Lerp(2.0f, 3.0f, c.D);
        float speed = Mathf.Min(Mathf.Lerp(0.4f, 0.9f, c.D), GameConstants.RotatingMaxAngularSpeed);
        for (int i = 0; i < n; i++)
        {
            PlatformShape shape = Pick(c, PlatformShape.Disc, PlatformShape.Disc, PlatformShape.Hexagon, PlatformShape.Triangle);
            float r = shape == PlatformShape.Triangle ? Mathf.Min(inner, 1.5f) : inner;
            float sign = c.Rng.NextDouble() < 0.5 ? -1f : 1f;
            float dy = c.Range(-0.4f, 0.4f);
            AddShaped(c, shape, DiameterForInner(shape, r), c.PathX(i + 1, n + 1), c.ClampY(c.Y + dy), gap * c.Range(0.85f, 1f),
                MaterialNames.PlatformSpin, 0.6f, spin: sign * speed * c.Range(0.85f, 1f));
        }
    }

    private static void BuildShapeMix(Ctx c)
    {
        int n = c.Rng.Next(4, 8);
        float gap = Mathf.Lerp(2.0f, 3.8f, c.D);
        string m = MaterialNames.PlatformShapes;
        for (int i = 0; i < n; i++)
        {
            var shape = (PlatformShape)c.Rng.Next(PlatformShapeExtensions.Count);
            float x = c.PathX(i + 1, n + 1);
            float g = gap * c.Range(0.85f, 1f);
            if (shape == PlatformShape.Ramp)
            {
                float rise = c.Range(0.6f, 1.4f);
                if (c.Y + rise > c.TargetDy + 3f || c.Entry.Y + c.Y + rise > GameConstants.MaxWorldY - 3f)
                    shape = PlatformShape.Disc; // zu hoch → flache Scheibe statt Rampe
                else
                {
                    AddRamp(c, rise, c.Range(3f, 4f), x, c.ClampY(c.Y + c.Range(-0.3f, 0.3f)), g, m);
                    continue;
                }
            }
            float y = c.ClampY(c.Y + BiasedDy(c, 0.8f, 1.0f));
            switch (shape)
            {
                case PlatformShape.Box:
                    c.Add(c.Range(3f, 5f), c.Range(3f, 4.5f), x, y, g, m);
                    break;
                case PlatformShape.Square: AddShaped(c, shape, c.Range(3f, 4f), x, y, g, m, 1f); break;
                case PlatformShape.Diamond: AddShaped(c, shape, c.Range(3.6f, 4.4f), x, y, g, m, 1f); break;
                case PlatformShape.Cylinder: AddShaped(c, shape, c.Range(3f, 3.8f), x, y, g, m, c.Range(2.5f, 5f)); break;
                case PlatformShape.Disc: AddShaped(c, shape, c.Range(3f, 3.8f), x, y, g, m, 0.6f); break;
                case PlatformShape.Triangle: AddShaped(c, shape, c.Range(4.9f, 5.6f), x, y, g, m, 1f); break;
                case PlatformShape.Pentagon: AddShaped(c, shape, c.Range(3.2f, 4f), x, y, g, m, 1f); break;
                case PlatformShape.Hexagon:
                case PlatformShape.Octagon: AddShaped(c, shape, c.Range(3f, 3.8f), x, y, g, m, 1f); break;
                case PlatformShape.Cross: AddShaped(c, shape, c.Range(5f, 6f), x, y, g, m, 1f); break;
            }
        }
    }

    private static void BuildGapJump(Ctx c)
    {
        int n = c.Rng.Next(4, 7);
        float gap = Mathf.Lerp(2.5f, 5.5f, c.D);
        float len = Mathf.Lerp(5f, 2.5f, c.D);
        float width = Mathf.Lerp(4f, 3f, c.D);
        PlatformShape shape = Pick(c, PlatformShape.Box, PlatformShape.Box, PlatformShape.Square, PlatformShape.Disc,
            PlatformShape.Hexagon, PlatformShape.Octagon, PlatformShape.Triangle);
        for (int i = 0; i < n; i++)
        {
            float g = Mathf.Min(gap * c.Range(0.85f, 1f), GameConstants.MaxSafeGap);
            float y = c.Y + c.Range(-0.5f, 0.5f);
            if (shape == PlatformShape.Box)
                c.Add(len, width, c.PathX(i + 1, n + 1), c.ClampY(y), g, MaterialNames.PlatformGap);
            else
                AddShaped(c, shape, Mathf.Max(len, width), c.PathX(i + 1, n + 1), c.ClampY(y), g, MaterialNames.PlatformGap,
                    shape == PlatformShape.Disc ? 0.6f : 1f);
        }
    }

    private static void BuildStairs(Ctx c)
    {
        int m = c.Rng.Next(2, 4);
        float h = Mathf.Lerp(1.0f, 1.8f, c.D);
        float spacing = Mathf.Lerp(1.5f, 3.0f, c.D);
        bool upFirst = c.Rng.NextDouble() < 0.6;
        float globalY = c.Entry.Y + c.Y;
        if (globalY + m * h > GameConstants.MaxWorldY - 2f) upFirst = false;
        if (globalY - m * h < GameConstants.MinWorldY + 2f) upFirst = true;
        int total = m * 2 + 1;
        int k = 0;
        float s = upFirst ? 1f : -1f;
        PlatformShape shape = Pick(c, PlatformShape.Box, PlatformShape.Box, PlatformShape.Square, PlatformShape.Cylinder, PlatformShape.Ramp);
        string mat = MaterialNames.PlatformStairs;
        void Step(float dir)
        {
            float x = c.PathX(++k, total + 1);
            switch (shape)
            {
                case PlatformShape.Ramp when dir > 0f:
                    AddRamp(c, h, 4f, x, c.Y, spacing, mat); // Stufe als Rampe hinauf
                    break;
                case PlatformShape.Square:
                    AddShaped(c, shape, 3.5f, x, c.ClampY(c.Y + dir * h), spacing, mat, 1f);
                    break;
                case PlatformShape.Cylinder:
                    AddShaped(c, shape, 3.6f, x, c.ClampY(c.Y + dir * h), spacing, mat, 3f);
                    break;
                default:
                    c.Add(3f, 4f, x, c.ClampY(c.Y + dir * h), spacing, mat);
                    break;
            }
        }
        for (int i = 0; i < m; i++) Step(s);
        c.Add(4f, 4f, c.PathX(++k, total + 1), c.Y, spacing, mat);
        for (int i = 0; i < m; i++) Step(-s);
    }

    private static void BuildZigZag(Ctx c)
    {
        int n = c.Rng.Next(5, 8);
        float o = Mathf.Lerp(3f, 6f, c.D);
        float gap = Mathf.Lerp(2f, 4f, c.D);
        PlatformShape shape = Pick(c, PlatformShape.Box, PlatformShape.Disc, PlatformShape.Hexagon, PlatformShape.Diamond, PlatformShape.Square);
        for (int i = 0; i < n; i++)
        {
            float side = i % 2 == 0 ? -0.5f : 0.5f;
            float x = c.PathX(i + 1, n + 1) + side * o;
            switch (shape)
            {
                case PlatformShape.Box: c.Add(3f, 3f, x, c.Y, gap, MaterialNames.PlatformZigZag); break;
                case PlatformShape.Disc: AddShaped(c, shape, 3.2f, x, c.Y, gap, MaterialNames.PlatformZigZag, 0.6f); break;
                case PlatformShape.Hexagon: AddShaped(c, shape, 3.2f, x, c.Y, gap, MaterialNames.PlatformZigZag, 1f); break;
                case PlatformShape.Diamond: AddShaped(c, shape, 3.6f, x, c.Y, gap, MaterialNames.PlatformZigZag, 1f); break;
                default: AddShaped(c, shape, 3f, x, c.Y, gap, MaterialNames.PlatformZigZag, 1f); break;
            }
        }
    }

    private static void BuildMoving(Ctx c)
    {
        int n = c.Rng.Next(2, 5);
        float speed = Mathf.Lerp(1.5f, 3.5f, c.D);
        float size = Mathf.Lerp(4f, 2.5f, c.D);
        float gap = Mathf.Lerp(2.0f, 3.0f, c.D);
        PlatformShape shape = Pick(c, PlatformShape.Box, PlatformShape.Box, PlatformShape.Disc, PlatformShape.Hexagon);
        for (int i = 0; i < n; i++)
        {
            float x = c.PathX(i + 1, n + 1);
            PlatformSpec p = shape == PlatformShape.Box
                ? c.Add(size, size, x, c.Y, gap, MaterialNames.PlatformMoving, thickness: 0.6f)
                : c.Add(Mathf.Max(size, MinDiameter(shape)), Mathf.Max(size, MinDiameter(shape)), x, c.Y, gap,
                    MaterialNames.PlatformMoving, 0.6f, shape: shape, rotationY: RandomYaw(c, shape), exactPlacement: false);
            float phase = c.Range(0f, Mathf.Tau);
            if (c.Rng.NextDouble() < 0.5)
            {
                const float a = 2.5f;
                p.Center.X -= a;
                p.MoveOffset = new Vector3(2f * a, 0f, 0f);
            }
            else
            {
                p.Center.Y -= 1.5f;
                p.MoveOffset = new Vector3(0f, 3f, 0f);
            }
            p.MoveSpeed = speed;
            p.Phase = phase;
        }
    }

    private static void BuildBeams(Ctx c)
    {
        int n = c.Rng.Next(3, 5);
        float width = Mathf.Lerp(1.0f, 0.5f, c.D);
        float len = Mathf.Lerp(8f, 5f, c.D);
        float maxGap = Mathf.Lerp(1.0f, 2.5f, c.D);
        for (int i = 0; i < n; i++)
        {
            float g = i == 0 ? 1.5f : (c.Rng.NextDouble() < 0.5 ? 0f : c.Range(0.5f, maxGap));
            c.Add(len, width, c.PathX(i + 1, n + 1), c.Y, g, MaterialNames.PlatformBeam, thickness: 0.5f);
        }
    }

    private static void BuildHeight(Ctx c)
    {
        int n = c.Rng.Next(3, 5);
        float h = Mathf.Min(Mathf.Lerp(1.2f, 2.0f, c.D), GameConstants.MaxSafeStep);
        float gap = Mathf.Lerp(1.5f, 2.5f, c.D);
        float startY = c.Y;
        if (c.Entry.Y + c.Y + n * h > GameConstants.MaxWorldY - 2f)
            startY = c.Y - n * h; // zuerst hinunter, dann klettern
        PlatformShape shape = Pick(c, PlatformShape.Box, PlatformShape.Box, PlatformShape.Cylinder, PlatformShape.Hexagon, PlatformShape.Octagon);
        void Pillar(int i, float y)
        {
            if (shape == PlatformShape.Box) c.Add(3.5f, 3.5f, c.PathX(i, n + 2), y, gap, MaterialNames.PlatformHeight);
            else AddShaped(c, shape, 3.5f, c.PathX(i, n + 2), y, gap, MaterialNames.PlatformHeight, 3f);
        }
        Pillar(1, c.ClampY(startY));
        for (int i = 0; i < n; i++)
            Pillar(i + 2, c.ClampY(c.Y + h));
    }

    private static void BuildBigGap(Ctx c)
    {
        // Pad → Supply (2+ Kisten) → Riesenlücke mit Anker → Landeplattform
        c.Add(8f, 6f, 0f, c.Y, 0f, MaterialNames.PlatformSupply, record: false);
        float supplyZ = c.Z + 4f;
        bool bridgeFirst = c.Layout.Index % 2 == 0;
        ItemType a = bridgeFirst ? ItemType.Bridge : ItemType.GrappleHook;
        ItemType b = bridgeFirst ? ItemType.GrappleHook : ItemType.Bridge;
        AddSupply(c, new Vector3(-2f, c.Y + 0.5f, supplyZ + 1.5f), a);
        AddSupply(c, new Vector3(2f, c.Y + 0.5f, supplyZ + 1.5f), b);
        AddSupply(c, new Vector3(-2f, c.Y + 0.5f, supplyZ - 1.5f), a);

        // 10–13,5 m: weit über der Sprungweite (≤ 6 m), aber mit ≥ 2,5 m Reserve zur Brückenlänge (F-08 / R-03).
        float gap = Mathf.Min(Mathf.Lerp(10f, 12f, c.D) + c.Range(0f, 1.5f), GameConstants.BigGapMax);
        c.Layout.ItemObstacleSize = gap;
        float anchorZ = c.Z - gap * 0.5f;
        float anchorY = c.Y + c.Range(6f, 8f);
        c.Layout.Anchors.Add(new Vector3(c.X, anchorY, anchorZ));
        c.Add(14f, 6f, c.X, c.Y, gap, MaterialNames.PlatformBigGap, record: false);
    }

    private static void BuildTallWall(Ctx c)
    {
        // Pad → Supply → Boden mit Wand (5,5–7 m hoch, 3 m tief, breiter als der Weg) → Landefläche
        c.Add(10f, 6f, 0f, c.Y, 0f, MaterialNames.PlatformSupply, record: false);
        float supplyZ = c.Z + 5f;
        AddSupply(c, new Vector3(-2f, c.Y + 0.5f, supplyZ + 2f), ItemType.Trampoline);
        AddSupply(c, new Vector3(2f, c.Y + 0.5f, supplyZ + 2f), ItemType.Trampoline);
        if (c.D > 0.5f)
            AddSupply(c, new Vector3(-2f, c.Y + 0.5f, supplyZ - 2f), ItemType.GrappleHook);

        float floorStart = c.Z;
        c.Add(15f, 6f, 0f, c.Y, 0f, MaterialNames.PlatformBigGap, record: false);
        float wallH = c.Range(5.5f, 7f);
        c.Layout.ItemObstacleSize = wallH;
        float wallZ = floorStart - 4f - 1.5f;
        c.Layout.Platforms.Add(new PlatformSpec
        {
            Center = new Vector3(0f, c.Y + wallH * 0.5f, wallZ),
            Size = new Vector3(14f, wallH, 3f),
            Material = MaterialNames.PlatformWall,
        });
        if (c.D > 0.5f)
            c.Layout.Anchors.Add(new Vector3(0f, c.Y + wallH + 4f, wallZ));
    }

    private static void AddSupply(Ctx c, Vector3 pos, ItemType item)
    {
        c.Layout.Crates.Add(new CrateSpec { Position = pos, ForcedDrop = item, IsSupply = true, DropSeed = c.Rng.Next() });
    }

    private static void PlaceRandomCrates(Ctx c, PatternType pattern)
    {
        if (pattern is PatternType.Start or PatternType.BigGap or PatternType.TallWall) return;
        int count = c.Rng.Next(0, 4);
        int platforms = c.Layout.Platforms.Count;
        for (int i = 0; i < count; i++)
        {
            // Kandidat: statische, ausreichend große Plattform (nicht das Pad, Index 0).
            for (int attempt = 0; attempt < 6; attempt++)
            {
                int pi = c.Rng.Next(1, Math.Max(2, platforms));
                if (pi >= platforms) break;
                PlatformSpec p = c.Layout.Platforms[pi];
                if (p.IsMoving || p.IsRotating || p.Shape == PlatformShape.Ramp || p.Size.X < 2.5f || p.Size.Z < 2.5f) continue;
                float side = c.Rng.NextDouble() < 0.5 ? -1f : 1f;
                float off = p.IsRoundOrPolygon || Mathf.Abs(p.RotationY) > 0.0001f
                    ? Mathf.Max(0f, p.InnerRadius - 0.7f)
                    : p.Size.X * 0.5f - 0.7f;
                var pos = new Vector3(p.Center.X + side * off, p.TopY + 0.5f, p.Center.Z);
                if (!ChunkLayout.SupportsPoint(p, pos, 0.3f)) continue;
                bool clash = false;
                foreach (CrateSpec cs in c.Layout.Crates)
                    if (cs.Position.DistanceTo(pos) < 1.2f) clash = true;
                if (clash) continue;
                c.Layout.Crates.Add(new CrateSpec { Position = pos, DropSeed = c.Rng.Next() });
                break;
            }
        }
    }

    // ------------------------------------------------------------------ Nodes

    public static Chunk Instantiate(ChunkLayout layout)
    {
        var chunk = new Chunk
        {
            Name = $"Chunk_{layout.Index}",
            Position = layout.EntryPoint,
        };
        chunk.Init(layout);

        int i = 0;
        foreach (PlatformSpec p in layout.Platforms)
        {
            Node3D body = p.IsMoving ? CreateMoving(p) : p.IsRotating ? CreateRotating(p) : CreateStatic(p);
            body.Name = $"Platform_{i++}";
            chunk.AddChild(body);
        }

        // Checkpoint
        PackedScene? cpScene = LoadScene(CheckpointScenePath);
        if (cpScene != null && cpScene.Instantiate() is Checkpoint cp)
        {
            cp.Name = "Checkpoint";
            cp.ChunkIndex = layout.Index;
            cp.Position = layout.CheckpointLocal;
            chunk.AddChild(cp);
        }

        // Anker
        PackedScene? anchorScene = LoadScene(AnchorScenePath);
        int a = 0;
        foreach (Vector3 an in layout.Anchors)
        {
            if (anchorScene?.Instantiate() is GrappleAnchor ga)
            {
                ga.Name = $"Anchor_{a++}";
                ga.Position = an;
                chunk.AddChild(ga);
            }
        }

        foreach (CrateSpec cs in layout.Crates)
            chunk.SpawnCrate(cs);

        return chunk;
    }

    private const string CheckpointScenePath = "res://scenes/objects/Checkpoint.tscn";
    private const string AnchorScenePath = "res://scenes/objects/GrappleAnchor.tscn";
    private static readonly System.Collections.Generic.Dictionary<string, PackedScene?> SceneCache = new();

    internal static PackedScene? LoadScene(string path)
    {
        if (SceneCache.TryGetValue(path, out PackedScene? s) && (s == null || GodotObject.IsInstanceValid(s)))
            return s;
        s = ResourceLoader.Exists(path) ? ResourceLoader.Load<PackedScene>(path) : null;
        if (s == null) GD.PushWarning($"[ChunkBuilder] Szene fehlt: {path}");
        SceneCache[path] = s;
        return s;
    }

    private static readonly System.Collections.Generic.Dictionary<string, BoxMesh> UnitMeshCache = new();
    private static readonly System.Collections.Generic.Dictionary<string, ArrayMesh> ShapeMeshCache = new();
    private static readonly System.Collections.Generic.Dictionary<string, ConvexPolygonShape3D> HullCache = new();

    private static BoxMesh UnitBox(string material)
    {
        if (UnitMeshCache.TryGetValue(material, out BoxMesh? m) && GodotObject.IsInstanceValid(m)) return m;
        m = new BoxMesh { Size = Vector3.One, Material = MaterialLibrary.Get(material) };
        UnitMeshCache[material] = m;
        return m;
    }

    /// <summary>Dickenanteil der Rampe am Einstieg (Dicke / Size.Y), auf 1/1000 gerastert (Cache-Schlüssel).</summary>
    private static int RampKey(PlatformSpec p) =>
        Mathf.RoundToInt(PlatformSpec.RampThickness / Mathf.Max(p.Size.Y, 0.01f) * 1000f);

    /// <summary>Gecachtes Einheits-Mesh je (Form, Material[, Rampenverhältnis]).</summary>
    internal static ArrayMesh UnitShape(PlatformShape shape, string material, int rampKey = 1000)
    {
        string key = shape == PlatformShape.Ramp ? $"{shape}|{rampKey}|{material}" : $"{shape}|{material}";
        if (ShapeMeshCache.TryGetValue(key, out ArrayMesh? m) && GodotObject.IsInstanceValid(m)) return m;
        m = ShapeMeshFactory.Build(shape, rampKey / 1000f);
        m.SurfaceSetMaterial(0, MaterialLibrary.GetShapeVariant(material));
        ShapeMeshCache[key] = m;
        return m;
    }

    private static ConvexPolygonShape3D Hull(PlatformSpec p)
    {
        int rk = p.Shape == PlatformShape.Ramp ? RampKey(p) : 1000;
        string key = $"{p.Shape}|{Mathf.RoundToInt(p.Size.X * 100f)}|{Mathf.RoundToInt(p.Size.Y * 100f)}|{Mathf.RoundToInt(p.Size.Z * 100f)}|{rk}";
        if (HullCache.TryGetValue(key, out ConvexPolygonShape3D? h) && GodotObject.IsInstanceValid(h)) return h;
        Vector3[] unit = ShapeMeshFactory.UnitHullPoints(p.Shape, rk / 1000f);
        var pts = new Vector3[unit.Length];
        for (int i = 0; i < unit.Length; i++) pts[i] = unit[i] * p.Size;
        h = new ConvexPolygonShape3D { Points = pts };
        HullCache[key] = h;
        return h;
    }

    /// <summary>Szenen- und Mesh-Cache beim Beenden leeren (F-10 / R-15).</summary>
    internal static void ClearSceneCache()
    {
        SceneCache.Clear();
        UnitMeshCache.Clear();
        ShapeMeshCache.Clear();
        HullCache.Clear();
    }

    private static StaticBody3D CreateStatic(PlatformSpec p)
    {
        var body = new StaticBody3D
        {
            Position = p.Center,
            CollisionLayer = GameConstants.LayerWorld,
            CollisionMask = 0,
        };
        AddShapeAndMesh(body, p);
        return body;
    }

    private static MovingPlatform CreateMoving(PlatformSpec p)
    {
        var body = new MovingPlatform
        {
            Position = p.Center,
            CollisionLayer = GameConstants.LayerWorld,
            CollisionMask = 0,
            Offset = p.MoveOffset,
            Speed = p.MoveSpeed,
            PhaseOffset = p.Phase,
        };
        AddShapeAndMesh(body, p);
        return body;
    }

    private static RotatingPlatform CreateRotating(PlatformSpec p)
    {
        var body = new RotatingPlatform
        {
            Position = p.Center,
            CollisionLayer = GameConstants.LayerWorld,
            CollisionMask = 0,
            AngularSpeed = p.SpinSpeed,
        };
        AddShapeAndMesh(body, p);
        return body;
    }

    private static void AddShapeAndMesh(Node3D body, PlatformSpec p)
    {
        if (Mathf.Abs(p.RotationY) > 0.0001f) body.Rotation = new Vector3(0f, p.RotationY, 0f);
        if (p.Shape != PlatformShape.Box)
        {
            AddShapedMeshAndCollision(body, p);
            return;
        }
        // R-10: ein gemeinsames 1-m-BoxMesh je Material, skaliert per Node (Shader rechnet in Weltkoordinaten
        // bzw. mit fwidth → Kanten bleiben pixelgenau). Spart je Plattform ein Mesh und erlaubt GPU-Batching.
        // Die Kollision behält eine eigene BoxShape3D (nicht-uniforme Skalierung von Shapes ist unzuverlässig).
        var mesh = new MeshInstance3D
        {
            Name = "Mesh",
            Mesh = UnitBox(p.Material),
            Scale = p.Size,
        };
        var shape = new CollisionShape3D { Name = "Shape", Shape = new BoxShape3D { Size = p.Size } };
        body.AddChild(mesh);
        body.AddChild(shape);
    }

    private static void AddShapedMeshAndCollision(Node3D body, PlatformSpec p)
    {
        var mesh = new MeshInstance3D
        {
            Name = "Mesh",
            Mesh = UnitShape(p.Shape, p.Material, p.Shape == PlatformShape.Ramp ? RampKey(p) : 1000),
            Scale = p.Size,
        };
        body.AddChild(mesh);
        switch (p.Shape)
        {
            case PlatformShape.Square:
                body.AddChild(new CollisionShape3D { Name = "Shape", Shape = new BoxShape3D { Size = p.Size } });
                break;
            case PlatformShape.Diamond:
            {
                float side = p.Size.X * Mathf.Sqrt2 * 0.5f;
                body.AddChild(new CollisionShape3D
                {
                    Name = "Shape",
                    Shape = new BoxShape3D { Size = new Vector3(side, p.Size.Y, p.Size.Z * Mathf.Sqrt2 * 0.5f) },
                    Rotation = new Vector3(0f, Mathf.Pi / 4f, 0f),
                });
                break;
            }
            case PlatformShape.Cylinder:
            case PlatformShape.Disc:
                body.AddChild(new CollisionShape3D
                {
                    Name = "Shape",
                    Shape = new CylinderShape3D { Radius = p.Size.X * 0.5f, Height = p.Size.Y },
                });
                break;
            case PlatformShape.Cross:
            {
                float r = PlatformShapeExtensions.CrossArmRatio;
                body.AddChild(new CollisionShape3D
                {
                    Name = "Shape",
                    Shape = new BoxShape3D { Size = new Vector3(p.Size.X, p.Size.Y, p.Size.Z * r) },
                });
                body.AddChild(new CollisionShape3D
                {
                    Name = "Shape2",
                    Shape = new BoxShape3D { Size = new Vector3(p.Size.X * r, p.Size.Y, p.Size.Z) },
                });
                break;
            }
            default: // Polygone, Ramp
                body.AddChild(new CollisionShape3D { Name = "Shape", Shape = Hull(p) });
                break;
        }
    }

    // ------------------------------------------------------------------ Hilfskontext

    private sealed class Ctx
    {
        public readonly Random Rng;
        public readonly Vector3 Entry;
        public readonly float D;
        public ChunkLayout Layout = null!;
        public float X, Y, Z;               // lokaler Cursor: Vorderkante der letzten Plattform, Oberfläche
        public readonly float TargetDx, TargetDy;

        public Ctx(Random rng, Vector3 entry, float d)
        {
            Rng = rng; Entry = entry; D = d;
            float dx = Range(-3f, 3f) - entry.X * 0.15f;
            float maxX = GameConstants.MaxLateralOffset - 4f;
            TargetDx = Mathf.Clamp(entry.X + Mathf.Clamp(dx, -3f, 3f), -maxX, maxX) - entry.X;
            float dy = Range(-3f, 3f) + (5f - entry.Y) * 0.1f;
            float gy = Mathf.Clamp(entry.Y + Mathf.Clamp(dy, -3f, 3f), GameConstants.MinWorldY + 3f, GameConstants.MaxWorldY - 8f);
            TargetDy = gy - entry.Y;
        }

        public float Range(float a, float b) => a + (float)Rng.NextDouble() * (b - a);

        public float PathX(int i, int n) => TargetDx * Mathf.Clamp(i / (float)n, 0f, 1f);

        public float ClampY(float localY) =>
            Mathf.Clamp(localY, GameConstants.MinWorldY - Entry.Y, GameConstants.MaxWorldY - Entry.Y);

        /// <summary>
        /// Plattform anhängen: Start bei Cursor-Z minus Lücke, Länge entlang -Z. <paramref name="surfaceY"/> ist die
        /// Einstiegsoberfläche (Ramp: unteres +Z-Ende, Size.Y = Rise + Dicke).
        /// Ist eine der beiden Plattformen keine Box, wird so platziert, dass die begehbare Lücke
        /// (<see cref="ChunkLayout.WalkableGap"/>) höchstens <paramref name="gap"/> beträgt (Bounding-Boxen
        /// überlappen in Z nie). Box→Box bleibt exakt wie v1.
        /// </summary>
        public PlatformSpec Add(float length, float width, float x, float surfaceY, float gap, string material,
            float thickness = 1f, bool record = true, PlatformShape shape = PlatformShape.Box, float rotationY = 0f,
            float spin = 0f, float rise = 0f, bool exactPlacement = true)
        {
            PlatformSpec? prev = Layout.Platforms.Count > 0 ? Layout.Platforms[^1] : null;
            var p = new PlatformSpec
            {
                Size = new Vector3(width, thickness, length),
                Material = material,
                Shape = shape,
                RotationY = rotationY,
                SpinSpeed = Mathf.Clamp(spin, -GameConstants.RotatingMaxAngularSpeed, GameConstants.RotatingMaxAngularSpeed),
                Rise = shape == PlatformShape.Ramp ? rise : 0f,
            };
            float cy = surfaceY + p.Rise - thickness * 0.5f;
            float zGap = gap;
            bool shaped = prev != null && (prev.Shape != PlatformShape.Box || shape != PlatformShape.Box || prev.IsRotating);
            if (shaped && exactPlacement && !prev!.IsMoving)
            {
                float Walk(float zg)
                {
                    p.Center = new Vector3(x, cy, Z - zg - length * 0.5f);
                    return ChunkLayout.WalkableGap(prev, p);
                }
                if (Walk(gap) > gap + 0.001f)
                {
                    if (Walk(0f) >= gap)
                    {
                        zGap = 0f;
                        // R V2-13: Ist selbst ohne Z-Abstand die begehbare Lücke > MaxSafeGap (z. B. schmale Form
                        // weit seitlich versetzt), X so weit zur Vorgängerplattform ziehen, bis sie wieder passt.
                        if (Walk(0f) > GameConstants.MaxSafeGap)
                        {
                            float x0 = x, lo = 0f, hi = 1f;
                            for (int it = 0; it < 22; it++)
                            {
                                float mid = 0.5f * (lo + hi);
                                x = Mathf.Lerp(x0, prev.Center.X, mid);
                                if (Walk(0f) > GameConstants.MaxSafeGap) lo = mid; else hi = mid;
                            }
                            x = Mathf.Lerp(x0, prev.Center.X, hi);
                        }
                    }
                    else
                    {
                        float lo = 0f, hi = gap;
                        for (int it = 0; it < 22; it++)
                        {
                            float mid = 0.5f * (lo + hi);
                            if (Walk(mid) <= gap) lo = mid; else hi = mid;
                        }
                        zGap = lo;
                    }
                }
            }
            float startZ = Z - zGap;
            p.Center = new Vector3(x, cy, startZ - length * 0.5f);
            if (record && prev != null)
            {
                Layout.NormalGaps.Add(shaped ? Mathf.Max(gap, ChunkLayout.WalkableGap(prev, p)) : gap);
                float step = surfaceY - Y;
                if (step > 0f) Layout.NormalSteps.Add(step);
            }
            Layout.Platforms.Add(p);
            X = x; Y = p.TopY; Z = startZ - length;
            return p;
        }

        public void AddLanding()
        {
            float y = Mathf.Min(TargetDy, Y + 1.5f);
            y = ClampY(y);
            Add(4f, 4f, TargetDx, y, Mathf.Lerp(2f, 3f, D), MaterialNames.ForPattern(Layout.Pattern));
        }
    }
}
