using Godot;
using JumpRun.Game;
using JumpRun.Items;
using JumpRun.World;

namespace JumpRun.Player;

/// <summary>
/// Spielerbewegung, Sprung (Coyote/Buffer/variable Höhe), Kistenschlag, Items, Helikopterflug, Respawn.
/// Implementiert <see cref="IPlayerAnimState"/> für das prozedurale Charakter-Rig (CONTRACT_V2 §5.2/5.3).
/// </summary>
public partial class PlayerController : CharacterBody3D, IPlayerAnimState
{
    [Signal] public delegate void JumpedEventHandler();
    [Signal] public delegate void LandedEventHandler();
    [Signal] public delegate void CrateHitEventHandler(Crate crate);
    [Signal] public delegate void RespawnedEventHandler();
    [Signal] public delegate void CheckpointReachedEventHandler(int chunkIndex);
    [Signal] public delegate void ItemUseFailedEventHandler(string reason);
    // v2 (CONTRACT_V2 §5.3)
    [Signal] public delegate void AttackedEventHandler();
    [Signal] public delegate void ItemUsedEventHandler(int itemType);
    [Signal] public delegate void GrappleStartedEventHandler();
    [Signal] public delegate void GrappleEndedEventHandler();
    [Signal] public delegate void FlightStartedEventHandler();
    [Signal] public delegate void FlightEndedEventHandler();
    [Signal] public delegate void BouncedEventHandler();

    private const string RotorScenePath = "res://scenes/objects/HelicopterRotor.tscn";

    public Inventory Inventory { get; private set; } = null!;
    public ThirdPersonCamera? CameraRig { get; private set; }
    public Camera3D? Camera => CameraRig?.Camera;
    public bool IsGrappling => _grappleTarget != null;
    public Vector3 RespawnPosition { get; private set; }
    public bool DebugAutoRun { get; set; }
    public int ActiveCheckpointChunk { get; private set; } = -1;
    public int CurrentChunkIndex { get; set; }
    /// <summary>Horizontale Blickrichtung der Spielfigur.</summary>
    public Vector3 Facing { get; private set; } = Vector3.Forward;

    // ------------------------------------------------ IPlayerAnimState
    public bool IsFlying => _flying;
    public float HorizontalSpeed => new Vector2(Velocity.X, Velocity.Z).Length();
    public float MoveSpeedRatio => Mathf.Clamp(HorizontalSpeed / GameConstants.SprintSpeed, 0f, 1f);
    public Vector3 GrappleTargetPosition =>
        _grappleTarget != null && IsInstanceValid(_grappleTarget) ? _grappleTarget.GlobalPosition : GlobalPosition;
    public float FlightFuelRatio => _flying ? Mathf.Clamp(_flightTime / GameConstants.HelicopterFlightTime, 0f, 1f) : 0f;
    public int AttackCount { get; private set; }
    public int JumpCount { get; private set; }
    public int LandCount { get; private set; }
    public PlayerAnimMode AnimMode { get; private set; } = PlayerAnimMode.Idle;
    public float ModelTopHeight
    {
        get => _modelTopHeight;
        set
        {
            _modelTopHeight = float.IsFinite(value) ? Mathf.Clamp(value, 0.5f, 5f) : 2f;
            if (_rotor != null && IsInstanceValid(_rotor)) _rotor.Position = new Vector3(0f, _modelTopHeight, 0f);
        }
    }

    /// <summary>Verbleibende Flugzeit in Sekunden (0, wenn nicht im Flug).</summary>
    public float FlightTimeLeft => _flying ? _flightTime : 0f;

    private bool _flying;
    private float _flightTime;
    private float _flightStartY;
    private float _lastGroundY;
    private float _flightGroundTime;
    private float _modelTopHeight = 2f;
    private HelicopterRotor? _rotor;

    private Node3D? _visual;
    private MeshInstance3D? _rope;
    private float _coyote;
    private float _jumpBuffer;
    private bool _jumpCutAvailable;
    private float _attackCooldown;
    private bool _wasOnFloor = true;
    private GrappleAnchor? _grappleTarget;
    private GrappleAnchor? _highlighted;

