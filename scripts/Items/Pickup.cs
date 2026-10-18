using Godot;
using JumpRun.Game;
using JumpRun.Player;
using JumpRun.World;

namespace JumpRun.Items;

/// <summary>Schwebendes, rotierendes Item. Wird beim Berühren eingesammelt (Inventar ist unbegrenzt).</summary>
public partial class Pickup : Area3D
{
    private ItemType _item = ItemType.Bridge;
    public ItemType Item
    {
        get => _item;
        set { _item = value; ApplyMaterial(); }
    }

    private MeshInstance3D? _mesh;
    private Node3D? _visual;
    private float _time;
    private float _retry;
    // R V2-12: Das Sicherheitsnetz (Spawn im Spieler) braucht nur die erste Sekunde; danach reicht BodyEntered.
    private float _pollTimeLeft = 1.0f;

    public override void _Ready()
    {
        AddToGroup("pickup");
        CollisionLayer = GameConstants.LayerPickup;
        CollisionMask = GameConstants.LayerPlayer;
        Monitoring = true;
        _visual = GetNodeOrNull<Node3D>("Visual");
        _mesh = GetNodeOrNull<MeshInstance3D>("Visual/Mesh");
        ApplyMaterial();
    }

    // R-07: Verbinden in _EnterTree, Trennen in _ExitTree – paarig auch nach Reparent/Pooling.
    public override void _EnterTree()
    {
        BodyEntered += OnBodyEntered;
    }

    public override void _ExitTree()
    {
        BodyEntered -= OnBodyEntered;
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        if (_visual != null)
        {
            _visual.Position = new Vector3(0f, 0.6f + Mathf.Sin(_time * 2.5f) * 0.15f, 0f);
            _visual.Rotation = new Vector3(0f, _time * 1.8f, 0f);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        // Sicherheitsnetz: falls BodyEntered verpasst wurde (z. B. Spawn im Spieler), periodisch prüfen.
        if (_pollTimeLeft <= 0f) { SetPhysicsProcess(false); return; }
        _pollTimeLeft -= (float)delta;
        _retry -= (float)delta;
        if (_retry > 0f) return;
        _retry = 0.25f;
        foreach (Node3D b in GetOverlappingBodies())
            if (TryCollect(b)) return;
    }

    private void OnBodyEntered(Node3D body) => TryCollect(body);

    private bool TryCollect(Node3D body)
    {
        if (IsQueuedForDeletion() || body is not PlayerController p || !IsInstanceValid(p)) return false;
        if (!p.Inventory.Add(_item)) return false;
        QueueFree();
        return true;
    }

    private void ApplyMaterial()
    {
        if (_mesh == null) return;
        string name = _item switch
        {
            ItemType.Bridge => MaterialNames.PickupBridge,
            ItemType.Trampoline => MaterialNames.PickupTrampoline,
            ItemType.Helicopter => MaterialNames.PickupHelicopter,
            _ => MaterialNames.PickupGrapple,
        };
        _mesh.MaterialOverride = MaterialLibrary.Get(name);
    }
}
