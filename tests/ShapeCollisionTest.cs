using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using JumpRun.Game;
using JumpRun.Items;
using JumpRun.Player;
using JumpRun.World;

namespace JumpRun.Tests;

/// <summary>
/// G4: Kollision passt zur Darstellung. Setzt den echten Spieler über jede Plattformform (inkl. Ramp, Cross,
/// drehende Disc) → nach ≤ 2 s IsOnFloor() und Fuß-Y = TopY ±0,1 m (Ramp: Oberfläche an der XZ-Position).
/// Zusätzlich: Spieler bleibt 5 s auf einer drehenden Disc; Kollisionstyp je Form wie CONTRACT_V2 §4.3.
/// Start: godot --headless --fixed-fps 60 --path . res://tests/ShapeCollisionTest.tscn → SHAPE_COLLISION_TEST_PASS
/// </summary>
public partial class ShapeCollisionTest : Node3D
{
    private Node3D _chunks = null!;
    private PlayerController _p = null!;
    private readonly List<string> _fail = new();
    private int _cases;

    public override void _Ready()
    {
        GameManager.Current.PersistenceEnabled = false;
        GameManager.Current.StartNewRun(7);
        var world = new Node3D { Name = "WorldRoot" };
        AddChild(world);
        _chunks = new Node3D { Name = "Chunks" };
        world.AddChild(_chunks);
        var placeables = new Node3D { Name = "Placeables" };
        world.AddChild(placeables);
        placeables.AddToGroup(Placeables.RootGroup);
        _p = ResourceLoader.Load<PackedScene>("res://scenes/Player.tscn").Instantiate<PlayerController>();
        AddChild(_p);
        Run();
    }

    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    private async Task Frames(int n) { for (int i = 0; i < n; i++) await Frame(); }

    private void Fail(string s) { _fail.Add(s); GD.Print("[ShapeCollision] FAIL " + s); }

    private static PlatformSpec Make(PlatformShape s)
    {
        var p = new PlatformSpec { Shape = s, Material = MaterialNames.PlatformShapes };
        switch (s)
        {
            case PlatformShape.Box: p.Size = new Vector3(4f, 1f, 5f); break;
            case PlatformShape.Square: p.Size = new Vector3(3.5f, 1f, 3.5f); break;
            case PlatformShape.Diamond: p.Size = new Vector3(4f, 1f, 4f); break;
            case PlatformShape.Cylinder: p.Size = new Vector3(3.4f, 3f, 3.4f); break;
            case PlatformShape.Disc: p.Size = new Vector3(3.4f, 0.6f, 3.4f); break;
            case PlatformShape.Triangle: p.Size = new Vector3(5.2f, 1f, 5.2f); p.RotationY = 0.7f; break;
            case PlatformShape.Pentagon: p.Size = new Vector3(3.6f, 1f, 3.6f); p.RotationY = 1.1f; break;
            case PlatformShape.Hexagon: p.Size = new Vector3(3.4f, 1f, 3.4f); break;
            case PlatformShape.Octagon: p.Size = new Vector3(3.2f, 1.5f, 3.2f); break;
            case PlatformShape.Ramp: p.Rise = 1.4f; p.Size = new Vector3(4f, 1.4f + PlatformSpec.RampThickness, 4.5f); break;
            case PlatformShape.Cross: p.Size = new Vector3(5.5f, 1f, 5.5f); break;
        }
        p.Center = new Vector3(0f, 2f - p.Size.Y * 0.5f, -20f); // Oberfläche (TopY) bei y = 2
        return p;
    }

    private async Task<Chunk> Spawn(PlatformSpec p)
    {
        foreach (Node n in _chunks.GetChildren()) n.QueueFree();
        await Frames(2);
        var l = new ChunkLayout { Index = 1, Pattern = PatternType.ShapeMix, EntryPoint = Vector3.Zero, CheckpointLocal = new Vector3(0f, -50f, 0f) };
        l.Platforms.Add(p);
        Chunk c = ChunkBuilder.Instantiate(l);
        _chunks.AddChild(c);
        await Frames(2);
        return c;
    }

    private async Task Drop(Vector3 pos)
    {
        _p.StopGrapple(false);
        _p.StopFlight();
        _p.GlobalPosition = pos;
        _p.Velocity = Vector3.Zero;
        _p.SetCheckpoint(new Vector3(0f, -80f, 0f), 0);
        await Frame();
    }