    /// <summary>True, wenn gerade ein Enterhaken-Ziel anvisiert ist (für den HUD-Indikator).</summary>
    public bool HasGrappleTarget => _highlighted != null && IsInstanceValid(_highlighted);
    private float _grappleTime;
    private float _grappleStuck;
    private float _highlightTimer;
    // Pro Instanz statt statisch (F-10/R-15) und wiederverwendet statt pro Aufruf neu (F-09/R-09).
    private readonly SphereShape3D _attackShape = new() { Radius = 1.1f };
    private PhysicsShapeQueryParameters3D? _attackQuery;
    private PhysicsRayQueryParameters3D? _ray;

    public override void _Ready()
    {
        AddToGroup("player");
        CollisionLayer = GameConstants.LayerPlayer;
        CollisionMask = GameConstants.LayerWorld | GameConstants.LayerCrate | GameConstants.LayerPlaceable;
        FloorSnapLength = 0.3f;
        Inventory = GetNodeOrNull<Inventory>("Inventory") ?? CreateInventory();
        CameraRig = GetNodeOrNull<ThirdPersonCamera>("CameraRig");
        _visual = GetNodeOrNull<Node3D>("Visual");
        _rope = GetNodeOrNull<MeshInstance3D>("GrappleRope");
        if (_rope != null)
        {
            _rope.TopLevel = true;
            _rope.Visible = false;
            _rope.MaterialOverride = MaterialLibrary.Get(MaterialNames.GrappleRope);
        }
        RespawnPosition = GlobalPosition;
        _lastGroundY = GlobalPosition.Y;
        CreateRotor();
    }

    private void CreateRotor()
    {
        if (!ResourceLoader.Exists(RotorScenePath)) return;
        if (ResourceLoader.Load<PackedScene>(RotorScenePath)?.Instantiate() is not HelicopterRotor rotor) return;
        rotor.Name = "HelicopterRotor";
        rotor.Position = new Vector3(0f, _modelTopHeight, 0f);
        rotor.Visible = false;
        rotor.Active = false;
        AddChild(rotor);
        _rotor = rotor;
    }

    /// <summary>
    /// Wiederverwendete Ray-Abfrage, die den Spieler selbst ausschließt (keine Allokation pro Aufruf).
    /// Nur synchron verwenden – der nächste Aufruf überschreibt From/To/Maske.
    /// </summary>
    internal PhysicsRayQueryParameters3D RayQuery(Vector3 from, Vector3 to, uint mask)
    {
        _ray ??= new PhysicsRayQueryParameters3D { Exclude = new Godot.Collections.Array<Rid> { GetRid() } };
        _ray.From = from;
        _ray.To = to;
        _ray.CollisionMask = mask;
        return _ray;
    }

    /// <summary>Steht der Spieler gerade auf einer beweglichen Plattform? (Origin-Shift wird dann verschoben.)</summary>
    public bool IsOnMovingPlatform()
    {
        if (!IsOnFloor()) return false;
        int n = GetSlideCollisionCount();
        for (int i = 0; i < n; i++)
            if (GetSlideCollision(i).GetCollider() is MovingPlatform or RotatingPlatform) return true;
        return false;
    }

    private Inventory CreateInventory()
    {
        var inv = new Inventory { Name = "Inventory" };
        AddChild(inv);
        return inv;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // 1/2/3/4 wählen das Gadget und setzen es sofort ein.
        if (@event.IsActionPressed("item_1")) ActivateItem(ItemType.Bridge);
        else if (@event.IsActionPressed("item_2")) ActivateItem(ItemType.Trampoline);
        else if (@event.IsActionPressed("item_3")) ActivateItem(ItemType.GrappleHook);
        else if (InputMap.HasAction("item_4") && @event.IsActionPressed("item_4")) ActivateItem(ItemType.Helicopter);
        else if (@event.IsActionPressed("item_next")) Inventory.SelectNext(1);
        else if (@event.IsActionPressed("item_prev")) Inventory.SelectNext(-1);
        else return;
        GetViewport().SetInputAsHandled();
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _attackCooldown = Mathf.Max(0f, _attackCooldown - dt);

        if (IsGrappling) ProcessGrapple(dt);
        else if (_flying) ProcessFlight(dt);
        else ProcessMovement(dt);

        if (Input.IsActionJustPressed("attack") && _attackCooldown <= 0f) TryAttack();
        if (Input.IsActionJustPressed("use_item")) UseSelectedItem();

        UpdateGrappleHighlight(dt);
        UpdateRope();

        if (GlobalPosition.Y < RespawnPosition.Y - GameConstants.FallDeathDepth)
            Respawn();

        UpdateAnimMode();
    }

