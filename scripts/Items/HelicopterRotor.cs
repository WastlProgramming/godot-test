using Godot;
using JumpRun.World;

namespace JumpRun.Items;

/// <summary>Rotor-Visual am Spieler (Player/HelicopterRotor). Dreht nur, solange Active.</summary>
public partial class HelicopterRotor : Node3D
{
    /// <summary>Drehrate der Blätter in rad/s.</summary>
    [Export] public float SpinRate { get; set; } = 28f;

    private bool _active;
    public bool Active
    {
        get => _active;
        set
        {
            _active = value;
            Visible = value;
            _spinUp = value ? 0f : _spinUp;
        }
    }

    private Node3D? _blades;
    private float _spinUp;

    public override void _Ready()
    {
        _blades = GetNodeOrNull<Node3D>("Blades");
        Material rotor = MaterialLibrary.Get(MaterialNames.HelicopterRotor);
        Material hub = MaterialLibrary.Get(MaterialNames.HelicopterHub);
        foreach (string n in new[] { "Blades/BladeA", "Blades/BladeB" })
            if (GetNodeOrNull<MeshInstance3D>(n) is MeshInstance3D m) m.MaterialOverride = rotor;
        foreach (string n in new[] { "Mast", "Hub" })
            if (GetNodeOrNull<MeshInstance3D>(n) is MeshInstance3D m) m.MaterialOverride = hub;
    }

    public override void _Process(double delta)
    {
        if (!_active || _blades == null) return;
        float dt = (float)delta;
        _spinUp = Mathf.Min(1f, _spinUp + dt * 3f);
        Vector3 r = _blades.Rotation;
        r.Y = Mathf.Wrap(r.Y + SpinRate * _spinUp * dt, -Mathf.Pi, Mathf.Pi);
        _blades.Rotation = r;
    }
}
