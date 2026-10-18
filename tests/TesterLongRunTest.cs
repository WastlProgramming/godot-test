using System;
using System.Collections.Generic;
using Godot;
using JumpRun.Game;
using JumpRun.Items;
using JumpRun.Player;
using JumpRun.World;

namespace JumpRun.Tests;

/// <summary>
/// Tester: Langzeitlauf (300 s simulierte Zeit, ~10 km) mit Node-/Objekt-/Orphan-Monitoring,
/// Distanz-Konsistenz über mehrere Floating-Origin-Verschiebungen und Chunk-Invarianten.
/// Start: godot --headless --fixed-fps 60 --path . res://tests/TesterLongRunTest.tscn
/// Ausgabe LONGRUN_TEST_PASS / LONGRUN_TEST_FAIL: ...
/// </summary>
public partial class TesterLongRunTest : Node
{
    private const float SimulatedSeconds = 300f;
    private const float TravelSpeed = 36f;
    private const float SampleInterval = 10f;

    private Main? _main;
    private WorldGenerator? _gen;
    private PlayerController? _p;
    private float _sim;
    private float _nextSample = 30f;
    private float _nextAction = 1f;
    private double _travelled;
    private int _shifts;
    private int _exceptions;
    private string? _firstException;
    private bool _done;
    private float _startDistanceRef;
    private readonly List<string> _fail = new();
    private readonly List<(float T, double Nodes, double Objects, double Orphans, int Alive, int Pickups, int Crates, int Placeables)> _samples = new();
    private float _maxAbsZ;

    public override void _Ready()
    {
        GameManager gm = GameManager.Current;
        gm.PersistenceEnabled = false;
        gm.StartNewRun(777);
        _main = ResourceLoader.Load<PackedScene>(GameManager.MainScenePath).Instantiate<Main>();
        AddChild(_main);
        _gen = _main.Generator!;
        _p = _main.Player!;
        _gen.OriginShifted += _ => _shifts++;
        AppDomain.CurrentDomain.FirstChanceException += OnEx;
        _startDistanceRef = -_p.GlobalPosition.Z;
    }

    public override void _ExitTree() => AppDomain.CurrentDomain.FirstChanceException -= OnEx;

