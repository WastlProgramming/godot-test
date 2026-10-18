using System.Collections.Generic;
using Godot;
using JumpRun.Items;

namespace JumpRun.World;

/// <summary>Ein Parkour-Abschnitt. Alle Kinder (Plattformen, Kisten, Pickups, Anker) verschwinden mit ihm.</summary>
public partial class Chunk : Node3D
{
    private const string CrateScenePath = "res://scenes/Crate.tscn";

    public int Index { get; private set; }
    public PatternType Pattern { get; private set; }
    public Vector3 EntryPoint { get; private set; }
    public Vector3 ExitPoint { get; private set; }
    public Aabb WorldBounds { get; private set; }
    public Vector3 CheckpointPosition { get; private set; }
    public bool IsItemChunk => Pattern.IsItemRequired();

    private Aabb _localBounds;
    private readonly List<(CrateSpec Spec, Crate? Instance)> _supply = new();

    internal void Init(ChunkLayout layout)
    {
        Index = layout.Index;
        Pattern = layout.Pattern;
        EntryPoint = layout.EntryPoint;
        ExitPoint = layout.ExitPoint;
        CheckpointPosition = layout.CheckpointPosition;
        _localBounds = layout.ComputeLocalBounds();
        WorldBounds = new Aabb(_localBounds.Position + layout.EntryPoint, _localBounds.Size);
    }

    public override void _EnterTree()
    {
        AddToGroup("chunk");
    }

    internal Crate? SpawnCrate(CrateSpec spec, bool givesBonus = true)
    {
        PackedScene? scene = ChunkBuilder.LoadScene(CrateScenePath);
        if (scene?.Instantiate() is not Crate crate) return null;
        crate.ForcedDrop = spec.ForcedDrop;
        crate.IsSupply = spec.IsSupply;
        crate.DropSeed = spec.DropSeed;
        crate.GivesBonus = givesBonus;
        crate.Position = spec.Position;
        AddChild(crate);
        if (spec.IsSupply)
        {
            for (int i = 0; i < _supply.Count; i++)
            {
                if (_supply[i].Spec == spec) { _supply[i] = (spec, crate); return crate; }
            }
            _supply.Add((spec, crate));
        }
        return crate;
    }

    /// <summary>
    /// Zerstörte Versorgungskisten mit garantiertem Drop neu erzeugen (Lösbarkeitsgarantie).
    /// Nachgelieferte Kisten geben keinen Punktebonus, sonst ließe sich der Score per Absichtssturz farmen (F-04).
    /// </summary>
    public void RestockSupply()
    {
        var snapshot = _supply.ToArray();
        foreach (var (spec, inst) in snapshot)
        {
            if (inst != null && IsInstanceValid(inst) && !inst.IsQueuedForDeletion()) continue;
            SpawnCrate(spec, givesBonus: false);
        }
    }

    public int SupplyCrateCount
    {
        get
        {
            int n = 0;
            foreach (var (_, inst) in _supply)
                if (inst != null && IsInstanceValid(inst) && !inst.IsQueuedForDeletion()) n++;
            return n;
        }
    }

    /// <summary>Gecachte Punkte nachziehen; die Node-Position verschiebt der Generator.</summary>
    public void ApplyOriginShift(Vector3 offset)
    {
        EntryPoint += offset;
        ExitPoint += offset;
        CheckpointPosition += offset;
        WorldBounds = new Aabb(WorldBounds.Position + offset, WorldBounds.Size);
    }
}
