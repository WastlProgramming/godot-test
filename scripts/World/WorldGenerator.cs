using System.Collections.Generic;
using Godot;
using JumpRun.Game;
using JumpRun.Items;
using JumpRun.Player;

namespace JumpRun.World;

/// <summary>Endlos-Generierung: Spawn vorne, Despawn hinten außer Sicht, Floating Origin.</summary>
public partial class WorldGenerator : Node3D
{
    [Signal] public delegate void ChunkSpawnedEventHandler(int index);
    [Signal] public delegate void ChunkDespawnedEventHandler(int index);
    [Signal] public delegate void OriginShiftedEventHandler(Vector3 offset);

    [Export] public NodePath PlayerPath { get; set; } = new("../../Player");
    [Export] public int SeedOverride { get; set; } = -1;

    public int AliveChunkCount => _chunks.Count;
    public int HighestChunkIndex { get; private set; } = -1;
    public IReadOnlyList<Chunk> AliveChunks => _chunks;
    public int TotalSpawned { get; private set; }
    public int TotalDespawned { get; private set; }
    public int CurrentSeed { get; private set; }

    private readonly List<Chunk> _chunks = new();
    private ChunkBuilder? _builder;
    private Node3D? _chunksRoot;
    private Node3D? _placeablesRoot;
    private PlayerController? _player;
    private Vector3 _nextEntry;
    private PatternType? _lastPattern;
    private int _lastStreak;
    private bool _initialized;
    private int _despawnTick;
    private const int DespawnIntervalFrames = 10; // Despawn ist nicht zeitkritisch → Frustum nur alle 10 Frames

    public override void _Ready()
    {
        _chunksRoot = GetNodeOrNull<Node3D>("../Chunks");
        _placeablesRoot = GetNodeOrNull<Node3D>("../Placeables");
        _player = GetNodeOrNull<PlayerController>(PlayerPath);
        if (_chunksRoot == null) GD.PushError("[WorldGenerator] WorldRoot/Chunks fehlt.");
        if (_player == null) GD.PushError("[WorldGenerator] Spieler nicht gefunden: " + PlayerPath);
        if (_player != null) _player.Respawned += OnPlayerRespawned;
    }

    public override void _ExitTree()
    {
        if (_player != null && IsInstanceValid(_player)) _player.Respawned -= OnPlayerRespawned;
    }

    public void Reset(int seed)
    {
        if (SeedOverride >= 0) seed = SeedOverride;
        CurrentSeed = seed;
        foreach (Chunk c in _chunks)
            if (IsInstanceValid(c)) c.QueueFree();
        _chunks.Clear();
        if (_placeablesRoot != null)
            foreach (Node n in _placeablesRoot.GetChildren()) n.QueueFree();
        _builder = new ChunkBuilder(seed);
        _nextEntry = Vector3.Zero;
        _lastPattern = null;
        _lastStreak = 0;
        HighestChunkIndex = -1;
        _initialized = true;
        SpawnNext(); // Chunk 0 sofort (Startfläche), der Rest max. 1 pro Physik-Frame.
        if (_player != null && _chunks.Count > 0)
        {
            Chunk first = _chunks[0];
            _player.CurrentChunkIndex = 0;
            _player.SetCheckpoint(first.CheckpointPosition, 0);
            _player.Respawn(countAsFall: false);
        }
    }

    public Chunk? GetChunk(int index)
    {
        foreach (Chunk c in _chunks)
            if (c.Index == index && IsInstanceValid(c)) return c;
        return null;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_initialized || _builder == null || _player == null || !IsInstanceValid(_player)) return;

        Vector3 pp = _player.GlobalPosition;
        UpdatePlayerChunk(pp);
        GameManager.Instance?.ReportPlayerZ(pp.Z);

        // Spawn: max. ein Chunk pro Physik-Frame
        if (HighestChunkIndex < _player.CurrentChunkIndex + GameConstants.ChunksAhead)
            SpawnNext();

        if (++_despawnTick >= DespawnIntervalFrames || _chunks.Count > GameConstants.MaxAliveChunks)
        {
            _despawnTick = 0;
            Despawn(pp);
        }