    private async Task<(bool OnFloor, float Y)> Settle(float maxSeconds)
    {
        int frames = Mathf.RoundToInt(maxSeconds * 60f);
        int onFloorFrames = 0;
        for (int i = 0; i < frames; i++)
        {
            await Frame();
            onFloorFrames = _p.IsOnFloor() ? onFloorFrames + 1 : 0;
            if (onFloorFrames >= 10) break;
        }
        return (_p.IsOnFloor(), _p.GlobalPosition.Y);
    }

    private static string ExpectedCollision(PlatformShape s) => s switch
    {
        PlatformShape.Box or PlatformShape.Square or PlatformShape.Diamond or PlatformShape.Cross => nameof(BoxShape3D),
        PlatformShape.Cylinder or PlatformShape.Disc => nameof(CylinderShape3D),
        _ => nameof(ConvexPolygonShape3D),
    };

    private async void Run()
    {
        try
        {
            for (int s = 0; s < PlatformShapeExtensions.Count; s++)
            {
                var shape = (PlatformShape)s;
                PlatformSpec spec = Make(shape);
                Chunk c = await Spawn(spec);
                Node3D? body = c.GetNodeOrNull<Node3D>("Platform_0");
                if (body == null) { Fail($"{shape}: Platform_0 fehlt"); continue; }
                int shapes = 0;
                foreach (Node n in body.GetChildren())
                {
                    if (n is not CollisionShape3D cs) continue;
                    shapes++;
                    if (cs.Shape?.GetType().Name != ExpectedCollision(shape)) Fail($"{shape}: Kollision {cs.Shape?.GetType().Name} statt {ExpectedCollision(shape)}");
                    if (cs.Shape is ConcavePolygonShape3D) Fail($"{shape}: Concave-Shape verboten");
                }
                if (shapes != (shape == PlatformShape.Cross ? 2 : 1)) Fail($"{shape}: {shapes} CollisionShapes");
                if (body.GetNodeOrNull<MeshInstance3D>("Mesh") is not MeshInstance3D mi || mi.Mesh == null) Fail($"{shape}: Mesh fehlt");
                else if (shape != PlatformShape.Box && mi.Mesh is not ArrayMesh) Fail($"{shape}: kein ArrayMesh");

                // Testpunkte: Mitte und ein Punkt nahe am Rand der begehbaren Fläche (Innenkreis − 0,5 m bzw. Balkenende).
                var points = new List<Vector3> { spec.Center };
                float rr = Mathf.Max(0f, spec.InnerRadius - 0.5f);
                if (shape == PlatformShape.Cross) points.Add(spec.Center + new Vector3(spec.Size.X * 0.5f - 0.6f, 0f, 0f));
                else if (shape == PlatformShape.Ramp) { points.Add(spec.Center + new Vector3(0f, 0f, 1.5f)); points.Add(spec.Center + new Vector3(0f, 0f, -1.5f)); }
                else if (shape != PlatformShape.Box) points.Add(spec.Center + new Vector3(rr * 0.7071f, 0f, rr * 0.7071f));
                foreach (Vector3 pt in points)
                {
                    _cases++;
                    float expected = spec.SurfaceYAt(pt.Z);
                    await Drop(new Vector3(pt.X, expected + 1.2f, pt.Z));
                    (bool onFloor, float y) = await Settle(2f);
                    bool ok = onFloor && Mathf.Abs(y - expected) <= 0.1f;
                    GD.Print($"[ShapeCollision] {(ok ? "PASS" : "FAIL")} {shape} at ({pt.X:F2},{pt.Z:F2}) y={y:F3} expected={expected:F3} onFloor={onFloor}");
                    if (!ok) Fail($"{shape}@({pt.X:F1},{pt.Z:F1}): y={y:F3} erwartet {expected:F3} onFloor={onFloor}");
                }
                // Außerhalb der Form (Ecke der Bounding-Box bei runden/polygonalen Formen) darf man nicht stehen.
                if (shape is PlatformShape.Disc or PlatformShape.Cylinder or PlatformShape.Diamond)
                {
                    Vector3 corner = spec.Center + new Vector3(spec.Size.X * 0.5f - 0.1f, 0f, spec.Size.Z * 0.5f - 0.1f);
                    await Drop(new Vector3(corner.X, spec.TopY + 1.2f, corner.Z));
                    await Frames(60);
                    if (_p.GlobalPosition.Y > spec.TopY - 0.5f && _p.IsOnFloor() && Mathf.Abs(_p.GlobalPosition.Y - spec.TopY) < 0.05f)
                        Fail($"{shape}: Spieler steht in der leeren Bounding-Box-Ecke (Kollision zu groß)");
                }
            }

            await RotatingDisc();
            await BuildTiming();

            GD.Print($"[ShapeCollision] cases={_cases} failures={_fail.Count}");
            GD.Print(_fail.Count == 0 ? "SHAPE_COLLISION_TEST_PASS" : "SHAPE_COLLISION_TEST_FAIL: " + string.Join("; ", _fail));
            GetTree().Quit(_fail.Count == 0 ? 0 : 1);
        }
        catch (Exception e)
        {
            GD.PrintErr("SHAPE_COLLISION_TEST_FAIL: " + e);
            GetTree().Quit(1);
        }
    }