    private void UpdateAnimMode()
    {
        if (IsGrappling) AnimMode = PlayerAnimMode.Grapple;
        // R V2-07: Steht man im Flug länger als 0,3 s am Boden, normale Lauf-/Idle-Pose statt Flugpose.
        else if (_flying && _flightGroundTime < 0.3f) AnimMode = PlayerAnimMode.Fly;
        else if (IsOnFloor()) AnimMode = HorizontalSpeed > 0.5f ? PlayerAnimMode.Run : PlayerAnimMode.Idle;
        else AnimMode = Velocity.Y > 0f ? PlayerAnimMode.Jump : PlayerAnimMode.Fall;
    }

    // ---------------------------------------------------------------- Bewegung

    private Vector3 WishDirection()
    {
        Vector2 input = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        if (DebugAutoRun) input = new Vector2(0f, -1f);
        Vector3 fwd = CameraRig?.FlatForward ?? Vector3.Forward;
        Vector3 right = new(-fwd.Z, 0f, fwd.X);
        Vector3 dir = right * input.X + fwd * -input.Y;
        return dir.LengthSquared() > 1f ? dir.Normalized() : dir;
    }

    private void ProcessMovement(float dt)
    {
        Vector3 v = Velocity;
        bool onFloor = IsOnFloor();

        if (onFloor) _coyote = GameConstants.CoyoteTime;
        else _coyote -= dt;

        if (!onFloor) v.Y -= GameConstants.Gravity * dt;

        bool jumpPressed = Input.IsActionJustPressed("jump");
        if (jumpPressed) _jumpBuffer = GameConstants.JumpBufferTime;
        else _jumpBuffer -= dt;

        if (DebugAutoRun && onFloor && IsEdgeAhead()) _jumpBuffer = GameConstants.JumpBufferTime;

        if (_jumpBuffer > 0f && _coyote > 0f)
        {
            v.Y = GameConstants.JumpVelocity;
            _jumpBuffer = 0f;
            _coyote = 0f;
            _jumpCutAvailable = true;
            JumpCount++;
            EmitSignal(SignalName.Jumped);
        }

        // Variable Sprunghöhe: Loslassen während des Aufstiegs halbiert vy (einmalig).
        if (_jumpCutAvailable && !DebugAutoRun && v.Y > 0f && !Input.IsActionPressed("jump"))
        {
            v.Y *= 0.5f;
            _jumpCutAvailable = false;
        }
        if (v.Y <= 0f) _jumpCutAvailable = false;

        Vector3 wish = WishDirection();
        bool sprint = Input.IsActionPressed("sprint") || DebugAutoRun;
        float speed = sprint ? GameConstants.SprintSpeed : GameConstants.WalkSpeed;
        Vector3 target = wish * speed;
        float accel = onFloor ? GameConstants.GroundAccel : GameConstants.AirAccel;
        var horiz = new Vector3(v.X, 0f, v.Z);
        if (onFloor || wish.LengthSquared() > 0.01f)
            horiz = horiz.MoveToward(target, accel * dt);
        v.X = horiz.X;
        v.Z = horiz.Z;

        Velocity = v;
        MoveAndSlide();

        UpdateFacing(wish, dt);
        UpdateLanding();
    }

    private void UpdateFacing(Vector3 wish, float dt)
    {
        if (wish.LengthSquared() <= 0.01f) return;
        Facing = new Vector3(wish.X, 0f, wish.Z).Normalized();
        if (_visual != null)
        {
            float targetYaw = Mathf.Atan2(-Facing.X, -Facing.Z);
            Vector3 r = _visual.Rotation;
            r.Y = Mathf.LerpAngle(r.Y, targetYaw, 1f - Mathf.Exp(-12f * dt));
            _visual.Rotation = r;
        }
    }

