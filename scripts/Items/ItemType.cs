namespace JumpRun.Items;

/// <summary>Reihenfolge verbindlich (CONTRACT_V2 §3.1). Slot-Index = (int)type - 1.</summary>
public enum ItemType
{
    None = 0,
    Bridge = 1,
    Trampoline = 2,
    GrappleHook = 3,
    Helicopter = 4,
}
