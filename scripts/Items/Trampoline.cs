using Godot;
using JumpRun.Game;
using JumpRun.Player;
using JumpRun.World;

namespace JumpRun.Items;

/// <summary>
/// Platziertes Trampolin. Impuls nur, wenn der Spieler wirklich auf dem Pad ist (F-02 / R-02):
/// Spielermitte horizontal innerhalb des Pad-Radius, Füße auf Pad-Höhe und nicht im Aufstieg.
/// Der Körper blockiert den Spieler nicht (Kollisionsausnahme), damit man auch darauflaufen kann;
/// seitliches Streifen oder das Platzieren direkt neben dem Spieler löst nichts aus.
/// </summary>
public partial class Trampoline : StaticBody3D
{
    /// <summary>Horizontaler Auslöseradius um die Pad-Mitte (Pad-Radius 0,8 m + Toleranz).</summary>
    public const float TriggerRadius = 0.85f;
    /// <summary>Füße des Spielers relativ zur Trampolin-Unterkante: von MinFeet bis MaxFeet.</summary>
    public const float MinFeet = -0.3f;
    public const float MaxFeet = 0.9f;
    private const float Cooldown = 0.3f;

    [Export] public float Lifetime { get; set; } = GameConstants.TrampolineLifetime;
    private float _age;
    private float _cooldown;
    private PlayerController? _player;

    public override void _Ready()
    {
        AddToGroup("placeable");
        CollisionLayer = GameConstants.LayerPlaceable;
        CollisionMask = 0;
        MeshInstance3D? baseMesh = GetNodeOrNull<MeshInstance3D>("Base");
        if (baseMesh != null) baseMesh.MaterialOverride = MaterialLibrary.Get(MaterialNames.TrampolineBase);
        MeshInstance3D? pad = GetNodeOrNull<MeshInstance3D>("Pad");
        if (pad != null) pad.MaterialOverride = MaterialLibrary.Get(MaterialNames.TrampolinePad);
        _player = GetTree().GetFirstNodeInGroup("player") as PlayerController;
        if (_player != null) AddCollisionExceptionWith(_player);
    }

    public override void _PhysicsProcess(double delta)
    {
        Placeables.TickLifetime(this, ref _age, Lifetime, delta);
        if (IsQueuedForDeletion()) return;
        _cooldown -= (float)delta;
        if (_cooldown > 0f || _player == null || !IsInstanceValid(_player)) return;
        if (ShouldBounce(GlobalPosition, _player.GlobalPosition, _player.Velocity))
        {
            _cooldown = Cooldown;
            _player.ApplyBounce(GameConstants.TrampolineBounceVelocity);
        }
    }

    /// <summary>Reine Logik (testbar): steht/landet der Spieler mit den Füßen auf dem Pad?</summary>
    public static bool ShouldBounce(Vector3 trampolinePos, Vector3 playerFeet, Vector3 playerVelocity)
    {
        if (playerVelocity.Y > 1f) return false; // Aufstieg von unten / gerade abgeprallt
        Vector3 rel = playerFeet - trampolinePos;
        if (rel.Y < MinFeet || rel.Y > MaxFeet) return false;
        return rel.X * rel.X + rel.Z * rel.Z <= TriggerRadius * TriggerRadius;
    }
}
