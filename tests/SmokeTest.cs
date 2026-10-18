using System;
using System.Collections.Generic;
using Godot;
using JumpRun.Game;
using JumpRun.Items;
using JumpRun.Player;
using JumpRun.World;

namespace JumpRun.Tests;

/// <summary>
/// Headless-Smoke-Test: lädt Main.tscn, bewegt den Spieler ~30 s (simulierte Zeit) entlang des Pfads
/// und prüft Spawn/Despawn, Chunk-Limit, Placeable-Limit, Kisten und Floating Origin.
/// </summary>
public partial class SmokeTest : Node
{
    private const float SimulatedSeconds = 32f;
    private const float WarmupSeconds = 2f;
    private const float TravelSpeed = 36f;     // m/s → > 1000 m, löst Floating Origin aus
    private const double RealTimeLimit = 180.0;

    private Main? _main;
    private WorldGenerator? _gen;
    private PlayerController? _player;
    private float _sim;
    private float _travelled;
    private int _maxHighest = -1;
    private int _despawnSignals;
    private int _spawnSignals;
    private int _originShifts;
    private int _cratesBroken;
    private int _exceptions;
    private string? _firstException;
    private bool _done;
    private bool _placeablesChecked;
    private float _nextCrateHit = 3f;
    private ulong _startTicks;
    private readonly List<string> _failures = new();

    public override void _Ready()
    {
        GD.Print("[SmokeTest] Logiktests …");
        List<string> logic = LogicTests.RunAll();
        if (logic.Count > 0)
        {
            foreach (string s in logic) GD.PrintErr("[LogicTests] " + s);
            Finish($"{logic.Count} Logiktest(s) fehlgeschlagen: {logic[0]}");
            return;
        }

        GameManager gm = GameManager.Current;
        gm.PersistenceEnabled = false;
        gm.StartNewRun(12345);

        PackedScene? scene = ResourceLoader.Load<PackedScene>(GameManager.MainScenePath);
        if (scene == null) { Finish("Main.tscn konnte nicht geladen werden"); return; }
        _main = scene.Instantiate<Main>();
        AddChild(_main);
        _gen = _main.Generator;
        _player = _main.Player;
        if (_gen == null || _player == null) { Finish("WorldGenerator oder Player fehlt in Main.tscn"); return; }

        _gen.ChunkSpawned += _ => _spawnSignals++;
        _gen.ChunkDespawned += _ => _despawnSignals++;
        _gen.OriginShifted += _ => _originShifts++;
        _player.DebugAutoRun = true;

        AppDomain.CurrentDomain.FirstChanceException += OnException;
        Engine.TimeScale = 2.0;
        _startTicks = Time.GetTicksMsec();
        GD.Print("[SmokeTest] Simulation gestartet");
    }

    public override void _ExitTree()
    {
        AppDomain.CurrentDomain.FirstChanceException -= OnException;
        Engine.TimeScale = 1.0;
    }

    private void OnException(object? sender, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs e)
    {
        _exceptions++;
        _firstException ??= $"{e.Exception.GetType().Name}: {e.Exception.Message}";
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_done || _gen == null || _player == null) return;
        float dt = (float)delta;
        _sim += dt;

        if ((Time.GetTicksMsec() - _startTicks) / 1000.0 > RealTimeLimit) { Finish("Zeitlimit überschritten"); return; }

        if (_sim > WarmupSeconds) MovePlayer(dt);
        if (_sim > WarmupSeconds + 1f) CheckInvariants();
        if (!_placeablesChecked && _sim > 1f) CheckPlaceables();
        if (_sim > _nextCrateHit) { _nextCrateHit += 2f; HitNearestCrate(); }

