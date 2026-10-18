using System;
using JumpRun.Items;

namespace JumpRun.World;

/// <summary>Reihenfolge verbindlich (CONTRACT_V2 §4.4) – neue Muster nur am Ende anhängen.</summary>
public enum PatternType
{
    Start,
    GapJump,
    Stairs,
    ZigZag,
    MovingPlatforms,
    NarrowBeams,
    HeightJump,
    BigGap,
    TallWall,
    Pillars,
    SpinningDiscs,
    ShapeMix,
}

public static class PatternTypeExtensions
{
    // Helikopter ist überall eine gültige Alternative (CONTRACT_V2 §3.3).
    private static readonly ItemType[] BigGapItems = { ItemType.Bridge, ItemType.GrappleHook, ItemType.Helicopter };
    private static readonly ItemType[] TallWallItems = { ItemType.Trampoline, ItemType.Helicopter };

    public static bool IsItemRequired(this PatternType p) => p is PatternType.BigGap or PatternType.TallWall;

    public static ItemType[] RequiredItems(this PatternType p) => p switch
    {
        PatternType.BigGap => BigGapItems,
        PatternType.TallWall => TallWallItems,
        _ => Array.Empty<ItemType>(),
    };
}