    private void OnEx(object? s, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs e)
    {
        _exceptions++;
        _firstException ??= e.Exception.GetType().Name + ": " + e.Exception.Message;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_done || _gen == null || _p == null) return;
        float dt = (float)delta;
        _sim += dt;
        if (_sim > 1f) Move(dt);
        if (_sim > 2f) Invariants();
        if (_sim > _nextAction) { _nextAction += 1.5f; Actions(); }
        if (_sim >= _nextSample) { _nextSample += SampleInterval; Sample(); }
        if (_sim >= SimulatedSeconds) Evaluate();
    }

    private void Move(float dt)
    {
        float targetZ = _p!.GlobalPosition.Z - TravelSpeed * dt;
        Chunk? c = null;
        foreach (Chunk ch in _gen!.AliveChunks)
            if (IsInstanceValid(ch) && targetZ <= ch.EntryPoint.Z && targetZ >= ch.ExitPoint.Z) { c = ch; break; }
        if (c == null) return;
        _travelled += TravelSpeed * dt;
        float t = Mathf.InverseLerp(c.EntryPoint.Z, c.ExitPoint.Z, targetZ);
        Vector3 path = c.EntryPoint.Lerp(c.ExitPoint, Mathf.Clamp(t, 0f, 1f));
        _p.GlobalPosition = new Vector3(path.X, path.Y + 15f, targetZ);
        _p.Velocity = Vector3.Zero;
        if (_p.ActiveCheckpointChunk < c.Index) _p.SetCheckpoint(c.CheckpointPosition, c.Index);
        _maxAbsZ = Mathf.Max(_maxAbsZ, Mathf.Abs(_p.GlobalPosition.Z));
    }

    private void Invariants()
    {
        int alive = _gen!.AliveChunkCount;
        if (alive > GameConstants.MaxAliveChunks) Fail($"zu viele Chunks ({alive}) bei t={_sim:F0}");
        if (alive < GameConstants.ChunksAhead) Fail($"zu wenige Chunks ({alive}) bei t={_sim:F0}");
        if (_gen.GetChunk(_p!.CurrentChunkIndex) == null) Fail($"Spieler-Chunk fehlt bei t={_sim:F0}");
        if (_gen.GetChunk(_p.ActiveCheckpointChunk) == null) Fail($"Checkpoint-Chunk fehlt bei t={_sim:F0}");
        // Kontinuität: alle Chunks zwischen Checkpoint und Spieler müssen existieren
        for (int i = _p.ActiveCheckpointChunk; i <= _p.CurrentChunkIndex; i++)
            if (_gen.GetChunk(i) == null) { Fail($"Loch: Chunk {i} zwischen Checkpoint und Spieler fehlt"); break; }
    }

    private void Actions()
    {
        // Kiste in der Nähe zerschlagen, Pickups einsammeln, Placeables erzeugen (Lebensdauer/Limit prüfen)
        Crate? best = null; float bd = float.MaxValue;
        foreach (Node n in GetTree().GetNodesInGroup("crate"))
            if (n is Crate c && IsInstanceValid(c) && !c.IsBroken)
            {
                float d = c.GlobalPosition.DistanceSquaredTo(_p!.GlobalPosition);
                if (d < bd) { bd = d; best = c; }
            }
        best?.Hit(_p!);
        _p!.Inventory.Clear();
        var bridge = ResourceLoader.Load<PackedScene>("res://scenes/objects/Bridge.tscn").Instantiate<Node3D>();
        Placeables.Add(_p, bridge, new Transform3D(Basis.Identity, _p.GlobalPosition + new Vector3(0, -30, 0)));
    }

    private void Sample()
    {
        // Verwaltete Wrapper (RefCounted: BoxMesh, Shapes, Query-Parameter) werden erst vom .NET-GC freigegeben.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var s = (_sim,
            Performance.GetMonitor(Performance.Monitor.ObjectNodeCount),
            Performance.GetMonitor(Performance.Monitor.ObjectCount),
            Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount),
            _gen!.AliveChunkCount,
            GetTree().GetNodesInGroup("pickup").Count,
            GetTree().GetNodesInGroup("crate").Count,
            GetTree().GetNodesInGroup("placeable").Count);
        _samples.Add(s);
        GD.Print($"[LongRun] t={s.Item1:F0}s nodes={s.Item2} objects={s.Item3} orphans={s.Item4} alive={s.Item5} pickups={s.Item6} crates={s.Item7} placeables={s.Item8} highest={_gen.HighestChunkIndex} dist={GameManager.Current.Distance} travelled={_travelled:F0} z={_p!.GlobalPosition.Z:F1}");
    }

    private void Evaluate()
    {
        _done = true;
        GameManager gm = GameManager.Current;
        if (_samples.Count >= 3)
        {
            var first = _samples[0];
            var last = _samples[^1];
            double maxNodes = 0, maxObj = 0;
            foreach (var s in _samples) { maxNodes = Math.Max(maxNodes, s.Nodes); maxObj = Math.Max(maxObj, s.Objects); }
            if (last.Nodes > first.Nodes * 1.5 + 50) Fail($"Node-Wachstum {first.Nodes} -> {last.Nodes}");
            if (last.Objects > first.Objects * 1.5 + 200) Fail($"Objekt-Wachstum {first.Objects} -> {last.Objects}");
            if (last.Orphans > first.Orphans + 20) Fail($"Orphan-Nodes wachsen {first.Orphans} -> {last.Orphans}");
            GD.Print($"[LongRun] maxNodes={maxNodes} maxObjects={maxObj}");
        }
        if (_shifts < 5) Fail($"zu wenige Origin-Shifts ({_shifts})");
        if (_maxAbsZ > GameConstants.OriginShiftThreshold + 50f) Fail($"|Z| zu groß ({_maxAbsZ:F0})");
        double expected = _startDistanceRef + _travelled;
        GD.Print($"[LongRun] distance={gm.Distance} expected≈{expected:F0} offset={gm.OriginOffsetZ} shifts={_shifts}");
        if (Math.Abs(gm.Distance - expected) > 45) Fail($"Distanz inkonsistent: {gm.Distance} vs erwartet {expected:F0}");
        if (_exceptions > 0) Fail($"{_exceptions} Exceptions: {_firstException}");
        GD.Print(_fail.Count == 0 ? "LONGRUN_TEST_PASS" : "LONGRUN_TEST_FAIL: " + string.Join("; ", _fail));
        GetTree().Quit(_fail.Count == 0 ? 0 : 1);
    }

    private void Fail(string s) { if (!_fail.Contains(s)) { _fail.Add(s); GD.PrintErr("[LongRun] " + s); } }
}