    private void UpdateLanding()
    {
        bool nowOnFloor = IsOnFloor();
        if (nowOnFloor) _lastGroundY = GlobalPosition.Y;
        if (nowOnFloor && !_wasOnFloor)
        {
            LandCount++;
            EmitSignal(SignalName.Landed);
        }
        _wasOnFloor = nowOnFloor;
    }

    // ---------------------------------------------------------------- Helikopter

    /// <summary>Startet einen Helikopterflug (Ladung verbraucht der Aufrufer, siehe HelicopterItem.TryUse).</summary>
    public void StartFlight()
    {
        if (IsGrappling) StopGrapple(true);
        if (_flying) return; // HelicopterItem.TryUse verhindert einen Start im Flug (kein Nachtanken, R V2-08)
        _flightTime = GameConstants.HelicopterFlightTime;
        // V2-01: Decke relativ zur letzten Bodenhöhe, damit verkettete Flüge in der Luft nicht immer höher steigen.
        _flightStartY = Mathf.Min(GlobalPosition.Y, _lastGroundY + GameConstants.HelicopterAirStartAllowance);
        _jumpCutAvailable = false;
        _jumpBuffer = 0f;
        if (_rotor != null && IsInstanceValid(_rotor)) { _rotor.Visible = true; _rotor.Active = true; }
        _flightGroundTime = 0f;
        _flying = true;
        EmitSignal(SignalName.FlightStarted);
    }

    /// <summary>Beendet den Flug; die vertikale Geschwindigkeit bleibt erhalten, kein Coyote-Sprung.</summary>
    public void StopFlight()
    {
        if (!_flying) return;
        _flying = false;
        _flightTime = 0f;
        _coyote = 0f;
        _jumpBuffer = 0f;
        _jumpCutAvailable = false;
        if (_rotor != null && IsInstanceValid(_rotor)) { _rotor.Active = false; _rotor.Visible = false; }
        EmitSignal(SignalName.FlightEnded);
    }

    private void ProcessFlight(float dt)
    {
        Vector3 v = Velocity;
        bool ascend = Input.IsActionPressed("jump");
        float targetVy = ascend ? GameConstants.HelicopterAscendSpeed : -GameConstants.HelicopterSinkSpeed;
        float ceiling = _flightStartY + GameConstants.HelicopterMaxRise;
        if (GlobalPosition.Y >= ceiling) targetVy = Mathf.Min(targetVy, 0f);
        v.Y = Mathf.MoveToward(v.Y, targetVy, GameConstants.HelicopterVerticalAccel * dt);
        if (GlobalPosition.Y >= ceiling) v.Y = Mathf.Min(v.Y, 0f);

        Vector3 wish = WishDirection();
        Vector3 target = wish * GameConstants.HelicopterHorizontalSpeed;
        var horiz = new Vector3(v.X, 0f, v.Z).MoveToward(target, GameConstants.HelicopterAccel * dt);
        v.X = horiz.X;
        v.Z = horiz.Z;

        Velocity = v;
        MoveAndSlide();
        UpdateFacing(wish, dt);
        UpdateLanding();
        _flightGroundTime = IsOnFloor() ? _flightGroundTime + dt : 0f;

        _flightTime -= dt;
        if (_flightTime <= 0f) StopFlight();
    }

    private bool IsEdgeAhead()
    {
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        Vector3 from = GlobalPosition + Facing * 1.2f + Vector3.Up * 0.5f;
        return space.IntersectRay(RayQuery(from, from + Vector3.Down * 1.5f, GameConstants.LayerWorld | GameConstants.LayerPlaceable)).Count == 0;
    }

    // ---------------------------------------------------------------- Kisten