        if (_sim >= SimulatedSeconds) Evaluate();
    }

    private void MovePlayer(float dt)
    {
        _travelled += TravelSpeed * dt;
        float targetZ = _player!.GlobalPosition.Z - TravelSpeed * dt;
        Chunk? c = null;
        foreach (Chunk ch in _gen!.AliveChunks)
        {
            if (!IsInstanceValid(ch)) continue;
            if (targetZ <= ch.EntryPoint.Z && targetZ >= ch.ExitPoint.Z) { c = ch; break; }
        }
        if (c == null) return; // Generator hinkt hinterher → warten
        float t = Mathf.InverseLerp(c.EntryPoint.Z, c.ExitPoint.Z, targetZ);
        Vector3 path = c.EntryPoint.Lerp(c.ExitPoint, Mathf.Clamp(t, 0f, 1f));
        _player.GlobalPosition = new Vector3(path.X, path.Y + 15f, targetZ);
        _player.Velocity = Vector3.Zero;
        if (_player.ActiveCheckpointChunk < c.Index)
            _player.SetCheckpoint(c.CheckpointPosition, c.Index);
    }

    private void CheckInvariants()
    {
        int alive = _gen!.AliveChunkCount;
        if (alive < GameConstants.ChunksAhead) Fail($"zu wenige Chunks ({alive})");
        if (alive > GameConstants.MaxAliveChunks) Fail($"zu viele Chunks ({alive})");
        if (_gen.GetChunk(_player!.CurrentChunkIndex) == null) Fail($"Spieler-Chunk {_player.CurrentChunkIndex} existiert nicht");
        if (_gen.GetChunk(_player.ActiveCheckpointChunk) == null) Fail($"Checkpoint-Chunk {_player.ActiveCheckpointChunk} existiert nicht");
        if (_gen.HighestChunkIndex < _maxHighest) Fail("HighestChunkIndex gesunken");
        _maxHighest = Math.Max(_maxHighest, _gen.HighestChunkIndex);
        if (!float.IsFinite(_player.GlobalPosition.Z)) Fail("Spielerposition ungültig");
    }

    private void CheckPlaceables()
    {
        _placeablesChecked = true;
        PackedScene? bridge = ResourceLoader.Load<PackedScene>("res://scenes/objects/Bridge.tscn");
        PackedScene? tramp = ResourceLoader.Load<PackedScene>("res://scenes/objects/Trampoline.tscn");
        if (bridge == null || tramp == null) { Fail("Placeable-Szenen fehlen"); return; }
        for (int i = 0; i < 5; i++)
        {
            Node3D obj = (i % 2 == 0 ? bridge.Instantiate<Node3D>() : tramp.Instantiate<Node3D>());
            Placeables.Add(_player!, obj, new Transform3D(Basis.Identity, new Vector3(0, -5f - i, 10f)));
        }
        Node3D? root = Placeables.FindRoot(_player!);
        int alive = 0;
        if (root != null)
            foreach (Node n in root.GetChildren())
                if (!n.IsQueuedForDeletion()) alive++;
        if (alive > GameConstants.MaxPlaceables) Fail($"Placeable-Limit verletzt ({alive})");

        // Inventar: unbegrenzt (CONTRACT_V2 §3.2)
        Inventory inv = _player!.Inventory;
        int before = inv.GetCount(ItemType.Bridge);
        bool allTrue = true;
        for (int i = 0; i < 50; i++) allTrue &= inv.Add(ItemType.Bridge);
        if (!allTrue || inv.GetCount(ItemType.Bridge) != before + 50) Fail($"Inventar nicht unbegrenzt ({inv.GetCount(ItemType.Bridge)})");
        if (inv.Consume(ItemType.None)) Fail("Consume(None) erlaubt");
        if (inv.Add(ItemType.None)) Fail("Add(None) erlaubt");
        if (inv.Add(ItemType.Bridge, 0)) Fail("Add(x, 0) erlaubt");
    }

    private void HitNearestCrate()
    {
        Crate? best = null;
        float bestD = float.MaxValue;
        foreach (Node n in GetTree().GetNodesInGroup("crate"))
        {
            if (n is not Crate c || !IsInstanceValid(c) || c.IsBroken) continue;
            float d = c.GlobalPosition.DistanceSquaredTo(_player!.GlobalPosition);
            if (d < bestD) { bestD = d; best = c; }
        }
        if (best == null) return;
        best.Hit(_player!);
        _cratesBroken++;
    }

    private void Evaluate()
    {
        GameManager gm = GameManager.Current;
        GD.Print($"[SmokeTest] sim={_sim:F1}s weg={_travelled:F0}m spawned={_gen!.TotalSpawned} despawned={_gen.TotalDespawned} " +
                 $"alive={_gen.AliveChunkCount} highest={_gen.HighestChunkIndex} originShifts={_originShifts} " +
                 $"distance={gm.Distance} score={gm.Score} crates={_cratesBroken} pickups={GetTree().GetNodesInGroup("pickup").Count} " +
                 $"nodes={Performance.GetMonitor(Performance.Monitor.ObjectNodeCount)}");
        if (_gen.TotalDespawned <= 0 || _despawnSignals <= 0) Fail("es wurde kein Chunk entfernt");
        if (_spawnSignals < GameConstants.ChunksAhead + 5) Fail($"zu wenige Chunks erzeugt ({_spawnSignals})");
        if (_gen.HighestChunkIndex < 15) Fail($"HighestChunkIndex zu klein ({_gen.HighestChunkIndex})");
        if (_originShifts < 1) Fail("Floating Origin wurde nicht ausgelöst");
        if (gm.Distance < 800) Fail($"Distanz zu klein ({gm.Distance}) – OriginOffset nicht berücksichtigt?");
        if (_cratesBroken < 1) Fail("keine Kiste zerschlagen");
        if (_exceptions > 0) Fail($"{_exceptions} Exception(s): {_firstException}");
        Finish(_failures.Count == 0 ? null : string.Join("; ", _failures));
    }

    private void Fail(string reason)
    {
        if (!_failures.Contains(reason)) _failures.Add(reason);
    }

    private void Finish(string? failure)
    {
        if (_done) return;
        _done = true;
        Engine.TimeScale = 1.0;
        if (failure == null) GD.Print("SMOKE_TEST_PASS");
        else GD.Print("SMOKE_TEST_FAIL: " + failure);
        GetTree().Quit(failure == null ? 0 : 1);
    }
}
