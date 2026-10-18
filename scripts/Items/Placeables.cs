using Godot;
using JumpRun.Game;

namespace JumpRun.Items;

/// <summary>Verwaltet platzierte Objekte unter Main/WorldRoot/Placeables (max. 3 gleichzeitig).</summary>
public static class Placeables
{
    public const string RootGroup = "placeables_root";

    public static Node3D? FindRoot(Node from)
    {
        SceneTree? tree = from.GetTree();
        return tree?.GetFirstNodeInGroup(RootGroup) as Node3D;
    }

    /// <summary>Fügt ein Objekt hinzu und entfernt bei Überschreitung das älteste.</summary>
    public static bool Add(Node from, Node3D placeable, Transform3D globalTransform)
    {
        Node3D? root = FindRoot(from);
        if (root == null)
        {
            GD.PushWarning("[Placeables] WorldRoot/Placeables nicht gefunden.");
            placeable.QueueFree();
            return false;
        }
        int alive = 0;
        foreach (Node n in root.GetChildren())
            if (!n.IsQueuedForDeletion()) alive++;
        foreach (Node n in root.GetChildren())
        {
            if (alive < GameConstants.MaxPlaceables) break;
            if (n.IsQueuedForDeletion()) continue;
            n.QueueFree();
            alive--;
        }
        root.AddChild(placeable);
        placeable.GlobalTransform = globalTransform;
        return true;
    }

    /// <summary>Gemeinsame Lebensdauer-Logik: in den letzten 2 s blinken, dann QueueFree.</summary>
    public static void TickLifetime(Node3D node, ref float age, float lifetime, double delta)
    {
        age += (float)delta;
        if (age >= lifetime) { node.QueueFree(); return; }
        float remaining = lifetime - age;
        node.Visible = remaining > 2f || Mathf.PosMod(remaining, 0.25f) > 0.1f;
    }
}
