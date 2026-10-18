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
/// G1/G2/G3 mit echter Physik (--fixed-fps 60): Helikopterflug (Steigen/Sinken/Deckel/Tankdauer, Schwerkraft danach),
/// Flugende bei Respawn/Bounce/Enterhaken, Taste-4-Umschalten ohne Verbrauch, Signale genau einmal je Flug,
/// Rotor-Visual, AnimMode, Überqueren echter BigGap- (≈13,5 m) und TallWall-Layouts (≈7 m),
/// unbegrenztes Inventar inkl. Pickup bei großem Bestand.
/// Start: godot --headless --fixed-fps 60 --path . res://tests/HelicopterTest.tscn → HELICOPTER_TEST_PASS
/// </summary>
public partial class HelicopterTest : Node3D
{
    private Node3D _chunks = null!;
    private PlayerController _p = null!;
    private readonly List<string> _fail = new();
    private int _flightStarted, _flightEnded, _itemUsed, _failedUse, _grappleStarted, _grappleEnded, _bounced, _attacked;
    private int _itemAdded;
    private const float Dt = 1f / 60f;

    public override void _Ready()
    {
        GameManager.Current.PersistenceEnabled = false;
        GameManager.Current.StartNewRun(11);
        if (!InputMap.HasAction("item_4"))
        {
            InputMap.AddAction("item_4");
            InputMap.ActionAddEvent("item_4", new InputEventKey { PhysicalKeycode = Key.Key4 });
        }
        var world = new Node3D { Name = "WorldRoot" };
        AddChild(world);
        _chunks = new Node3D { Name = "Chunks" };
        world.AddChild(_chunks);
        var placeables = new Node3D { Name = "Placeables" };
        world.AddChild(placeables);
        placeables.AddToGroup(Placeables.RootGroup);
        _p = ResourceLoader.Load<PackedScene>("res://scenes/Player.tscn").Instantiate<PlayerController>();
        AddChild(_p);
        // Signale per String-Namen (wie AUDIO) – prüft zugleich Namen/Arität.
        _p.Connect("FlightStarted", Callable.From(() => _flightStarted++));
        _p.Connect("FlightEnded", Callable.From(() => _flightEnded++));
        _p.Connect("ItemUsed", Callable.From((int t) => _itemUsed++));
        _p.Connect("ItemUseFailed", Callable.From((string r) => _failedUse++));
        _p.Connect("GrappleStarted", Callable.From(() => _grappleStarted++));
        _p.Connect("GrappleEnded", Callable.From(() => _grappleEnded++));
        _p.Connect("Bounced", Callable.From(() => _bounced++));
        _p.Connect("Attacked", Callable.From(() => _attacked++));
        _p.Inventory.Connect("ItemAdded", Callable.From((int t, int n) => _itemAdded++));
        Run();
    }

    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    private async Task Frames(int n) { for (int i = 0; i < n; i++) await Frame(); }
    private void Check(bool ok, string msg) { GD.Print($"[Heli] {(ok ? "PASS" : "FAIL")} {msg}"); if (!ok) _fail.Add(msg); }

    private async Task<ChunkLayout> Load(ChunkLayout l)
    {
        foreach (Node n in _chunks.GetChildren()) n.QueueFree();
        foreach (Node n in GetTree().GetNodesInGroup("placeable")) n.QueueFree();
        await Frames(2);
        _chunks.AddChild(ChunkBuilder.Instantiate(l));
        await Frames(2);
        return l;
    }

    private async Task Place(Vector3 pos)
    {
        _p.StopGrapple(false);
        _p.StopFlight();
        _p.GlobalPosition = pos;
        _p.Velocity = Vector3.Zero;
        _p.CameraRig!.SnapBehind(Vector3.Forward);
        _p.SetCheckpoint(pos + Vector3.Down, 0);
        await Frames(20);
    }

    private void ReleaseAll()
    {
        foreach (string a in new[] { "move_forward", "move_back", "sprint", "jump", "use_item", "item_4" }) Input.ActionRelease(a);
    }

