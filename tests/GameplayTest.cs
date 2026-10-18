using System;
using System.Collections.Generic;
using Godot;
using JumpRun.Game;
using JumpRun.Items;
using JumpRun.Player;
using JumpRun.World;

namespace JumpRun.Tests;

/// <summary>
/// Headless-Gameplay-Test mit echter Physik: Stehen, Laufen, Springen, Kistenschlag,
/// Brücke, Trampolin, Enterhaken, Sturz/Respawn. Ausgabe GAMEPLAY_TEST_PASS / GAMEPLAY_TEST_FAIL.
/// </summary>
public partial class GameplayTest : Node
{
    private Main? _main;
    private PlayerController? _p;
    private readonly List<string> _fail = new();
    private readonly List<(string Name, float Duration, Action Start, Func<string?> Check, Action<float>? Tick)> _steps = new();
    private int _step = -1;
    private float _t;
    private float _maxY;
    private Vector3 _mark;
    private bool _done;

    public override void _Ready()
    {
        GameManager.Current.PersistenceEnabled = false;
        GameManager.Current.StartNewRun(4242);
        _main = ResourceLoader.Load<PackedScene>(GameManager.MainScenePath).Instantiate<Main>();
        AddChild(_main);
        _p = _main.Player!;

        Add("Stehen", 1.0f, () => { }, () => _p.IsOnFloor() && Mathf.Abs(_p.GlobalPosition.Y) < 0.2f ? null : $"steht nicht (y={_p.GlobalPosition.Y:F2})");
        Add("Laufen", 1.5f, () => { _mark = _p.GlobalPosition; Input.ActionPress("move_forward"); },
            () => { Input.ActionRelease("move_forward"); float d = _mark.Z - _p.GlobalPosition.Z; return d > 7f ? null : $"nur {d:F1} m gelaufen"; });
        Add("Springen", 1.2f, () => { _maxY = 0; Input.ActionPress("jump"); },
            () => { Input.ActionRelease("jump"); return _maxY > 1.9f && _maxY < 2.6f ? null : $"Sprunghöhe {_maxY:F2}"; },
            _ => { _maxY = Mathf.Max(_maxY, _p.GlobalPosition.Y); });
        Add("Kiste", 0.6f, () =>
        {
            _p.Inventory.Clear();
            var crate = ResourceLoader.Load<PackedScene>("res://scenes/Crate.tscn").Instantiate<Crate>();
            crate.ForcedDrop = ItemType.GrappleHook;
            _main.Generator!.GetChunk(0)!.AddChild(crate);
            crate.GlobalPosition = _p.GlobalPosition + _p.Facing * 1.3f + Vector3.Up * 0.5f;
            Input.ActionPress("attack");
        }, () =>
        {
            Input.ActionRelease("attack");
            return GetTree().GetNodesInGroup("pickup").Count > 0 || _p.Inventory.GetCount(ItemType.GrappleHook) > 0 ? null : "Kiste nicht zerstört / kein Pickup";
        });
        Add("Pickup", 1.0f, () =>
        {
            foreach (Node n in GetTree().GetNodesInGroup("pickup"))
                if (n is Pickup pk) _p.GlobalPosition = pk.GlobalPosition - Vector3.Up * 0.3f;
        }, () => _p.Inventory.GetCount(ItemType.GrappleHook) == 1 ? null : "Pickup nicht eingesammelt");
        Add("Brücke", 0.5f, () =>
        {
            _p.GlobalPosition = new Vector3(0, 0.05f, -10f);
            _p.Inventory.Add(ItemType.Bridge, 2);
            _p.Inventory.Select(ItemType.Bridge);
        }, () => null);
        Add("Brücke2", 0.5f, () => Input.ActionPress("use_item"), () =>
        {
            Input.ActionRelease("use_item");
            int n = GetTree().GetNodesInGroup("placeable").Count;
            return n >= 1 && _p.Inventory.GetCount(ItemType.Bridge) == 1 ? null : $"Brücke nicht platziert (placeables={n})";
        });
        Add("Trampolin", 0.5f, () =>
        {
            foreach (Node n in GetTree().GetNodesInGroup("placeable")) n.QueueFree();
            _p.GlobalPosition = new Vector3(-3f, 0.05f, -25f); // neben der Helikopter-Tutorialkiste (0, 0.5, -26)
            _p.Velocity = Vector3.Zero;
            _p.Inventory.Add(ItemType.Trampoline, 1);
            _p.Inventory.Select(ItemType.Trampoline);
        }, () => null);
        Add("Trampolin2", 0.4f, () => Input.ActionPress("use_item"), () =>
        {
            Input.ActionRelease("use_item");
            return _p.Inventory.GetCount(ItemType.Trampoline) == 0 ? null : "Trampolin nicht platziert";
        });
        Add("Trampolin3", 2.0f, () =>
        {
            _maxY = 0;
            foreach (Node n in GetTree().GetNodesInGroup("placeable"))
                if (n is Trampoline t) _p.GlobalPosition = t.GlobalPosition + Vector3.Up * 2f;
        }, () => _maxY > 7f ? null : $"Trampolin-Sprung nur {_maxY:F1} m", _ => _maxY = Mathf.Max(_maxY, _p.GlobalPosition.Y));
        Add("Enterhaken", 2.0f, () =>
        {
            _p.GlobalPosition = new Vector3(0, 0.05f, -8f);
            _p.Velocity = Vector3.Zero;
            _p.CameraRig!.SnapBehind(Vector3.Forward);
            var anchor = ResourceLoader.Load<PackedScene>("res://scenes/objects/GrappleAnchor.tscn").Instantiate<GrappleAnchor>();
            _main.Generator!.GetChunk(0)!.AddChild(anchor);
            anchor.GlobalPosition = new Vector3(0, 3.5f, -22f);
            _p.Inventory.Select(ItemType.GrappleHook);
            _mark = anchor.GlobalPosition;
        }, () => null, t =>
        {
            if (t > 0.3f && t < 0.35f)
            {
                if (GrappleHook.FindTarget(_p) == null) Fail("Enterhaken: kein Ziel gefunden");
                Input.ActionPress("use_item");
            }
            if (t > 0.4f) Input.ActionRelease("use_item");
            _maxY = Mathf.Min(_mark.DistanceTo(_p.GlobalPosition + Vector3.Up), t < 0.3f ? 999f : _maxY);
        });
        Add("Enterhaken2", 0.1f, () => { }, () => _maxY < 3f && _p.Inventory.GetCount(ItemType.GrappleHook) == 0 ? null : $"Enterhaken: Abstand min {_maxY:F1} m");
        Add("Respawn", 3.0f, () =>
        {
            _p.GlobalPosition = new Vector3(30f, 0f, -10f); // neben der Strecke → Sturz
            _p.Velocity = Vector3.Zero;
        }, () => _p.IsOnFloor() && _p.GlobalPosition.DistanceTo(_p.RespawnPosition) < 2.5f && GameManager.Current.Falls == 1
            ? null : $"Respawn fehlgeschlagen (pos={_p.GlobalPosition}, falls={GameManager.Current.Falls})");
    }

    private void Add(string name, float dur, Action start, Func<string?> check, Action<float>? tick = null) =>
        _steps.Add((name, dur, start, check, tick));

    private void Fail(string s) { if (!_fail.Contains(s)) _fail.Add(s); }

    public override void _PhysicsProcess(double delta)
    {
        if (_done) return;
        _t += (float)delta;
        if (_step >= 0) _steps[_step].Tick?.Invoke(_t);
        if (_step >= 0 && _t < _steps[_step].Duration) return;
        if (_step >= 0)
        {
            string? r = _steps[_step].Check();
            GD.Print($"[GameplayTest] {_steps[_step].Name}: {(r == null ? "ok" : r)}");
            if (r != null) Fail($"{_steps[_step].Name}: {r}");
        }
        _step++;
        _t = 0;
        if (_step >= _steps.Count)
        {
            _done = true;
            GD.Print(_fail.Count == 0 ? "GAMEPLAY_TEST_PASS" : "GAMEPLAY_TEST_FAIL: " + string.Join("; ", _fail));
            GetTree().Quit(_fail.Count == 0 ? 0 : 1);
            return;
        }
        _steps[_step].Start();
    }
}