    private void TryAttack()
    {
        _attackCooldown = GameConstants.AttackCooldown;
        AttackCount++;
        EmitSignal(SignalName.Attacked);
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        _attackQuery ??= new PhysicsShapeQueryParameters3D
        {
            Shape = _attackShape,
            CollisionMask = GameConstants.LayerCrate,
            CollideWithBodies = true,
            CollideWithAreas = false,
        };
        _attackQuery.Transform = new Transform3D(Basis.Identity, GlobalPosition + Vector3.Up * 0.6f + Facing * (GameConstants.AttackRange * 0.5f));
        Godot.Collections.Array<Godot.Collections.Dictionary> hits = space.IntersectShape(_attackQuery, 8);
        Crate? best = null;
        float bestDist = float.MaxValue;
        foreach (var h in hits)
        {
            if (h["collider"].AsGodotObject() is Crate c && IsInstanceValid(c) && !c.IsBroken)
            {
                float d = c.GlobalPosition.DistanceSquaredTo(GlobalPosition);
                if (d < bestDist) { bestDist = d; best = c; }
            }
        }
        if (best == null) return;
        EmitSignal(SignalName.CrateHit, best);
        best.Hit(this);
    }

    // ---------------------------------------------------------------- Items

    /// <summary>Taste 1–4: Item wählen und sofort einsetzen (öffentlich für Tests).</summary>
    public void ActivateItem(ItemType t)
    {
        if (t == ItemType.Helicopter)
        {
            // Erneutes Drücken beendet den Flug – ohne Verbrauch, ohne Fehlerhinweis.
            if (_flying) { StopFlight(); return; }
            Inventory.Select(t);
            UseItem(t);
            return;
        }
        if (!IsGrappling) Inventory.Select(t);
        UseSelectedItem();
    }

    private void UseSelectedItem()
    {
        // use_item während des Enterhakens löst ihn immer – auch mit 0 Ladungen, ohne Fehlerhinweis (F-03 / R-04).
        ItemType t = Inventory.Selected;
        if (IsGrappling)
        {
            // CONTRACT_V2 §3.3 (V2-05): Mit gewähltem Helikopter und Ladung endet der Haken und der Flug startet.
            if (t == ItemType.Helicopter && Inventory.GetCount(ItemType.Helicopter) > 0)
            {
                UseItem(ItemType.Helicopter);
                return;
            }
            StopGrapple(true);
            return;
        }
        if (t == ItemType.Helicopter && _flying)
        {
            StopFlight();
            return;
        }
        UseItem(t);
    }

    private void UseItem(ItemType t)
    {
        if (t == ItemType.None) return;
        if (Inventory.GetCount(t) <= 0)
        {
            EmitSignal(SignalName.ItemUseFailed, "Keine Ladung mehr");
            return;
        }
        bool ok;
        string reason;
        switch (t)
        {
            case ItemType.Bridge: ok = BridgeItem.TryUse(this, out reason); break;
            case ItemType.Trampoline: ok = TrampolineItem.TryUse(this, out reason); break;
            case ItemType.GrappleHook: ok = GrappleHook.TryUse(this, out reason); break;
            case ItemType.Helicopter:
                // HelicopterItem.TryUse verbraucht die Ladung selbst.
                ok = HelicopterItem.TryUse(this, out reason);
                if (ok) EmitSignal(SignalName.ItemUsed, (int)t);
                else EmitSignal(SignalName.ItemUseFailed, reason);
                return;
            default: return;
        }
        if (ok)
        {
            Inventory.Consume(t);
            EmitSignal(SignalName.ItemUsed, (int)t);
        }
        else EmitSignal(SignalName.ItemUseFailed, reason);
    }

    public void ApplyBounce(float verticalVelocity)
    {
        if (IsGrappling) StopGrapple(true);
        StopFlight();
        Vector3 v = Velocity;
        v.Y = verticalVelocity;
        Velocity = v;
        _jumpCutAvailable = false;
        _coyote = 0f;
        EmitSignal(SignalName.Bounced);
    }

    public void StartGrapple(GrappleAnchor anchor)
    {
        if (!IsInstanceValid(anchor)) return;
        StopFlight();
        bool wasActive = _grappleTarget != null;
        _grappleTarget = anchor;
        _grappleTime = 0f;
        _grappleStuck = 0f;
        _jumpCutAvailable = false;
        if (!wasActive) EmitSignal(SignalName.GrappleStarted);
    }