    private async void Run()
    {
        try
        {
            await Load(new ChunkBuilder(1).Plan(0, Vector3.Zero, null, 0, false, 0f)); // Start-Chunk (30 × 12 m)
            await Inventory();
            await FuelAndPhysics();
            await ToggleAndUseItem();
            await FlightEnders();
            await RotorAndAnim();
            await CrossBigGap();
            await CrossTallWall();
            ReleaseAll();
            GD.Print(_fail.Count == 0 ? "HELICOPTER_TEST_PASS" : "HELICOPTER_TEST_FAIL: " + string.Join("; ", _fail));
            GetTree().Quit(_fail.Count == 0 ? 0 : 1);
        }
        catch (Exception e)
        {
            GD.PrintErr("HELICOPTER_TEST_FAIL: " + e);
            GetTree().Quit(1);
        }
    }

    // ---------------------------------------------------------------- G1

    private async Task Inventory()
    {
        await Place(new Vector3(-4f, 0.05f, -6f));
        Inventory inv = _p.Inventory;
        inv.Clear();
        int added0 = _itemAdded;
        for (int i = 0; i < 50; i++) inv.Add(ItemType.Bridge);
        Check(inv.GetCount(ItemType.Bridge) == 50 && _itemAdded - added0 == 50, $"50× Add(Bridge) = {inv.GetCount(ItemType.Bridge)}, ItemAdded={_itemAdded - added0}");
        inv.Add(ItemType.Bridge, 500);
        var pickup = ResourceLoader.Load<PackedScene>("res://scenes/Pickup.tscn").Instantiate<Pickup>();
        pickup.Item = ItemType.Bridge;
        _chunks.GetChild(0).AddChild(pickup);
        pickup.GlobalPosition = _p.GlobalPosition + new Vector3(0f, 0.3f, -2f);
        await Frames(3);
        Input.ActionPress("move_forward");
        await Frames(30);
        Input.ActionRelease("move_forward");
        await Frames(5);
        Check(inv.GetCount(ItemType.Bridge) == 551 && !IsInstanceValid(pickup), $"Pickup bei Bestand 550 eingesammelt (count={inv.GetCount(ItemType.Bridge)})");
        inv.Clear();
    }

    // ---------------------------------------------------------------- G2 Physik

    private async Task FuelAndPhysics()
    {
        // Sinken ohne Eingabe in der Luft
        await Place(new Vector3(0f, 0.05f, -10f));
        _p.GlobalPosition = new Vector3(0f, 12f, -10f);
        _p.Velocity = Vector3.Zero;
        _p.Inventory.Add(ItemType.Helicopter, 1);
        int s0 = _flightStarted, e0 = _flightEnded;
        _p.ActivateItem(ItemType.Helicopter);
        Check(_p.IsFlying && _p.Inventory.GetCount(ItemType.Helicopter) == 0 && _flightStarted == s0 + 1, "Flug startet in der Luft, 1 Ladung verbraucht");
        await Frames(60);
        Check(Mathf.Abs(_p.Velocity.Y + GameConstants.HelicopterSinkSpeed) < 0.05f, $"Sinkrate {_p.Velocity.Y:F2} ≈ -1,2 m/s");
        Input.ActionPress("jump");
        await Frames(45);
        Check(_p.Velocity.Y <= GameConstants.HelicopterAscendSpeed + 0.01f && _p.Velocity.Y > GameConstants.HelicopterAscendSpeed - 0.05f, $"Steigrate {_p.Velocity.Y:F2} ≈ 6 m/s");
        Input.ActionRelease("jump");

        // Tankdauer 3,5 s ±1 Frame und Deckel +14 m bei gehaltener Leertaste (am Boden gestartet)
        await Place(new Vector3(0f, 0.05f, -10f));
        _p.Inventory.Add(ItemType.Helicopter, 1);
        float startY = _p.GlobalPosition.Y;
        e0 = _flightEnded;
        _p.ActivateItem(ItemType.Helicopter);
        Input.ActionPress("jump");
        int frames = 0;
        float maxY = startY;
        bool fuelMonotonic = true;
        float lastFuel = 1.01f;
        while (_p.IsFlying && frames < 400)
        {
            await Frame();
            frames++;
            maxY = Mathf.Max(maxY, _p.GlobalPosition.Y);
            if (_p.IsFlying)
            {
                if (_p.FlightFuelRatio > lastFuel + 1e-4f) fuelMonotonic = false;
                lastFuel = _p.FlightFuelRatio;
            }
        }
        Input.ActionRelease("jump");
        int expected = Mathf.RoundToInt(GameConstants.HelicopterFlightTime * 60f);
        Check(Mathf.Abs(frames - expected) <= 1, $"Flugdauer {frames} Frames (erwartet {expected} ±1)");
        Check(fuelMonotonic && _p.FlightFuelRatio == 0f && _p.FlightTimeLeft == 0f, "Tank sinkt monoton, danach 0");
        Check(maxY <= startY + GameConstants.HelicopterMaxRise + 0.15f && maxY >= startY + GameConstants.HelicopterMaxRise - 0.5f,
            $"Deckel: maxRise={maxY - startY:F2} m (≤ 14)");
        Check(_flightEnded == e0 + 1, "FlightEnded genau einmal bei leerem Tank");
        float vy0 = _p.Velocity.Y;
        await Frames(30);
        Check(_p.Velocity.Y < vy0 - 8f, $"Nach Flugende normale Schwerkraft (vy {vy0:F1} → {_p.Velocity.Y:F1})");
        Check(!_p.IsFlying, "Flug beendet");
    }

