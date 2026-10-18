using Godot;
using JumpRun.Game;
using JumpRun.Player;
using JumpRun.World;

namespace JumpRun.Items;

public static class TrampolineItem
{
    private const string ScenePath = "res://scenes/objects/Trampoline.tscn";
    private static SphereShape3D? _clearanceShape;
    private static PhysicsShapeQueryParameters3D? _clearanceQuery;

    /// <summary>Statische Ressourcen beim Beenden freigeben (F-10).</summary>
    internal static void ReleaseStatics()
    {
        _clearanceQuery?.Dispose();
        _clearanceShape?.Dispose();
        _clearanceQuery = null;
        _clearanceShape = null;
    }

    public static bool TryUse(PlayerController p, out string failReason)
    {
        failReason = "";
        if (!p.IsOnFloor() || p.IsGrappling)
        {
            failReason = "Nur am Boden möglich";
            return false;
        }
        Vector3 fwd = p.CameraRig?.FlatForward ?? p.Facing;
        Vector3 target = p.GlobalPosition + fwd * 1.5f;
        PhysicsDirectSpaceState3D space = p.GetWorld3D().DirectSpaceState;

        var hit = space.IntersectRay(p.RayQuery(target + Vector3.Up * 1.0f, target + Vector3.Down * 1.5f,
            GameConstants.LayerWorld | GameConstants.LayerPlaceable));
        if (hit.Count == 0 || hit["normal"].AsVector3().Dot(Vector3.Up) < 0.7f)
        {
            failReason = "Kein Platz für das Trampolin";
            return false;
        }
        // Auf beweglichen/drehenden Plattformen würde das Trampolin in der Luft stehen bleiben (F-07, V2-04).
        if (hit["collider"].AsGodotObject() is MovingPlatform or RotatingPlatform)
        {
            failReason = "Nicht auf beweglichen Plattformen";
            return false;
        }
        Vector3 pos = hit["position"].AsVector3();

        // Freiraum prüfen (keine Wand / Kiste im Weg)
        _clearanceShape ??= new SphereShape3D { Radius = 0.7f };
        _clearanceQuery ??= new PhysicsShapeQueryParameters3D
        {
            Shape = _clearanceShape,
            CollisionMask = GameConstants.LayerWorld | GameConstants.LayerCrate | GameConstants.LayerPlaceable,
            CollideWithAreas = false,
        };
        _clearanceQuery.Transform = new Transform3D(Basis.Identity, pos + Vector3.Up * 0.85f);
        if (space.IntersectShape(_clearanceQuery, 1).Count > 0)
        {
            failReason = "Kein Platz für das Trampolin";
            return false;
        }

        PackedScene? scene = ChunkBuilder.LoadScene(ScenePath);
        if (scene?.Instantiate() is not Trampoline t)
        {
            failReason = "Kein Platz für das Trampolin";
            return false;
        }
        Basis basis = Basis.LookingAt(fwd, Vector3.Up);
        return Placeables.Add(p, t, new Transform3D(basis, pos));
    }
}
