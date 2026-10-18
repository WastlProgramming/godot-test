using JumpRun.Player;

namespace JumpRun.Items;

/// <summary>Helikopter (CONTRACT_V2 §3.3): kurzer Flug, am Boden und in der Luft einsetzbar.</summary>
public static class HelicopterItem
{
    /// <summary>
    /// Verbraucht bei Erfolg eine Ladung und startet den Flug. Ein laufender Enterhaken wird beendet.
    /// Läuft bereits ein Flug, wird nichts verbraucht (Beenden übernimmt PlayerController).
    /// </summary>
    public static bool TryUse(PlayerController p, out string failReason)
    {
        failReason = "";
        if (p.IsFlying) return false;
        if (p.Inventory.GetCount(ItemType.Helicopter) <= 0)
        {
            failReason = "Keine Ladung mehr";
            return false;
        }
        if (p.IsGrappling) p.StopGrapple(true);
        if (!p.Inventory.Consume(ItemType.Helicopter))
        {
            failReason = "Keine Ladung mehr";
            return false;
        }
        p.StartFlight();
        return true;
    }
}
