using Godot;

namespace JumpRun.Audio;

/// <summary>
/// Connects gameplay signals to sounds (CONTRACT_V2 §6.4). Hung into Main as <c>Main/AudioEventBridge</c>.
/// Finds the player (group <c>player</c>) and its child <c>Inventory</c> and connects by signal *name*
/// (only if <c>HasSignal</c>), so it compiles and runs even when gameplay signals do not exist yet.
/// All calls go through <c>AudioManager.Instance?.</c> (null-safe for tests without the autoload).
/// </summary>
public partial class AudioEventBridge : Node
{
    public const float LandThrottleSeconds = 0.15f;
    public const float StartupMuteSeconds = 0.5f;
    public const float GrappleAttachDelay = 0.1f;
    private const float FindPlayerTimeout = 5f;

    private static readonly StringName SigJumped = "Jumped";
    private static readonly StringName SigLanded = "Landed";
    private static readonly StringName SigAttacked = "Attacked";
    private static readonly StringName SigCrateHit = "CrateHit";
    private static readonly StringName SigItemUsed = "ItemUsed";
    private static readonly StringName SigGrappleStarted = "GrappleStarted";
    private static readonly StringName SigGrappleEnded = "GrappleEnded";
    private static readonly StringName SigFlightStarted = "FlightStarted";
    private static readonly StringName SigFlightEnded = "FlightEnded";
    private static readonly StringName SigBounced = "Bounced";
    private static readonly StringName SigRespawned = "Respawned";
    private static readonly StringName SigCheckpointReached = "CheckpointReached";
    private static readonly StringName SigItemUseFailed = "ItemUseFailed";
    private static readonly StringName SigItemAdded = "ItemAdded";

    private Node? _player;
    private Node? _inventory;
    private float _age;
    private float _searchTime;
    private float _sinceLand = 999f;
    private float _grappleAttachIn = -1f;
    private int _connectedCount;
    private bool _flying;

    private Callable _cJumped, _cLanded, _cAttacked, _cCrateHit, _cItemUsed, _cGrappleStarted, _cGrappleEnded,
        _cFlightStarted, _cFlightEnded, _cBounced, _cRespawned, _cCheckpoint, _cItemUseFailed, _cItemAdded;

    /// <summary>Number of signals connected (for tests).</summary>
    public int ConnectedCount => _connectedCount;

    /// <summary>The node the bridge listens to (player), null until found.</summary>
    public Node? Target => _player;

    /// <summary>
    /// Optional explicit target (tests). When set before <c>_Ready</c>, the group lookup is skipped.
    /// </summary>
    public Node? ExplicitTarget { get; set; }

    public override void _Ready()
    {
        _cJumped = Callable.From(OnJumped);
        _cLanded = Callable.From(OnLanded);
        _cAttacked = Callable.From(OnAttacked);
        _cCrateHit = Callable.From<Node>(OnCrateHit);
        _cItemUsed = Callable.From<int>(OnItemUsed);
        _cGrappleStarted = Callable.From(OnGrappleStarted);
        _cGrappleEnded = Callable.From(OnGrappleEnded);
        _cFlightStarted = Callable.From(OnFlightStarted);
        _cFlightEnded = Callable.From(OnFlightEnded);
        _cBounced = Callable.From(OnBounced);
        _cRespawned = Callable.From(OnRespawned);
        _cCheckpoint = Callable.From<int>(OnCheckpointReached);
        _cItemUseFailed = Callable.From<string>(OnItemUseFailed);
        _cItemAdded = Callable.From<int, int>(OnItemAdded);
        CallDeferred(MethodName.TryConnect);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _age += dt;
        _sinceLand += dt;

        if (_player == null)
        {
            _searchTime += dt;
            if (_searchTime < FindPlayerTimeout)
                TryConnect();
        }
        else if (!IsInstanceValid(_player))
        {
            _player = null;
            _inventory = null;
            _connectedCount = 0;
        }

        if (_grappleAttachIn >= 0f)
        {
            _grappleAttachIn -= dt;
            if (_grappleAttachIn < 0f)
                AudioManager.Instance?.PlaySfx(Sfx.GrappleAttach);
        }
    }

    public override void _ExitTree()
    {
        Disconnect();
        if (_flying)
        {
            AudioManager.Instance?.SetLoop(Sfx.HelicopterLoop, false);
            _flying = false;
        }
    }

    // ------------------------------------------------------------------------------------------

