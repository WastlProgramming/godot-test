using Godot;
using JumpRun.Game;

namespace JumpRun.Items;

/// <summary>4 Slots (Brücke, Trampolin, Enterhaken, Helikopter), unbegrenzt viele Ladungen (CONTRACT_V2 §3.2).</summary>
public partial class Inventory : Node
{
    [Signal] public delegate void InventoryChangedEventHandler(int bridge, int trampoline, int grapple, int helicopter);
    [Signal] public delegate void SelectionChangedEventHandler(int itemType);
    [Signal] public delegate void ItemAddedEventHandler(int itemType, int newCount);

    private readonly int[] _counts = new int[GameConstants.ItemSlotCount];

    public ItemType Selected { get; private set; } = ItemType.Bridge;

    public static bool Valid(ItemType t) => (int)t >= 1 && (int)t <= GameConstants.ItemSlotCount;

    public int GetCount(ItemType t) => Valid(t) ? _counts[(int)t - 1] : 0;

    /// <summary>Immer true für gültige Items und amount &gt; 0 (sättigt bei InventoryHardCap).</summary>
    public bool Add(ItemType t, int amount = 1)
    {
        if (!Valid(t) || amount <= 0) return false;
        int i = (int)t - 1;
        long n = (long)_counts[i] + amount;
        _counts[i] = (int)System.Math.Min(n, GameConstants.InventoryHardCap);
        Emit();
        EmitSignal(SignalName.ItemAdded, (int)t, _counts[i]);
        return true;
    }

    public bool Consume(ItemType t)
    {
        if (!Valid(t)) return false;
        int i = (int)t - 1;
        if (_counts[i] <= 0) return false;
        _counts[i]--;
        Emit();
        return true;
    }

    public void Select(ItemType t)
    {
        if (!Valid(t) || t == Selected) return;
        Selected = t;
        EmitSignal(SignalName.SelectionChanged, (int)t);
    }

    public void SelectNext(int dir)
    {
        int n = GameConstants.ItemSlotCount;
        int s = (int)Selected - 1;
        int step = dir >= 0 ? 1 : -1;
        s = ((s + step) % n + n) % n;
        Select((ItemType)(s + 1));
    }

    public void Clear()
    {
        for (int i = 0; i < _counts.Length; i++) _counts[i] = 0;
        Emit();
    }

    private void Emit() => EmitSignal(SignalName.InventoryChanged, _counts[0], _counts[1], _counts[2], _counts[3]);
}
