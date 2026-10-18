using System;
using Godot;
using JumpRun.Game;
using JumpRun.Player;
using JumpRun.World;

namespace JumpRun.Items;

/// <summary>Zerstörbare Kiste (1 HP) mit Drop-Tabelle oder garantiertem Drop.</summary>
public partial class Crate : StaticBody3D
{
    [Signal] public delegate void BrokenEventHandler(int droppedItemType);

    private const string PickupScenePath = "res://scenes/Pickup.tscn";
    private const string BreakFxPath = "res://art/fx/crate_break.tscn";

    public ItemType? ForcedDrop { get; set; }
    public bool IsSupply { get; set; }
    public int DropSeed { get; set; }
    public bool IsBroken { get; private set; }
    /// <summary>false für nachgelieferte Versorgungskisten (kein Score-Farming, F-04).</summary>
    public bool GivesBonus { get; set; } = true;

    public override void _Ready()
    {
        AddToGroup("crate");
        CollisionLayer = GameConstants.LayerCrate;
        CollisionMask = 0;
        MeshInstance3D? mesh = GetNodeOrNull<MeshInstance3D>("Mesh");
        if (mesh != null) mesh.MaterialOverride = MaterialLibrary.Get(MaterialNames.Crate);
    }

    /// <summary>Drop-Tabelle CONTRACT_V2 §3.4: Brücke 27, Trampolin 27, Enterhaken 22, Helikopter 12, nichts 12.</summary>
    public static ItemType RollDrop(Random rng)
    {
        int r = rng.Next(100);
        if (r < 27) return ItemType.Bridge;
        if (r < 54) return ItemType.Trampoline;
        if (r < 76) return ItemType.GrappleHook;
        if (r < 88) return ItemType.Helicopter;
        return ItemType.None;
    }

    public void Hit(PlayerController by)
    {
        if (IsBroken || IsQueuedForDeletion()) return;
        IsBroken = true;
        ItemType drop = ForcedDrop ?? RollDrop(new Random(DropSeed));
        Node? parent = GetParent();
        Vector3 localPos = Position;

        if (parent != null)
        {
            SpawnEffect(parent, localPos);
            if (drop != ItemType.None) SpawnPickup(parent, localPos, drop);
        }
        if (GivesBonus) GameManager.Instance?.AddCrateBonus(GameConstants.CrateBonusScore);
        EmitSignal(SignalName.Broken, (int)drop);
        QueueFree();
    }

    private static void SpawnPickup(Node parent, Vector3 localPos, ItemType item)
    {
        PackedScene? scene = ChunkBuilder.LoadScene(PickupScenePath);
        if (scene?.Instantiate() is not Pickup p) return;
        p.Item = item;
        p.Position = localPos + new Vector3(0f, 0.3f, 0f);
        // Deferred (Hit kann aus Physik-Callbacks kommen). Ist der Chunk bis dahin weg, das Pickup freigeben
        // statt es als verwaisten Node zu verlieren (R-05).
        Callable.From(() =>
        {
            if (!IsInstanceValid(p)) return;
            if (IsInstanceValid(parent) && parent.IsInsideTree() && !parent.IsQueuedForDeletion()) parent.AddChild(p);
            else p.Free();
        }).CallDeferred();
    }

    private static void SpawnEffect(Node parent, Vector3 localPos)
    {
        if (!ResourceLoader.Exists(BreakFxPath)) return;
        if (ResourceLoader.Load<PackedScene>(BreakFxPath)?.Instantiate() is not Node3D fx) return;
        fx.Position = localPos;
        Callable.From(() =>
        {
            if (!IsInstanceValid(fx)) return;
            if (!IsInstanceValid(parent) || !parent.IsInsideTree() || parent.IsQueuedForDeletion()) { fx.Free(); return; }
            parent.AddChild(fx);
            if (fx is CpuParticles3D cpu) cpu.Emitting = true;
            else if (fx is GpuParticles3D gpu) gpu.Emitting = true;
        }).CallDeferred();
        SceneTree? tree = parent.GetTree();
        if (tree == null) return;
        tree.CreateTimer(2.0, processAlways: false).Timeout += () =>
        {
            if (IsInstanceValid(fx)) fx.QueueFree();
        };
    }
}
