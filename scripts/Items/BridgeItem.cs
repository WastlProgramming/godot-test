using Godot;
using JumpRun.Game;
using JumpRun.Player;
using JumpRun.World;

namespace JumpRun.Items;

public static class BridgeItem
{
    private const string ScenePath = "res://scenes/objects/Bridge.tscn";

    public static bool TryUse(PlayerController p, out string failReason)
    {
        failReason = "";
        if (!p.IsOnFloor() || p.IsGrappling)
        {
            failReason = "Nur am Boden möglich";
            return false;
        }
        PackedScene? scene = ChunkBuilder.LoadScene(ScenePath);
        if (scene?.Instantiate() is not Bridge bridge)
        {
            failReason = "Brücke nicht verfügbar";
            return false;
        }
        Vector3 fwd = p.CameraRig?.FlatForward ?? p.Facing;
        // Leichtes Schräg-Zielen auf die Laufrichtung (-Z) einrasten, damit die volle Länge zählt (R-03).
        if (fwd.Dot(Vector3.Forward) > Mathf.Cos(Mathf.DegToRad(GameConstants.BridgeSnapDeg))) fwd = Vector3.Forward;
        Vector3 feet = p.GlobalPosition;
        Vector3 center = feet + fwd * (GameConstants.BridgeLength * 0.5f) + Vector3.Down * 0.15f;
        Basis basis = Basis.LookingAt(fwd, Vector3.Up);
        return Placeables.Add(p, bridge, new Transform3D(basis, center)) || Fail(out failReason);
    }

    private static bool Fail(out string reason)
    {
        reason = "Brücke nicht verfügbar";
        return false;
    }
}
