using Godot;

namespace JumpRun.World;

/// <summary>Ping-Pong-Plattform (Sinus) relativ zur Startposition (lokal im Chunk).</summary>
public partial class MovingPlatform : AnimatableBody3D
{
    [Export] public Vector3 Offset { get; set; } = new(4, 0, 0);
    [Export] public float Speed { get; set; } = 2f;
    [Export] public float PhaseOffset { get; set; }

    private Vector3 _start;
    private double _time;

    public override void _Ready()
    {
        SyncToPhysics = true;
        _start = Position;
    }

    public override void _PhysicsProcess(double delta)
    {
        float len = Offset.Length();
        if (len < 0.001f || Speed <= 0f) return;
        _time += delta;
        // Max. Geschwindigkeit = len/2 * w = Speed
        float w = 2f * Speed / len;
        float t = 0.5f - 0.5f * Mathf.Cos((float)_time * w + PhaseOffset);
        Position = _start + Offset * t;
    }
}
