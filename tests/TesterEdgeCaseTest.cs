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
/// Tester: Grenzfälle mit echter Physik (Items mit 0 Ladungen, Item-Spam, Enterhaken ohne Anker,
/// Pause während Enterhaken, Trampolin-Auslösung, Origin-Shift auf beweglicher Plattform,
/// Restock/Bonus-Farming, Chunk-Kontinuität zwischen Checkpoint und Spieler).
/// Start: godot --headless --fixed-fps 60 --path . res://tests/TesterEdgeCaseTest.tscn
/// Ausgabe EDGE_TEST_DONE pass=N fail=M und je Fall "[Edge] PASS|FAIL name: details".
/// Exit-Code 0 = alles PASS, 1 = mindestens ein FAIL (seit der Fix-Runde), 2 bei Absturz des Testtreibers.
/// </summary>
public partial class TesterEdgeCaseTest : Node
{
    private Main _main = null!;
    private WorldGenerator _gen = null!;
    private PlayerController _p = null!;
    private string? _lastReason;
    private int _exceptions;
    private string? _firstException;
    private int _pass, _fail;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        GameManager.Current.PersistenceEnabled = false;
        GameManager.Current.StartNewRun(4242);
        _main = ResourceLoader.Load<PackedScene>(GameManager.MainScenePath).Instantiate<Main>();
        _main.ProcessMode = ProcessModeEnum.Pausable; // wie im echten Spiel (Main ist Szenenwurzel), nicht vom Testtreiber erben
        AddChild(_main);
        _gen = _main.Generator!;
        _p = _main.Player!;
        _p.ItemUseFailed += r => _lastReason = r;
        AppDomain.CurrentDomain.FirstChanceException += OnEx;
        Run();
    }

    public override void _ExitTree() => AppDomain.CurrentDomain.FirstChanceException -= OnEx;

    private void OnEx(object? s, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs e)
    {
        _exceptions++;
        _firstException ??= e.Exception.GetType().Name + ": " + e.Exception.Message + " @ " + e.Exception.StackTrace;
    }

    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

    private async Task Frames(int n) { for (int i = 0; i < n; i++) await Frame(); }

    private void Report(string name, bool ok, string details)
    {
        if (ok) _pass++; else _fail++;
        GD.Print($"[Edge] {(ok ? "PASS" : "FAIL")} {name}: {details}");
    }

    private async Task Press(string action)
    {
        Input.ActionPress(action);
        await Frame();
        Input.ActionRelease(action);
        await Frame();
    }

    private async Task PlaceOnStart(Vector3 pos)
    {
        _p.StopGrapple(false);
        _p.GlobalPosition = pos;
        _p.Velocity = Vector3.Zero;
        _p.CameraRig!.SnapBehind(Vector3.Forward);
        await Frames(20);
    }

    private int PlaceableCount()
    {
        int n = 0;
        foreach (Node x in GetTree().GetNodesInGroup("placeable"))
            if (!x.IsQueuedForDeletion()) n++;
        return n;
    }

    private void ClearPlaceables()
    {
        foreach (Node x in GetTree().GetNodesInGroup("placeable")) x.QueueFree();
    }

    private async void Run()
    {
        try
        {
            await Frames(30);
            await ZeroCharges();
            await BridgeSpam();
            await GrappleNoAnchor();
            await PauseDuringGrapple();
            await UseItemWhileGrapplingNoCharges();
            await AnchorFreedDuringGrapple();
            await RespawnDuringGrapple();
            await TrampolineSideTrigger();
            await OriginShiftOnMovingPlatform();
            await RestockAndFarming();
            await CheckpointSkipHole();
            Report("Exceptions", _exceptions == 0, _exceptions == 0 ? "keine" : $"{_exceptions}: {_firstException}");
            GD.Print($"EDGE_TEST_DONE pass={_pass} fail={_fail}");
            GetTree().Paused = false;
            GetTree().Quit(_fail == 0 ? 0 : 1);
        }
        catch (Exception e)
        {
            GD.PrintErr("EDGE_TEST_CRASH: " + e);
            GetTree().Quit(2);
        }
    }

    // ------------------------------------------------------------------

    private async Task ZeroCharges()
    {
        await PlaceOnStart(new Vector3(0, 0.05f, -10f));
        _p.Inventory.Clear();
        ClearPlaceables();
        await Frame();
        bool ok = true;
        var details = new List<string>();
        foreach (ItemType t in new[] { ItemType.Bridge, ItemType.Trampoline, ItemType.GrappleHook })
        {
            _p.Inventory.Select(t);
            _lastReason = null;
            await Press("use_item");
            bool good = _lastReason == "Keine Ladung mehr" && PlaceableCount() == 0 && !_p.IsGrappling && _p.Inventory.GetCount(t) == 0;
            ok &= good;
            details.Add($"{t}: reason='{_lastReason}'");
        }
        Report("ZeroCharges", ok, string.Join(", ", details));
    }

    private async Task BridgeSpam()
    {
        await PlaceOnStart(new Vector3(0, 0.05f, -10f));
        ClearPlaceables();
        await Frame();
        _p.Inventory.Clear();
        _p.Inventory.Add(ItemType.Bridge, 5);
        _p.Inventory.Select(ItemType.Bridge);
        Node3D root = Placeables.FindRoot(_p)!;
        int created = 0;
        void OnChild(Node n) { if (n is Bridge) created++; }
        root.ChildEnteredTree += OnChild;
        int maxAlive = 0;
        for (int i = 0; i < 12; i++)
        {
            Input.ActionPress("use_item");
            await Frame();
            Input.ActionRelease("use_item");
            maxAlive = Math.Max(maxAlive, PlaceableCount());
            await Frame();
            maxAlive = Math.Max(maxAlive, PlaceableCount());
        }
        root.ChildEnteredTree -= OnChild;
        int consumed = 5 - _p.Inventory.GetCount(ItemType.Bridge);
        Report("BridgeSpam", consumed == created && maxAlive <= GameConstants.MaxPlaceables && _p.Inventory.GetCount(ItemType.Bridge) >= 0,
            $"consumed={consumed} created={created} maxAlive={maxAlive} lastReason='{_lastReason}'");
        ClearPlaceables();
    }

    private async Task GrappleNoAnchor()
    {
        await PlaceOnStart(new Vector3(0, 0.05f, -10f));
        _p.Inventory.Clear();
        _p.Inventory.Add(ItemType.GrappleHook, 2);
        _p.Inventory.Select(ItemType.GrappleHook);
        bool noTarget = GrappleHook.FindTarget(_p) == null;
        _lastReason = null;
        await Press("use_item");
        Report("GrappleNoAnchor", noTarget && !_p.IsGrappling && _p.Inventory.GetCount(ItemType.GrappleHook) == 2 && _lastReason == "Kein Anker in Reichweite",
            $"noTarget={noTarget} grappling={_p.IsGrappling} count={_p.Inventory.GetCount(ItemType.GrappleHook)} reason='{_lastReason}'");
    }

    private GrappleAnchor SpawnAnchor(Vector3 globalPos)
    {
        var a = ResourceLoader.Load<PackedScene>("res://scenes/objects/GrappleAnchor.tscn").Instantiate<GrappleAnchor>();
        _gen.GetChunk(0)!.AddChild(a);
        a.GlobalPosition = globalPos;
        return a;
    }

    private async Task PauseDuringGrapple()
    {
        await PlaceOnStart(new Vector3(0, 0.05f, -8f));
        GrappleAnchor a = SpawnAnchor(new Vector3(0, 3.5f, -22f));
        await Frame();
        _p.StartGrapple(a);
        await Frames(5);
        Vector3 atPause = _p.GlobalPosition;
        GetTree().Paused = true;
        await Frames(60);
        Vector3 afterPause = _p.GlobalPosition;
        bool stillGrappling = _p.IsGrappling;
        GetTree().Paused = false;
        float minDist = 999f;
        for (int i = 0; i < 120; i++)
        {
            await Frame();
            minDist = Mathf.Min(minDist, a.GlobalPosition.DistanceTo(_p.GlobalPosition + Vector3.Up * 0.9f));
        }
        bool ok = atPause.DistanceTo(afterPause) < 0.01f && stillGrappling && minDist < 2f && !_p.IsGrappling;
        Report("PauseDuringGrapple", ok, $"movedWhilePaused={atPause.DistanceTo(afterPause):F3} grapplingAfterPause={stillGrappling} minDist={minDist:F2}");
        a.QueueFree();
        await PlaceOnStart(new Vector3(0, 0.05f, -10f));
    }

    private async Task UseItemWhileGrapplingNoCharges()
    {
        await PlaceOnStart(new Vector3(0, 0.05f, -4f));
        GrappleAnchor a = SpawnAnchor(new Vector3(0, 6f, -26f));
        await Frame();
        _p.Inventory.Clear();
        _p.Inventory.Select(ItemType.GrappleHook);
        _p.StartGrapple(a); // entspricht dem Zustand nach dem Verbrauch der letzten Ladung
        await Frames(2);
        _lastReason = null;
        await Press("use_item");
        bool released = !_p.IsGrappling && _lastReason == null;
        Report("UseItemWhileGrappling(0 Ladungen)", released,
            $"released={released} reason='{_lastReason}' (CONTRACT: use_item während Enterhaken löst ihn)");
        _p.StopGrapple(false);
        a.QueueFree();
    }

    private async Task AnchorFreedDuringGrapple()
    {
        await PlaceOnStart(new Vector3(0, 0.05f, -4f));
        GrappleAnchor a = SpawnAnchor(new Vector3(0, 6f, -26f));
        await Frame();
        _p.StartGrapple(a);
        await Frames(3);
        int exBefore = _exceptions;
        a.QueueFree();
        await Frames(5);
        Report("AnchorFreedDuringGrapple", !_p.IsGrappling && _exceptions == exBefore, $"grappling={_p.IsGrappling} newExceptions={_exceptions - exBefore}");
    }

    private async Task RespawnDuringGrapple()
    {
        await PlaceOnStart(new Vector3(0, 0.05f, -4f));
        GrappleAnchor a = SpawnAnchor(new Vector3(0, 6f, -26f));
        await Frame();
        _p.StartGrapple(a);
        await Frames(3);
        _p.Respawn();
        await Frames(2);
        var rope = _p.GetNodeOrNull<MeshInstance3D>("GrappleRope");
        Report("RespawnDuringGrapple", !_p.IsGrappling && (rope == null || !rope.Visible) && _p.Velocity.Length() < 1f,
            $"grappling={_p.IsGrappling} ropeVisible={rope?.Visible} v={_p.Velocity.Length():F2}");
        a.QueueFree();
    }

    private async Task TrampolineSideTrigger()
    {
        ClearPlaceables();
        await PlaceOnStart(new Vector3(0, 0.05f, -20f));
        _p.Inventory.Clear();
        _p.Inventory.Add(ItemType.Trampoline, 1);
        _p.Inventory.Select(ItemType.Trampoline);
        float y0 = _p.GlobalPosition.Y;
        await Press("use_item");
        bool placed = _p.Inventory.GetCount(ItemType.Trampoline) == 0;
        float maxY = y0;
        for (int i = 0; i < 40; i++) { await Frame(); maxY = Mathf.Max(maxY, _p.GlobalPosition.Y); }
        Report("TrampolineNoSideTrigger", placed && maxY - y0 < 1f,
            $"placed={placed} riseWithoutSteppingOn={maxY - y0:F2} m (Spieler steht 1,5 m daneben, Auslöseradius {Trampoline.TriggerRadius} m)");
        ClearPlaceables();
        await PlaceOnStart(new Vector3(0, 0.05f, -10f));
    }

    private async Task OriginShiftOnMovingPlatform()
    {
        Chunk c0 = _gen.GetChunk(0)!;
        var mp = new MovingPlatform
        {
            Name = "TestMover",
            CollisionLayer = GameConstants.LayerWorld,
            CollisionMask = 0,
            Offset = new Vector3(4f, 0f, 0f),
            Speed = 1.5f,
        };
        mp.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(4, 0.6f, 4) } });
        mp.Position = c0.ToLocal(new Vector3(-2f, 4f, -15f));
        c0.AddChild(mp);
        await Frames(5);
        _p.StopGrapple(false);
        _p.GlobalPosition = mp.GlobalPosition + Vector3.Up * 0.5f;
        _p.Velocity = Vector3.Zero;
        await Frames(60);
        bool onPlatform = _p.IsOnFloor() && _p.GlobalPosition.Y > mp.GlobalPosition.Y;
        Vector3 relBefore = _p.GlobalPosition - mp.GlobalPosition;

        MethodInfo? shift = typeof(WorldGenerator).GetMethod("ShiftOrigin", BindingFlags.NonPublic | BindingFlags.Instance);
        if (shift == null) { Report("OriginShiftOnMovingPlatform", false, "ShiftOrigin nicht gefunden"); return; }
        shift.Invoke(_gen, new object[] { new Vector3(0, 0, 500f) });
        float maxDev = 0f;
        Vector3 worst = Vector3.Zero;
        for (int i = 0; i < 20; i++)
        {
            await Frame();
            Vector3 rel = _p.GlobalPosition - mp.GlobalPosition;
            float dev = new Vector2(rel.X - relBefore.X, rel.Z - relBefore.Z).Length();
            if (dev > maxDev) { maxDev = dev; worst = _p.GlobalPosition; }
        }
        Report("OriginShiftOnMovingPlatform", onPlatform && maxDev < 1.5f,
            $"onPlatformBefore={onPlatform} maxHorizDeviation={maxDev:F1} m worstPlayerPos={worst} platform={mp.GlobalPosition}");
        // zurückschieben, damit die restlichen Tests in Ursprungsnähe laufen
        shift.Invoke(_gen, new object[] { new Vector3(0, 0, -500f) });
        await Frames(10);

        // R-01: Generator erkennt "steht auf beweglicher Plattform" (Origin-Shift wird dann verschoben)
        _p.GlobalPosition = mp.GlobalPosition + Vector3.Up * 0.5f;
        _p.Velocity = Vector3.Zero;
        await Frames(20);
        Report("DetectsMovingPlatform", _p.IsOnMovingPlatform(), $"onFloor={_p.IsOnFloor()} onMoving={_p.IsOnMovingPlatform()}");

        // F-07: Trampolin auf beweglicher Plattform wird abgelehnt
        ClearPlaceables();
        _p.Inventory.Clear();
        _p.Inventory.Add(ItemType.Trampoline, 1);
        _p.Inventory.Select(ItemType.Trampoline);
        _p.CameraRig!.SnapBehind(Vector3.Right); // entlang der Plattform zielen (4 m breit)
        _lastReason = null;
        await Press("use_item");
        Report("TrampolineOnMovingPlatformRejected", _p.Inventory.GetCount(ItemType.Trampoline) == 1 && PlaceableCount() == 0,
            $"count={_p.Inventory.GetCount(ItemType.Trampoline)} placeables={PlaceableCount()} reason='{_lastReason}'");
        ClearPlaceables();
        mp.QueueFree();
        await Frames(5);
    }

    private async Task RestockAndFarming()
    {
        // Spieler in Etappen nach vorne bewegen, bis ein Item-Chunk existiert.
        Chunk? item = null;
        for (int k = 0; k < 60 && item == null; k++)
        {
            foreach (Chunk c in _gen.AliveChunks)
                if (IsInstanceValid(c) && c.IsItemChunk) { item = c; break; }
            if (item != null) break;
            Chunk? far = _gen.GetChunk(_gen.HighestChunkIndex - 2);
            if (far != null)
            {
                _p.SetCheckpoint(far.CheckpointPosition, far.Index);
                _p.Respawn(false);
            }
            await Frames(15);
        }
        if (item == null) { Report("Restock", false, "kein Item-Chunk gefunden"); return; }

        int supply0 = item.SupplyCrateCount;
        int bonus0 = GameManager.Current.CrateBonus;
        _p.SetCheckpoint(item.CheckpointPosition, item.Index);
        _p.Respawn(false);
        await Frames(10);
        for (int cycle = 0; cycle < 3; cycle++)
        {
            foreach (Node n in item.GetChildren())
                if (n is Crate cr && cr.IsSupply && !cr.IsBroken) cr.Hit(_p);
            await Frames(3);
            foreach (Node n in GetTree().GetNodesInGroup("pickup")) n.QueueFree(); // Pickups nicht einsammeln
            _p.Inventory.Clear();
            await Frames(2);
            _p.Respawn(); // absichtlicher Sturz
            await Frames(3);
        }
        int supply1 = item.SupplyCrateCount;
        int gained = GameManager.Current.CrateBonus - bonus0;
        Report("RestockSupply", supply1 == supply0 && supply0 >= 2, $"pattern={item.Pattern} supplyBefore={supply0} after3Cycles={supply1}");
        Report("CrateBonusFarming", gained <= supply0 * GameConstants.CrateBonusScore,
            $"Bonus nach 3 Sturz-Zyklen +{gained} (max. legitim {supply0 * GameConstants.CrateBonusScore})");
    }

    private async Task CheckpointSkipHole()
    {
        // Spieler überfliegt Checkpoints (z. B. Trampolin/Enterhaken) → Chunks zwischen Checkpoint und Spieler dürfen nicht verschwinden.
        Chunk? start = _gen.GetChunk(_p.ActiveCheckpointChunk);
        if (start == null) { Report("CheckpointSkipHole", false, "kein Checkpoint-Chunk"); return; }
        int cp = _p.ActiveCheckpointChunk;
        _p.Respawn(false);
        await Frames(5);
        string? hole = null;
        float speed = 30f;
        for (int i = 0; i < 360 && hole == null; i++)
        {
            float targetZ = _p.GlobalPosition.Z - speed / 60f;
            Chunk? c = null;
            foreach (Chunk ch in _gen.AliveChunks)
                if (IsInstanceValid(ch) && targetZ <= ch.EntryPoint.Z && targetZ >= ch.ExitPoint.Z) { c = ch; break; }
            if (c != null)
            {
                // knapp oberhalb der Checkpoint-Trigger (3 m), unterhalb der Sturzgrenze
                Vector3 cpPos = _p.RespawnPosition;
                _p.GlobalPosition = new Vector3(c.EntryPoint.X, Mathf.Max(c.EntryPoint.Y + 6f, cpPos.Y - 15f), targetZ);
                _p.Velocity = Vector3.Zero;
            }
            await Frame();
            for (int k = _p.ActiveCheckpointChunk; k <= _p.CurrentChunkIndex; k++)
                if (_gen.GetChunk(k) == null) { hole = $"Chunk {k} fehlt (checkpoint={_p.ActiveCheckpointChunk}, current={_p.CurrentChunkIndex})"; break; }
        }
        Report("CheckpointSkipHole", hole == null, hole ?? $"kein Loch (checkpoint={_p.ActiveCheckpointChunk}, current={_p.CurrentChunkIndex}, start cp={cp})");
        _p.Respawn(false);
        await Frames(10);
        bool groundAhead = true;
        // nach dem Respawn: ist der Weg bis zum aktuellen Chunk lückenlos?
        for (int k = _p.ActiveCheckpointChunk; k <= _gen.HighestChunkIndex - 1; k++)
        {
            Chunk? a = _gen.GetChunk(k), b = _gen.GetChunk(k + 1);
            if (a == null || b == null) { groundAhead = false; break; }
            if (a.ExitPoint.DistanceTo(b.EntryPoint) > 0.01f) { groundAhead = false; break; }
        }
        Report("PathContinuousAfterRespawn", groundAhead, $"checkpoint={_p.ActiveCheckpointChunk} highest={_gen.HighestChunkIndex}");
    }
}