    // ---------------------------------------------------------------- Taste 4 / use_item

    private async Task ToggleAndUseItem()
    {
        await Place(new Vector3(0f, 0.05f, -10f));
        _p.Inventory.Clear();
        _p.Inventory.Add(ItemType.Helicopter, 2);
        int fail0 = _failedUse, used0 = _itemUsed, s0 = _flightStarted, e0 = _flightEnded;
        // Echter Eingabepfad über _UnhandledInput (InputMap-Aktion item_4).
        Input.ParseInputEvent(new InputEventAction { Action = "item_4", Pressed = true });
        await Frame();
        Input.ParseInputEvent(new InputEventAction { Action = "item_4", Pressed = false });
        await Frames(5);
        Check(_p.IsFlying && _p.Inventory.GetCount(ItemType.Helicopter) == 1 && _p.Inventory.Selected == ItemType.Helicopter, "Taste 4 wählt und startet");
        Check(_itemUsed == used0 + 1, "ItemUsed(4) gesendet");
        _p.ActivateItem(ItemType.Helicopter);
        await Frame();
        Check(!_p.IsFlying && _p.Inventory.GetCount(ItemType.Helicopter) == 1 && _failedUse == fail0, "Erneutes Drücken beendet ohne Verbrauch/Fehlertext");
        // use_item-Pfad
        Input.ActionPress("use_item");
        await Frame();
        Input.ActionRelease("use_item");
        await Frames(3);
        Check(_p.IsFlying && _p.Inventory.GetCount(ItemType.Helicopter) == 0, "use_item startet Flug");
        Input.ActionPress("use_item");
        await Frame();
        Input.ActionRelease("use_item");
        await Frames(3);
        Check(!_p.IsFlying && _failedUse == fail0, "use_item beendet Flug ohne Fehlertext");
        Check(_flightStarted - s0 == 2 && _flightEnded - e0 == 2, $"Signale je Flug genau einmal ({_flightStarted - s0}/{_flightEnded - e0})");
        // Keine Ladung → Fehlertext
        _p.ActivateItem(ItemType.Helicopter);
        Check(!_p.IsFlying && _failedUse == fail0 + 1, "0 Ladungen → ItemUseFailed");
        await Frames(30);
    }

    // ---------------------------------------------------------------- Flugende

    private GrappleAnchor SpawnAnchor(Vector3 pos)
    {
        var a = ResourceLoader.Load<PackedScene>("res://scenes/objects/GrappleAnchor.tscn").Instantiate<GrappleAnchor>();
        _chunks.GetChild(0).AddChild(a);
        a.GlobalPosition = pos;
        return a;
    }

