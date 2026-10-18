using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using JumpRun.Game;
using JumpRun.Items;
using JumpRun.Player;
using JumpRun.World;

namespace JumpRun.Tests;

/// <summary>
/// Tester: physikalische Lösbarkeit der Item-Pflicht-Abschnitte (BigGap mit Brücke bzw. Enterhaken,
/// TallWall mit Trampolin) über mehrere Seeds und Schwierigkeitsgrade, mit echter Spielerphysik.
/// Start: godot --headless --fixed-fps 60 --path . res://tests/TesterItemSolvabilityTest.tscn
/// Ausgabe je Fall "[Solve] PASS|FAIL ..." und ITEM_SOLVE_DONE pass=N fail=M.
/// </summary>
public partial class TesterItemSolvabilityTest : Node3D
{
    private Node3D _chunks = null!;
    private PlayerController _p = null!;
    private int _pass, _fail;

    public override void _Ready()
    {
        GameManager.Current.PersistenceEnabled = false;
        GameManager.Current.StartNewRun(1);
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

    private async void Run()
    {
        try
        {
            var cases = new List<(PatternType Pattern, float D, int Seed, int Index)>();
            foreach (float d in new[] { 0.1f, 0.6f, 1.0f })
                foreach (PatternType pt in new[] { PatternType.BigGap, PatternType.TallWall })
                {
                    int found = 0;
                    for (int seed = 1; seed < 400 && found < 4; seed++)
                    {
                        var b = new ChunkBuilder(seed);
                        int idx = 10 + seed % 7;
                        ChunkLayout l = b.Plan(idx, Vector3.Zero, PatternType.GapJump, 1, false, d);
                        if (l.Pattern != pt) continue;
                        // für BigGap möglichst große Lücken wählen (worst case)
                        if (pt == PatternType.BigGap && d >= 1f && l.ItemObstacleSize < GameConstants.BigGapMax - 0.7f) continue;
                        cases.Add((pt, d, seed, idx));
                        found++;
                    }
                }

            foreach (var c in cases)
            {
                if (c.Pattern == PatternType.BigGap)
                {
                    await Scenario(c, "Bridge", BridgeRun);
                    await Scenario(c, "Grapple", GrappleRun);
                }
                else
                {
                    await Scenario(c, "Trampoline", TrampolineRun);
                }
            }
            GD.Print($"ITEM_SOLVE_DONE pass={_pass} fail={_fail}");
            GetTree().Quit(_fail == 0 ? 0 : 1);
        }
        catch (Exception e)
        {
            GD.PrintErr("ITEM_SOLVE_CRASH: " + e);
            GetTree().Quit(2);
        }
    }

    private async Task Scenario((PatternType Pattern, float D, int Seed, int Index) c, string how,
        Func<ChunkLayout, Task<(bool, string)>> run)
    {
        foreach (Node n in _chunks.GetChildren()) n.QueueFree();
        foreach (Node n in GetTree().GetNodesInGroup("placeable")) n.QueueFree();
        await Frames(2);
        ChunkLayout l = new ChunkBuilder(c.Seed).Plan(c.Index, Vector3.Zero, PatternType.GapJump, 1, false, c.D);
        Chunk chunk = ChunkBuilder.Instantiate(l);
        _chunks.AddChild(chunk);
        await Frames(2);
        _p.Inventory.Clear();
        (bool ok, string info) = await run(l);
        if (ok) _pass++; else _fail++;
        GD.Print($"[Solve] {(ok ? "PASS" : "FAIL")} {c.Pattern}/{how} seed={c.Seed} d={c.D:F1} obstacle={l.ItemObstacleSize:F2}: {info}");
        foreach (string a in new[] { "move_forward", "sprint", "jump", "use_item" }) Input.ActionRelease(a);
    }

    private async Task Place(Vector3 pos)
    {
        _p.StopGrapple(false);
        _p.GlobalPosition = pos;
        _p.Velocity = Vector3.Zero;
        _p.CameraRig!.SnapBehind(Vector3.Forward);
        _p.SetCheckpoint(pos + Vector3.Down, 0);
        await Frames(20);
    }

    private static float FrontEdge(PlatformSpec p) => p.Center.Z - p.Size.Z / 2f; // weiter in -Z
    private static float BackEdge(PlatformSpec p) => p.Center.Z + p.Size.Z / 2f;

    private async Task Press(string a) { Input.ActionPress(a); await Frame(); Input.ActionRelease(a); await Frame(); }

    // BigGap: Platforms[1] = Supply, Platforms[2] = Landeplattform
    private async Task<(bool, string)> BridgeRun(ChunkLayout l)
    {
        PlatformSpec supply = l.Platforms[1], landing = l.Platforms[2];
        await Place(new Vector3(supply.Center.X, supply.TopY + 0.05f, FrontEdge(supply) + 0.6f));
        _p.Inventory.Add(ItemType.Bridge, 1);
        _p.Inventory.Select(ItemType.Bridge);
        await Press("use_item");
        if (_p.Inventory.GetCount(ItemType.Bridge) != 0) return (false, "Brücke nicht platziert");
        Input.ActionPress("move_forward");
        bool reached = false;
        for (int i = 0; i < 240; i++)
        {
            await Frame();
            if (_p.GlobalPosition.Z < BackEdge(landing) - 1f && _p.IsOnFloor() && Mathf.Abs(_p.GlobalPosition.Y - landing.TopY) < 0.3f) { reached = true; break; }
            if (_p.GlobalPosition.Y < landing.TopY - 3f) break;
        }
        Input.ActionRelease("move_forward");
        return (reached, $"end z={_p.GlobalPosition.Z:F1} y={_p.GlobalPosition.Y:F1} landingNear={BackEdge(landing):F1}");
    }

    private void SetPitch(float deg)
    {
        ThirdPersonCamera cam = _p.CameraRig!;
        typeof(ThirdPersonCamera).GetField("_pitchRad", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cam, Mathf.DegToRad(deg));
        typeof(ThirdPersonCamera).GetMethod("ApplyRotation", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(cam, null);
    }

    private async Task<(bool, string)> GrappleRun(ChunkLayout l)
    {
        PlatformSpec supply = l.Platforms[1], landing = l.Platforms[2];
        await Place(new Vector3(supply.Center.X, supply.TopY + 0.05f, FrontEdge(supply) + 0.6f));
        _p.Inventory.Add(ItemType.GrappleHook, 1);
        _p.Inventory.Select(ItemType.GrappleHook);
        // Kamera so weit nach oben neigen, bis ein Ziel gefunden wird (realistisches Zielen)
        float pitch = -15f;
        while (GrappleHook.FindTarget(_p) == null && pitch < 35f) { pitch += 5f; SetPitch(pitch); await Frames(2); }
        if (GrappleHook.FindTarget(_p) == null) return (false, "kein Anker anvisierbar (max. Pitch 35°)");
        await Press("use_item");
        if (!_p.IsGrappling && _p.Inventory.GetCount(ItemType.GrappleHook) != 0) return (false, "Enterhaken nicht gestartet");
        Input.ActionPress("move_forward");
        Input.ActionPress("sprint");
        bool reached = false;
        for (int i = 0; i < 300; i++)
        {
            await Frame();
            if (_p.GlobalPosition.Z < BackEdge(landing) - 0.5f && _p.IsOnFloor() && Mathf.Abs(_p.GlobalPosition.Y - landing.TopY) < 0.3f) { reached = true; break; }
            if (_p.GlobalPosition.Y < landing.TopY - 3f) break;
        }
        Input.ActionRelease("move_forward");
        Input.ActionRelease("sprint");
        return (reached, $"pitch={pitch}° end z={_p.GlobalPosition.Z:F1} y={_p.GlobalPosition.Y:F1} landingNear={BackEdge(landing):F1}");
    }

    // TallWall: Platforms[1] = Supply, [2] = Boden, [3] = Wand
    private async Task<(bool, string)> TrampolineRun(ChunkLayout l)
    {
        PlatformSpec floor = l.Platforms[2], wall = l.Platforms[3];
        float wallFront = BackEdge(wall);
        // 3,5 m vor der Wand: Trampolin landet 1,5 m vor dem Spieler (≈2 m vor der Wand)
        await Place(new Vector3(0f, floor.TopY + 0.05f, wallFront + 3.5f));
        _p.Inventory.Add(ItemType.Trampoline, 1);
        _p.Inventory.Select(ItemType.Trampoline);
        await Press("use_item");
        if (_p.Inventory.GetCount(ItemType.Trampoline) != 0) return (false, "Trampolin nicht platziert");
        await Frames(2);
        Input.ActionPress("move_forward");
        bool reached = false;
        for (int i = 0; i < 300; i++)
        {
            await Frame();
            if (_p.GlobalPosition.Z < FrontEdge(wall) - 0.5f && _p.IsOnFloor() && Mathf.Abs(_p.GlobalPosition.Y - floor.TopY) < 0.3f) { reached = true; break; }
            if (_p.GlobalPosition.Y < floor.TopY - 3f) break;
        }
        Input.ActionRelease("move_forward");
        return (reached, $"wallH={wall.Size.Y:F1} end z={_p.GlobalPosition.Z:F1} y={_p.GlobalPosition.Y:F1} wallBack={FrontEdge(wall):F1}");
    }
}
