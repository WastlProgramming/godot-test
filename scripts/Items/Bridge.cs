using Godot;
using JumpRun.Game;
using JumpRun.World;

namespace JumpRun.Items;

/// <summary>Platzierte Brücke (Planke 2 × 0,3 × 16 m), Layer placeable.</summary>
public partial class Bridge : StaticBody3D
{
    [Export] public float Lifetime { get; set; } = GameConstants.BridgeLifetime;
    private float _age;

    public override void _Ready()
    {
        AddToGroup("placeable");
        CollisionLayer = GameConstants.LayerPlaceable;
        CollisionMask = 0;
        MeshInstance3D? mesh = GetNodeOrNull<MeshInstance3D>("Mesh");
        if (mesh != null) mesh.MaterialOverride = MaterialLibrary.Get(MaterialNames.Bridge);
    }

    public override void _PhysicsProcess(double delta) => Placeables.TickLifetime(this, ref _age, Lifetime, delta);
}
