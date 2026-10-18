using Godot;
using JumpRun.Player;

namespace JumpRun.World;

/// <summary>Checkpoint-Trigger auf dem Pad am Anfang jedes Chunks.</summary>
public partial class Checkpoint : Area3D
{
    public int ChunkIndex { get; set; }
    public bool IsActive { get; private set; }

    private MeshInstance3D? _flag;

    public override void _Ready()
    {
        AddToGroup("checkpoint");
        CollisionLayer = Game.GameConstants.LayerTrigger;
        CollisionMask = Game.GameConstants.LayerPlayer;
        Monitoring = true;
        _flag = GetNodeOrNull<MeshInstance3D>("Flag");
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

    private void OnBodyEntered(Node3D body)
    {
        if (body is PlayerController player) Activate(player);
    }

    /// <summary>
    /// Setzt diesen Checkpoint für den Spieler (nur vorwärts). Wird vom Trigger und – beim Überfliegen im
    /// Helikopter (V2-03) – vom WorldGenerator aufgerufen.
    /// </summary>
    public void Activate(PlayerController player)
    {
        if (IsActive && player.ActiveCheckpointChunk == ChunkIndex) return;
        // Nur vorwärts: ältere Checkpoints überschreiben einen neueren nicht.
        if (ChunkIndex < player.ActiveCheckpointChunk) return;
        player.SetCheckpoint(GlobalPosition, ChunkIndex);
        foreach (Node n in GetTree().GetNodesInGroup("checkpoint"))
        {
            if (n is Checkpoint c && IsInstanceValid(c))
                c.SetActive(c == this);
        }
    }

    public void SetActive(bool on)
    {
        if (IsActive == on) return;
        IsActive = on;
        ApplyMaterial();
    }

    private void ApplyMaterial()
    {
        if (_flag != null)
            _flag.MaterialOverride = MaterialLibrary.Get(IsActive ? MaterialNames.CheckpointFlagActive : MaterialNames.CheckpointFlag);
    }
}