    private void TryConnect()
    {
        if (_player != null || !IsInsideTree()) return;
        Node? p = ExplicitTarget ?? GetTree().GetFirstNodeInGroup("player");
        if (p == null || !IsInstanceValid(p)) return;
        _player = p;
        _connectedCount = 0;
        Link(p, SigJumped, _cJumped);
        Link(p, SigLanded, _cLanded);
        Link(p, SigAttacked, _cAttacked);
        Link(p, SigCrateHit, _cCrateHit);
        Link(p, SigItemUsed, _cItemUsed);
        Link(p, SigGrappleStarted, _cGrappleStarted);
        Link(p, SigGrappleEnded, _cGrappleEnded);
        Link(p, SigFlightStarted, _cFlightStarted);
        Link(p, SigFlightEnded, _cFlightEnded);
        Link(p, SigBounced, _cBounced);
        Link(p, SigRespawned, _cRespawned);
        Link(p, SigCheckpointReached, _cCheckpoint);
        Link(p, SigItemUseFailed, _cItemUseFailed);
        _inventory = p.GetNodeOrNull("Inventory");
        if (_inventory != null)
            Link(_inventory, SigItemAdded, _cItemAdded);
    }

    private void Link(Node n, StringName signal, Callable c)
    {
        if (!n.HasSignal(signal) || n.IsConnected(signal, c)) return;
        if (n.Connect(signal, c) == Error.Ok) _connectedCount++;
    }

    private static void Unlink(Node? n, StringName signal, Callable c)
    {
        if (n != null && IsInstanceValid(n) && n.HasSignal(signal) && n.IsConnected(signal, c))
            n.Disconnect(signal, c);
    }

    private void Disconnect()
    {
        Node? p = _player;
        Unlink(p, SigJumped, _cJumped);
        Unlink(p, SigLanded, _cLanded);
        Unlink(p, SigAttacked, _cAttacked);
        Unlink(p, SigCrateHit, _cCrateHit);
        Unlink(p, SigItemUsed, _cItemUsed);
        Unlink(p, SigGrappleStarted, _cGrappleStarted);
        Unlink(p, SigGrappleEnded, _cGrappleEnded);
        Unlink(p, SigFlightStarted, _cFlightStarted);
        Unlink(p, SigFlightEnded, _cFlightEnded);
        Unlink(p, SigBounced, _cBounced);
        Unlink(p, SigRespawned, _cRespawned);
        Unlink(p, SigCheckpointReached, _cCheckpoint);
        Unlink(p, SigItemUseFailed, _cItemUseFailed);
        Unlink(_inventory, SigItemAdded, _cItemAdded);
        _player = null;
        _inventory = null;
        _connectedCount = 0;
    }

    // ------------------------------------------------------------------------------------------
    // Handlers
    // ------------------------------------------------------------------------------------------

    private void OnJumped() => AudioManager.Instance?.PlaySfx(Sfx.Jump);

    private void OnLanded()
    {
        if (_sinceLand < LandThrottleSeconds) return;
        _sinceLand = 0f;
        AudioManager.Instance?.PlaySfx(Sfx.Land);
    }

    private void OnAttacked() => AudioManager.Instance?.PlaySfx(Sfx.Swing, null, 0f, 0.08f);

    private void OnCrateHit(Node crate)
    {
        if (crate is Node3D n3 && IsInstanceValid(n3) && n3.IsInsideTree())
            AudioManager.Instance?.PlaySfx(Sfx.CrateBreak, n3.GlobalPosition, 0f, 0.08f);
        else
            AudioManager.Instance?.PlaySfx(Sfx.CrateBreak, null, 0f, 0.08f);
    }

    private void OnItemUsed(int itemType)
    {
        string? name = itemType switch
        {
            1 => Sfx.BridgePlace,
            2 => Sfx.TrampolinePlace,
            3 => Sfx.GrappleShoot,
            4 => Sfx.HelicopterStart,
            _ => null,
        };
        if (name != null) AudioManager.Instance?.PlaySfx(name);
    }

    private void OnGrappleStarted() => _grappleAttachIn = GrappleAttachDelay;

    private void OnGrappleEnded() => _grappleAttachIn = -1f; // cancelled before the hook arrived

    private void OnFlightStarted()
    {
        _flying = true;
        AudioManager.Instance?.SetLoop(Sfx.HelicopterLoop, true);
    }

    private void OnFlightEnded()
    {
        _flying = false;
        AudioManager.Instance?.SetLoop(Sfx.HelicopterLoop, false);
    }

    private void OnBounced() => AudioManager.Instance?.PlaySfx(Sfx.TrampolineBounce);

    private void OnRespawned()
    {
        if (_age < StartupMuteSeconds) return;
        AudioManager.Instance?.PlaySfx(Sfx.Respawn, null, 0f, 0f);
    }

    private void OnCheckpointReached(int chunkIndex)
    {
        if (chunkIndex <= 0 || _age < StartupMuteSeconds) return;
        AudioManager.Instance?.PlaySfx(Sfx.Checkpoint, null, 0f, 0f);
    }

    private void OnItemUseFailed(string reason) => AudioManager.Instance?.PlaySfx(Sfx.ItemFail, null, 0f, 0f);

    private void OnItemAdded(int itemType, int newCount) => AudioManager.Instance?.PlaySfx(Sfx.Pickup, null, 0f, 0.04f);
}