    private async Task FlightEnders()
    {
        // Respawn
        await Place(new Vector3(0f, 0.05f, -10f));
        _p.Inventory.Add(ItemType.Helicopter, 5);
        int e0 = _flightEnded;
        _p.ActivateItem(ItemType.Helicopter);
        await Frames(10);
        _p.Respawn(false);
        await Frame();
        Check(!_p.IsFlying && _flightEnded == e0 + 1, "Respawn beendet Flug");
        // Trampolin-Bounce
        await Place(new Vector3(0f, 0.05f, -10f));
        int b0 = _bounced;
        _p.ActivateItem(ItemType.Helicopter);
        await Frames(5);
        _p.ApplyBounce(GameConstants.TrampolineBounceVelocity);
        await Frame();
        Check(!_p.IsFlying && _flightEnded == e0 + 2 && _bounced == b0 + 1, "Bounce beendet Flug");
        // Enterhaken-Start beendet Flug
        await Place(new Vector3(0f, 0.05f, -8f));
        GrappleAnchor a = SpawnAnchor(new Vector3(0f, 4f, -20f));
        await Frames(2);
        int g0 = _grappleStarted, ge0 = _grappleEnded;
        _p.ActivateItem(ItemType.Helicopter);
        await Frames(5);
        _p.StartGrapple(a);
        await Frame();
        Check(!_p.IsFlying && _p.IsGrappling && _flightEnded == e0 + 3 && _grappleStarted == g0 + 1, "StartGrapple beendet Flug");
        // Taste 4 während Enterhaken → Haken endet, Flug startet
        _p.ActivateItem(ItemType.Helicopter);
        await Frame();
        Check(_p.IsFlying && !_p.IsGrappling && _grappleEnded == ge0 + 1, "Taste 4 im Enterhaken: Haken endet, Flug startet");
        _p.StopFlight();
        a.QueueFree();
        await Frames(30);
    }

    // ---------------------------------------------------------------- Rotor / AnimState

    private async Task RotorAndAnim()
    {
        await Place(new Vector3(0f, 0.05f, -10f));
        Node3D? rotor = _p.GetNodeOrNull<Node3D>("HelicopterRotor");
        bool sceneExists = ResourceLoader.Exists("res://scenes/objects/HelicopterRotor.tscn");
        Check(!sceneExists || rotor != null, "Player/HelicopterRotor angelegt");
        IPlayerAnimState st = _p;
        st.ModelTopHeight = 1.7f;
        if (rotor != null)
        {
            Check(!rotor.Visible, "Rotor am Boden unsichtbar");
            Check(Mathf.IsEqualApprox(rotor.Position.Y, 1.7f), $"Rotor auf ModelTopHeight ({rotor.Position.Y:F2})");
        }
        Check(st.AnimMode == PlayerAnimMode.Idle, $"AnimMode Idle ({st.AnimMode})");
        int a0 = _attacked, ac0 = st.AttackCount;
        Input.ActionPress("attack");
        await Frame();
        Input.ActionRelease("attack");
        await Frames(2);
        Check(_attacked == a0 + 1 && st.AttackCount == ac0 + 1, "Attacked/AttackCount auch ohne Treffer");
        int j0 = st.JumpCount, l0 = st.LandCount;
        Input.ActionPress("jump");
        await Frames(4);
        Check(st.AnimMode == PlayerAnimMode.Jump && st.JumpCount == j0 + 1, $"AnimMode Jump ({st.AnimMode})");
        Input.ActionRelease("jump");
        for (int i = 0; i < 90 && st.AnimMode != PlayerAnimMode.Fall; i++) await Frame();
        Check(st.AnimMode == PlayerAnimMode.Fall, "AnimMode Fall");
        for (int i = 0; i < 90 && !_p.IsOnFloor(); i++) await Frame();
        await Frames(2);
        Check(st.LandCount == l0 + 1, "LandCount nach Landung");
        Input.ActionPress("move_forward");
        await Frames(20);
        Check(st.AnimMode == PlayerAnimMode.Run && st.MoveSpeedRatio > 0.5f && st.HorizontalSpeed > 5f, $"AnimMode Run ({st.AnimMode}, {st.HorizontalSpeed:F1} m/s)");
        Input.ActionRelease("move_forward");
        _p.Inventory.Add(ItemType.Helicopter, 1);
        _p.ActivateItem(ItemType.Helicopter);
        await Frames(3);
        Check(st.AnimMode == PlayerAnimMode.Fly && st.IsFlying && st.FlightFuelRatio > 0.9f, $"AnimMode Fly ({st.AnimMode}, fuel {st.FlightFuelRatio:F2})");
        if (rotor != null) Check(rotor.Visible, "Rotor im Flug sichtbar");
        _p.StopFlight();
        await Frame();
        if (rotor != null) Check(!rotor.Visible, "Rotor nach Flug unsichtbar");
        Check(st.FlightFuelRatio == 0f, "FlightFuelRatio 0 außerhalb des Flugs");
        st.ModelTopHeight = 2f;
        await Frames(40);
    }

