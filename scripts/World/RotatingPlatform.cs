using Godot;

namespace JumpRun.World;

/// <summary>Um die lokale Y-Achse drehende Plattform (CONTRACT_V2 §4.3). Trägt den Spieler über sync_to_physics.</summary>
public partial class RotatingPlatform : AnimatableBody3D
{
    /// <summary>Drehgeschwindigkeit in rad/s (Vorzeichen = Drehsinn).</summary>
    [Export] public float AngularSpeed { get; set; } = 0.5f;

    private float _baseYaw;
    private double _time;

    public override void _Ready()
    {
        SyncToPhysics = true;
        _baseYaw = Rotation.Y;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Mathf.Abs(AngularSpeed) < 0.0001f) return;
        _time += delta;
        // Zeit modulo einer Umdrehung halten (Genauigkeit bei langen Läufen).
        double period = Mathf.Tau / Mathf.Abs(AngularSpeed);
        if (_time > period) _time -= period;
        Vector3 r = Rotation;
        r.Y = _baseYaw + (float)(_time * AngularSpeed);
        Rotation = r;
    }
}
