using Godot;
using JumpRun.Game;
using JumpRun.Player;
using JumpRun.World;

namespace JumpRun.Items;

/// <summary>Enterhaken: zielt auf den nächsten Anker im Blickkegel der Kamera mit freier Sichtlinie.</summary>
public static class GrappleHook
{
    public static bool TryUse(PlayerController p, out string failReason)
    {
        failReason = "";
        if (p.IsGrappling) return false; // Lösen übernimmt PlayerController.UseSelectedItem
        GrappleAnchor? target = FindTarget(p);
        if (target == null)
        {
            failReason = "Kein Anker in Reichweite";
            return false;
        }
        p.StartGrapple(target);
        return true;
    }

    public static GrappleAnchor? FindTarget(PlayerController p)
    {
        Camera3D? cam = p.Camera;
        if (cam == null || !GodotObject.IsInstanceValid(cam) || !p.IsInsideTree()) return null;
        Vector3 camPos = cam.GlobalPosition;
        Vector3 camFwd = -cam.GlobalBasis.Z.Normalized();
        Vector3 origin = p.GlobalPosition + Vector3.Up * 1.0f;
        float cosCone = Mathf.Cos(Mathf.DegToRad(GameConstants.GrappleConeDeg));
        float rangeSq = GameConstants.GrappleRange * GameConstants.GrappleRange;
        PhysicsDirectSpaceState3D space = p.GetWorld3D().DirectSpaceState;

        GrappleAnchor? best = null;
        float bestDot = -1f;
        foreach (GrappleAnchor a in GrappleAnchor.Active)
        {
            if (!GodotObject.IsInstanceValid(a) || a.IsQueuedForDeletion() || !a.IsInsideTree()) continue;
            Vector3 ap = a.GlobalPosition;
            if (origin.DistanceSquaredTo(ap) > rangeSq) continue;
            Vector3 dir = (ap - camPos).Normalized();
            float dot = dir.Dot(camFwd);
            if (dot < cosCone || dot <= bestDot) continue;

            var hit = space.IntersectRay(p.RayQuery(origin, ap,
                GameConstants.LayerWorld | GameConstants.LayerGrappleAnchor | GameConstants.LayerPlaceable));
            if (hit.Count > 0 && hit["collider"].AsGodotObject() != a) continue;
            best = a;
            bestDot = dot;
        }
        return best;
    }
}
