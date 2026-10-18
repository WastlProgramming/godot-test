using Godot;

namespace JumpRun.World;

/// <summary>Ankerpunkt für den Enterhaken (Layer 32).</summary>
public partial class GrappleAnchor : StaticBody3D
{
    private MeshInstance3D? _mesh;
    private bool _highlighted;

    private static readonly System.Collections.Generic.List<GrappleAnchor> ActiveList = new();
    /// <summary>Alle Anker im Baum (Registry statt GetNodesInGroup → keine Array-Allokation je Suche, R-09).</summary>
    public static System.Collections.Generic.IReadOnlyList<GrappleAnchor> Active => ActiveList;

    public override void _EnterTree() => ActiveList.Add(this);
    public override void _ExitTree() => ActiveList.Remove(this);

    public override void _Ready()
    {
        AddToGroup("grapple_anchor");
        CollisionLayer = Game.GameConstants.LayerGrappleAnchor;
        CollisionMask = 0;
        _mesh = GetNodeOrNull<MeshInstance3D>("Mesh");
        ApplyMaterial();
    }

    public void SetHighlighted(bool on)
    {
        if (on == _highlighted) return;
        _highlighted = on;
        ApplyMaterial();
    }

    private void ApplyMaterial()
    {
        if (_mesh == null) return;
        _mesh.MaterialOverride = MaterialLibrary.Get(_highlighted ? MaterialNames.GrappleAnchorActive : MaterialNames.GrappleAnchor);
    }
}
