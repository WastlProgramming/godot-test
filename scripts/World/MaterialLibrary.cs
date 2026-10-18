using System.Collections.Generic;
using Godot;

namespace JumpRun.World;

/// <summary>Feste Materialnamen (CONTRACT 7.1). Nur diese Konstanten werden an MaterialLibrary übergeben.</summary>
public static class MaterialNames
{
    public const string PlatformStart = "platform_start";
    public const string PlatformGap = "platform_gap";
    public const string PlatformStairs = "platform_stairs";
    public const string PlatformZigZag = "platform_zigzag";
    public const string PlatformMoving = "platform_moving";
    public const string PlatformBeam = "platform_beam";
    public const string PlatformHeight = "platform_height";
    public const string PlatformBigGap = "platform_biggap";
    public const string PlatformWall = "platform_wall";
    public const string PlatformSupply = "platform_supply";
    public const string PlatformCheckpoint = "platform_checkpoint";
    public const string Crate = "crate";
    public const string Bridge = "bridge";
    public const string TrampolineBase = "trampoline_base";
    public const string TrampolinePad = "trampoline_pad";
    public const string GrappleAnchor = "grapple_anchor";
    public const string GrappleAnchorActive = "grapple_anchor_active";
    public const string GrappleRope = "grapple_rope";
    public const string PickupBridge = "pickup_bridge";
    public const string PickupTrampoline = "pickup_trampoline";
    public const string PickupGrapple = "pickup_grapple";
    public const string CheckpointFlag = "checkpoint_flag";
    public const string CheckpointFlagActive = "checkpoint_flag_active";
    public const string PlayerBody = "player_body";
    public const string PlayerAccent = "player_accent";

    // v2 (CONTRACT_V2 §7.2)
    public const string PlatformPillars = "platform_pillars";
    public const string PlatformSpin = "platform_spin";
    public const string PlatformShapes = "platform_shapes";
    public const string PlatformGapShape = "platform_gap_shape";
    public const string PlatformStairsShape = "platform_stairs_shape";
    public const string PlatformZigZagShape = "platform_zigzag_shape";
    public const string PlatformMovingShape = "platform_moving_shape";
    public const string PlatformHeightShape = "platform_height_shape";
    public const string PlatformPillarsShape = "platform_pillars_shape";
    public const string PlatformSpinShape = "platform_spin_shape";
    public const string PlatformShapesShape = "platform_shapes_shape";
    public const string PickupHelicopter = "pickup_helicopter";
    public const string HelicopterRotor = "helicopter_rotor";
    public const string HelicopterHub = "helicopter_hub";

    public static string ForPattern(PatternType p) => p switch
    {
        PatternType.Start => PlatformStart,
        PatternType.GapJump => PlatformGap,
        PatternType.Stairs => PlatformStairs,
        PatternType.ZigZag => PlatformZigZag,
        PatternType.MovingPlatforms => PlatformMoving,
        PatternType.NarrowBeams => PlatformBeam,
        PatternType.HeightJump => PlatformHeight,
        PatternType.BigGap => PlatformBigGap,
        PatternType.TallWall => PlatformBigGap,
        PatternType.Pillars => PlatformPillars,
        PatternType.SpinningDiscs => PlatformSpin,
        PatternType.ShapeMix => PlatformShapes,
        _ => PlatformGap,
    };
}

/// <summary>Lädt Designer-Materialien mit Cache und Magenta-Fallback.</summary>
public static class MaterialLibrary
{
    private static readonly Dictionary<string, Material> Cache = new();
    private static readonly HashSet<string> Warned = new();
    private static StandardMaterial3D? _fallback;

    public static Material Get(string name)
    {
        if (Cache.TryGetValue(name, out Material? m) && GodotObject.IsInstanceValid(m))
            return m;

        string path = $"res://art/materials/{name}.tres";
        Material? loaded = null;
        if (ResourceLoader.Exists(path))
            loaded = ResourceLoader.Load<Material>(path);

        if (loaded == null)
        {
            if (Warned.Add(name))
                GD.PushWarning($"[MaterialLibrary] Material fehlt: {path} – Magenta-Fallback.");
            loaded = Fallback;
        }
        Cache[name] = loaded;
        return loaded;
    }

    /// <summary>
    /// Material für Nicht-Box-Formen: lädt res://art/materials/{name}_shape.tres, sonst ohne Warnung Get(name)
    /// (CONTRACT_V2 §4.3). Namen, die bereits auf "_shape" enden, werden direkt geladen.
    /// </summary>
    public static Material GetShapeVariant(string name)
    {
        string key = name.EndsWith("_shape") ? name : name + "_shape";
        if (Cache.TryGetValue(key, out Material? m) && GodotObject.IsInstanceValid(m))
            return m;
        string path = $"res://art/materials/{key}.tres";
        Material? loaded = ResourceLoader.Exists(path) ? ResourceLoader.Load<Material>(path) : null;
        if (loaded == null)
        {
            string baseName = name.EndsWith("_shape") ? name[..^6] : name;
            loaded = Get(baseName);
        }
        Cache[key] = loaded;
        return loaded;
    }

    /// <summary>Cache beim Beenden leeren, damit keine RIDs nach dem RenderingServer überleben (F-10 / R-15).</summary>
    internal static void ClearCache()
    {
        Cache.Clear();
        Warned.Clear();
        _fallback?.Dispose();
        _fallback = null;
    }

    private static StandardMaterial3D Fallback => _fallback ??= new StandardMaterial3D
    {
        AlbedoColor = new Color(1f, 0f, 1f),
    };

    /// <summary>Lädt eine Designer-Ressource (Szene/Environment) nur, wenn vorhanden.</summary>
    public static T? TryLoad<T>(string path) where T : Resource
    {
        if (!ResourceLoader.Exists(path)) return null;
        return ResourceLoader.Load<T>(path);
    }
}