    public void StopGrapple(bool keepMomentum)
    {
        if (_grappleTarget == null) return;
        _grappleTarget = null;
        if (!keepMomentum) Velocity = Vector3.Zero;
        else if (Velocity.Length() > 14f) Velocity = Velocity.Normalized() * 14f;
        if (_rope != null) _rope.Visible = false;
        EmitSignal(SignalName.GrappleEnded);
    }

    private void ProcessGrapple(float dt)
    {
        GrappleAnchor? a = _grappleTarget;
        if (a == null || !IsInstanceValid(a) || a.IsQueuedForDeletion()) { StopGrapple(true); return; }
        _grappleTime += dt;
        if (Input.IsActionJustPressed("jump"))
        {
            StopGrapple(true);
            Velocity += Vector3.Up * 3f;
            return;
        }
        Vector3 toAnchor = a.GlobalPosition - (GlobalPosition + Vector3.Up * 0.9f);
        float dist = toAnchor.Length();
        if (dist < GameConstants.GrappleReleaseDistance || _grappleTime > 3f) { StopGrapple(true); return; }

        Vector3 before = GlobalPosition;
        Velocity = toAnchor / dist * GameConstants.GrapplePullSpeed;
        MoveAndSlide();
        float moved = before.DistanceTo(GlobalPosition);
        _grappleStuck = moved < GameConstants.GrapplePullSpeed * dt * 0.1f ? _grappleStuck + dt : 0f;
        if (_grappleStuck > 0.3f) StopGrapple(false);
    }

    private void UpdateGrappleHighlight(float dt)
    {
        _highlightTimer -= dt;
        if (_highlightTimer > 0f) return;
        _highlightTimer = 0.1f;
        GrappleAnchor? t = Inventory.Selected == ItemType.GrappleHook && Inventory.GetCount(ItemType.GrappleHook) > 0 && !IsGrappling
            ? GrappleHook.FindTarget(this)
            : null;
        if (_highlighted != null && IsInstanceValid(_highlighted) && _highlighted != t) _highlighted.SetHighlighted(false);
        if (t != null) t.SetHighlighted(true);
        _highlighted = t;
    }

    private void UpdateRope()
    {
        if (_rope == null) return;
        if (_grappleTarget == null || !IsInstanceValid(_grappleTarget)) { _rope.Visible = false; return; }
        Vector3 from = GlobalPosition + Vector3.Up * 1.0f;
        Vector3 to = _grappleTarget.GlobalPosition;
        Vector3 d = to - from;
        float len = d.Length();
        if (len < 0.01f) { _rope.Visible = false; return; }
        Vector3 y = d / len;
        Vector3 helper = Mathf.Abs(y.Dot(Vector3.Up)) > 0.95f ? Vector3.Right : Vector3.Up;
        Vector3 x = helper.Cross(y).Normalized();
        Vector3 z = x.Cross(y).Normalized();
        _rope.GlobalTransform = new Transform3D(new Basis(x, y * len, z), (from + to) * 0.5f);
        _rope.Visible = true;
    }

    // ---------------------------------------------------------------- Checkpoint / Respawn

    public void SetCheckpoint(Vector3 position, int chunkIndex)
    {
        RespawnPosition = position + Vector3.Up * 1.0f;
        bool changed = chunkIndex != ActiveCheckpointChunk;
        ActiveCheckpointChunk = chunkIndex;
        if (changed) EmitSignal(SignalName.CheckpointReached, chunkIndex);
    }

    public void Respawn() => Respawn(true);

    public void Respawn(bool countAsFall)
    {
        StopGrapple(false);
        StopFlight();
        GlobalPosition = RespawnPosition;
        _lastGroundY = RespawnPosition.Y;
        Velocity = Vector3.Zero;
        _jumpBuffer = 0f;
        _jumpCutAvailable = false;
        Facing = Vector3.Forward;
        if (_visual != null) _visual.Rotation = Vector3.Zero;
        CameraRig?.SnapBehind(Vector3.Forward);
        if (countAsFall) GameManager.Instance?.RegisterFall();
        EmitSignal(SignalName.Respawned);
    }

    public void ApplyOriginShift(Vector3 offset)
    {
        GlobalPosition += offset;
        RespawnPosition += offset;
        CameraRig?.ApplyOriginShift(offset);
        UpdateRope();
    }
}