    /// <summary>Chunk-Bau (Plan + Instantiate + AddChild) &lt; 4 ms im Mittel; Shape-/Hull-Caches greifen.</summary>
    private async Task BuildTiming()
    {
        foreach (Node old in _chunks.GetChildren()) old.QueueFree();
        await Frames(2);
        var b = new ChunkBuilder(424242);
        Vector3 entry = Vector3.Zero;
        PatternType? prev = null;
        int streak = 0;
        double total = 0, max = 0;
        const int n = 150;
        var alive = new Queue<Chunk>();
        for (int i = 0; i < n; i++)
        {
            bool prevItem = prev.HasValue && prev.Value.IsItemRequired();
            ulong t0 = Time.GetTicksUsec();
            ChunkLayout l = b.Plan(i, entry, prev, streak, prevItem, ChunkBuilder.DifficultyForIndex(i));
            Chunk c = ChunkBuilder.Instantiate(l);
            _chunks.AddChild(c);
            double ms = (Time.GetTicksUsec() - t0) / 1000.0;
            if (i >= 5) { total += ms; max = Math.Max(max, ms); }
            alive.Enqueue(c);
            if (alive.Count > 12) alive.Dequeue().QueueFree();
            streak = prev.HasValue && prev.Value == l.Pattern ? streak + 1 : 1;
            prev = l.Pattern;
            entry = l.ExitPoint;
            await Frame();
        }
        double avg = total / (n - 5);
        _cases++;
        GD.Print($"[ShapeCollision] ChunkBuild avg={avg:F2} ms max={max:F2} ms over {n - 5} chunks");
        if (avg > 4.0) Fail($"Chunk-Bau zu langsam: Mittel {avg:F2} ms");
        foreach (Node x in _chunks.GetChildren()) x.QueueFree();
        await Frames(2);
    }

    private async Task RotatingDisc()
    {
        var spec = new PlatformSpec
        {
            Shape = PlatformShape.Disc,
            Size = new Vector3(3.4f, 0.6f, 3.4f),
            Center = new Vector3(0f, 1.7f, -20f),
            SpinSpeed = GameConstants.RotatingMaxAngularSpeed,
            Material = MaterialNames.PlatformSpin,
        };
        Chunk c = await Spawn(spec);
        if (c.GetNodeOrNull("Platform_0") is not RotatingPlatform rp) { Fail("Drehende Disc ist keine RotatingPlatform"); return; }
        float yaw0 = rp.Rotation.Y;
        // Spieler 0,9 m außerhalb der Mitte absetzen (Bahngeschwindigkeit ≈ 0,8 m/s).
        await Drop(new Vector3(0.9f, spec.TopY + 1f, -20f));
        (bool onFloor, _) = await Settle(2f);
        if (!onFloor) { Fail("Drehende Disc: nicht gelandet"); return; }
        bool movingDetected = _p.IsOnMovingPlatform();
        bool stayed = true;
        float minY = float.MaxValue;
        for (int i = 0; i < 300; i++)
        {
            await Frame();
            minY = Mathf.Min(minY, _p.GlobalPosition.Y);
            float r = new Vector2(_p.GlobalPosition.X - spec.Center.X, _p.GlobalPosition.Z - spec.Center.Z).Length();
            if (r > spec.InnerRadius + 0.3f || _p.GlobalPosition.Y < spec.TopY - 0.2f) { stayed = false; break; }
        }
        float yaw1 = rp.Rotation.Y;
        bool rotated = Mathf.Abs(Mathf.AngleDifference(yaw0, yaw1)) > 0.1f;
        _cases++;
        GD.Print($"[ShapeCollision] RotatingDisc stayed={stayed} onFloor={_p.IsOnFloor()} minY={minY:F2} rotated={rotated} movingDetected={movingDetected}");
        if (!stayed || !_p.IsOnFloor()) Fail($"Drehende Disc: Spieler nicht 5 s oben geblieben (minY={minY:F2})");
        if (!rotated) Fail("Drehende Disc dreht sich nicht");
        if (!movingDetected) Fail("IsOnMovingPlatform erkennt RotatingPlatform nicht");
    }
}