        // Nicht verschieben, solange der Spieler auf einer beweglichen Plattform steht: die Teleportation
        // der AnimatableBody würde sonst als riesige Plattformgeschwindigkeit auf den Spieler übertragen (R-01).
        if (Mathf.Abs(pp.Z) > GameConstants.OriginShiftThreshold && !_player.IsOnMovingPlatform())
            ShiftOrigin(new Vector3(0f, 0f, -Mathf.Round(pp.Z)));
    }

    private void UpdatePlayerChunk(Vector3 pp)
    {
        foreach (Chunk c in _chunks)
        {
            if (pp.Z <= c.EntryPoint.Z && pp.Z >= c.ExitPoint.Z)
            {
                _player!.CurrentChunkIndex = c.Index;
                // V2-03/V2-02: Im Flug liegt man meist über dem ≈ 3 m hohen Trigger. Den Checkpoint des
                // überflogenen Chunks deshalb nachziehen, sonst respawnt man weit hinten und alle Chunks ab dem
                // alten Checkpoint bleiben geschützt (unbegrenztes Chunk-Wachstum).
                if (_player.IsFlying && c.Index > _player.ActiveCheckpointChunk)
                {
                    if (c.GetNodeOrNull<Checkpoint>("Checkpoint") is { } cp) cp.Activate(_player);
                    else _player.SetCheckpoint(c.CheckpointPosition, c.Index);
                }
                return;
            }
        }
    }

    private void SpawnNext()
    {
        if (_builder == null || _chunksRoot == null) return;
        int index = HighestChunkIndex + 1;
        bool prevItem = _lastPattern.HasValue && _lastPattern.Value.IsItemRequired();
        Chunk chunk = _builder.Build(index, _nextEntry, _lastPattern, _lastStreak, prevItem,
            ChunkBuilder.DifficultyForIndex(index));
        _chunksRoot.AddChild(chunk);
        _chunks.Add(chunk);
        _lastStreak = _lastPattern.HasValue && _lastPattern.Value == chunk.Pattern ? _lastStreak + 1 : 1;
        _lastPattern = chunk.Pattern;
        _nextEntry = chunk.ExitPoint;
        HighestChunkIndex = index;
        TotalSpawned++;
        EmitSignal(SignalName.ChunkSpawned, index);
    }

    private void Despawn(Vector3 pp)
    {
        int protectFrom = ProtectedFromIndex();
        Camera3D? cam = GetViewport()?.GetCamera3D();
        Godot.Collections.Array<Plane>? frustum = cam != null ? cam.GetFrustum() : null; // nur alle DespawnIntervalFrames (R-08)

        bool hardLimit = _chunks.Count > GameConstants.MaxAliveChunks;
        for (int i = 0; i < _chunks.Count; i++)
        {
            Chunk c = _chunks[i];
            if (!IsInstanceValid(c)) { _chunks.RemoveAt(i); i--; continue; }
            // Bedingung 1 + 4: alles ab dem aktiven Checkpoint bis zum Spieler (und davor) bleibt erhalten (F-01).
            if (c.Index >= protectFrom) continue;

            // R-13: Das harte Limit (MaxAliveChunks) entfernt den ältesten ungeschützten Chunk notfalls auch,
            // wenn er noch sichtbar ist – bewusste Speicher-Schutzregel; im Normalbetrieb (≈ 6 vorne + 1–2 hinten)
            // wird sie nie erreicht. F-11: Despawn richtet sich nach Kamera-Frustum und Abstand, nicht nach der
            // Laufrichtung; wer > 30 m rückwärts läuft, stürzt und respawnt am (immer geschützten) Checkpoint.
            bool remove;
            if (hardLimit && i == OldestRemovableIndex(protectFrom))
            {
                remove = true;                                                 // hartes Limit
            }
            else
            {
                bool farBehind = c.ExitPoint.Z - pp.Z > GameConstants.DespawnDistance;           // Bedingung 2
                bool outOfView = frustum == null || !FrustumUtil.AabbIntersectsFrustum(c.WorldBounds, frustum); // 3
                remove = farBehind && outOfView;
            }
            if (!remove) continue;

            int idx = c.Index;
            _chunks.RemoveAt(i);
            c.QueueFree();
            TotalDespawned++;
            EmitSignal(SignalName.ChunkDespawned, idx);
            hardLimit = _chunks.Count > GameConstants.MaxAliveChunks;
            i--;
        }
    }

    /// <summary>Kleinster geschützter Chunk-Index: min(Checkpoint-Chunk, Spieler-Chunk).</summary>
    private int ProtectedFromIndex()
    {
        int current = _player!.CurrentChunkIndex;
        int checkpoint = _player.ActiveCheckpointChunk;
        return checkpoint >= 0 ? Mathf.Min(current, checkpoint) : current;
    }

    private int OldestRemovableIndex(int protectFrom)
    {
        int best = -1, bestIdx = int.MaxValue;
        for (int i = 0; i < _chunks.Count; i++)
        {
            Chunk c = _chunks[i];
            if (c.Index >= protectFrom) continue;
            if (c.Index < bestIdx) { bestIdx = c.Index; best = i; }
        }
        return best;
    }

    private void ShiftOrigin(Vector3 offset)
    {
        if (offset == Vector3.Zero) return;
        foreach (Chunk c in _chunks)
        {
            if (!IsInstanceValid(c)) continue;
            c.Position += offset;
            c.ApplyOriginShift(offset);
        }
        if (_placeablesRoot != null)
            foreach (Node n in _placeablesRoot.GetChildren())
                if (n is Node3D n3) n3.Position += offset;
        _nextEntry += offset;
        _player!.ApplyOriginShift(offset);
        GameManager.Instance?.AddOriginOffset(offset.Z);
        EmitSignal(SignalName.OriginShifted, offset);
    }

    private void OnPlayerRespawned()
    {
        if (_player == null) return;
        Chunk? c = GetChunk(_player.ActiveCheckpointChunk);
        if (c == null || !c.IsItemChunk) return;
        foreach (ItemType t in c.Pattern.RequiredItems())
            if (_player.Inventory.GetCount(t) > 0) return;
        c.RestockSupply(); // nachgelieferte Kisten geben keinen Bonus (F-04)
    }
}
