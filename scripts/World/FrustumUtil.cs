using Godot;

namespace JumpRun.World;

public static class FrustumUtil
{
    /// <summary>
    /// Konservativer Ebenen-Test (p-Vertex). Die Ebenen aus Camera3D.GetFrustum() zeigen nach außen.
    /// Liefert false nur, wenn das AABB sicher vollständig außerhalb liegt.
    /// </summary>
    public static bool AabbIntersectsFrustum(Aabb box, Godot.Collections.Array<Plane> frustum)
    {
        if (frustum == null || frustum.Count == 0) return true;
        Vector3 min = box.Position;
        Vector3 max = box.End;
        for (int i = 0; i < frustum.Count; i++)
        {
            Plane p = frustum[i];
            Vector3 n = p.Normal;
            // Eckpunkt, der am weitesten "innen" liegt (gegen die Normalenrichtung).
            var v = new Vector3(
                n.X > 0 ? min.X : max.X,
                n.Y > 0 ? min.Y : max.Y,
                n.Z > 0 ? min.Z : max.Z);
            if (p.DistanceTo(v) > 0f)
                return false;
        }
        return true;
    }
}