    // ---------------------------------------------------------------- echte Layouts

    private static float FrontEdge(PlatformSpec p) => p.Center.Z - p.Size.Z / 2f;
    private static float BackEdge(PlatformSpec p) => p.Center.Z + p.Size.Z / 2f;

    private static ChunkLayout? FindLayout(PatternType pt, Func<ChunkLayout, bool> accept)
    {
        for (int seed = 1; seed < 600; seed++)
        {
            int idx = 10 + seed % 7;
            ChunkLayout l = new ChunkBuilder(seed).Plan(idx, Vector3.Zero, PatternType.GapJump, 1, false, 1f);
            if (l.Pattern == pt && accept(l)) return l;
        }
        return null;
    }

    private async Task CrossBigGap()
    {
        ChunkLayout? l = FindLayout(PatternType.BigGap, x => x.ItemObstacleSize >= GameConstants.BigGapMax - 0.3f);
        if (l == null) { Check(false, "kein BigGap-Layout ≥ 13,2 m gefunden"); return; }
        await Load(l);
        PlatformSpec supply = l.Platforms[1], landing = l.Platforms[2];
        await Place(new Vector3(supply.Center.X, supply.TopY + 0.05f, FrontEdge(supply) + 0.6f));
        _p.Inventory.Clear();
        _p.Inventory.Add(ItemType.Helicopter, 1);
        _p.ActivateItem(ItemType.Helicopter);
        Input.ActionPress("move_forward");
        Input.ActionPress("jump");
        bool reached = false;
        for (int i = 0; i < 360; i++)
        {
            await Frame();
            if (i == 25) Input.ActionRelease("jump"); // kurz steigen, dann gleiten
            if (_p.GlobalPosition.Z < BackEdge(landing) - 1f && _p.IsOnFloor() && Mathf.Abs(_p.GlobalPosition.Y - landing.TopY) < 0.3f) { reached = true; break; }
            if (_p.GlobalPosition.Y < landing.TopY - 3f) break;
        }
        ReleaseAll();
        _p.StopFlight();
        Check(reached, $"Helikopter überquert BigGap {l.ItemObstacleSize:F2} m (end z={_p.GlobalPosition.Z:F1} y={_p.GlobalPosition.Y:F1})");
    }

    private async Task CrossTallWall()
    {
        ChunkLayout? l = FindLayout(PatternType.TallWall, x => x.ItemObstacleSize >= 6.6f);
        if (l == null) { Check(false, "kein TallWall-Layout ≥ 6,6 m gefunden"); return; }
        await Load(l);
        PlatformSpec floor = l.Platforms[2], wall = l.Platforms[3];
        await Place(new Vector3(0f, floor.TopY + 0.05f, BackEdge(wall) + 3.5f));
        _p.Inventory.Clear();
        _p.Inventory.Add(ItemType.Helicopter, 1);
        _p.ActivateItem(ItemType.Helicopter);
        Input.ActionPress("jump");
        Input.ActionPress("move_forward");
        bool reached = false;
        float wallTop = wall.TopY;
        for (int i = 0; i < 420; i++)
        {
            await Frame();
            if (_p.GlobalPosition.Y > wallTop + 1.5f) Input.ActionRelease("jump");
            if (_p.GlobalPosition.Z < FrontEdge(wall) - 1.5f)
            {
                // Hinter der Wand abbremsen (wie ein Spieler), sonst trägt der Schwung über das Bodenende hinaus.
                Input.ActionRelease("move_forward");
                if (_p.Velocity.Z < -1f) Input.ActionPress("move_back"); else Input.ActionRelease("move_back");
            }
            if (_p.GlobalPosition.Z < FrontEdge(wall) - 0.5f && _p.IsOnFloor() && Mathf.Abs(_p.GlobalPosition.Y - floor.TopY) < 0.3f) { reached = true; break; }
            if (_p.GlobalPosition.Y < floor.TopY - 3f) break;
        }
        ReleaseAll();
        _p.StopFlight();
        Check(reached, $"Helikopter überfliegt TallWall {wall.Size.Y:F1} m (end z={_p.GlobalPosition.Z:F1} y={_p.GlobalPosition.Y:F1})");
    }
}
