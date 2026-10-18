using System.Collections.Generic;
using Godot;

namespace JumpRun.Characters;

/// <summary>
/// Materialien der Charaktere. Bevorzugt <c>res://art/characters/{key}.tres</c> (vom ResourceLoader gecacht);
/// fehlt die Datei, wird ein gleichwertiges StandardMaterial3D aus der Tabelle gebaut.
/// Keine statischen Material-Caches (Freigabe vor RenderingServer-Shutdown, vgl. v1 F-10).
/// </summary>
public static class CharacterMaterials
{
    public const string Dir = "res://art/characters/";

    public const string EyeWhite = "char_eye_white";
    public const string EyeDark = "char_eye_dark";
    public const string Limb = "char_limb";
    public const string Gold = "char_gold";
    public const string BottleGlass = "bottle_glass";
    public const string BottleLabel = "bottle_label";
    public const string TrashCan = "trash_can";
    public const string TrashLid = "trash_lid";
    public const string HeroSuit = "hero_suit";
    public const string HeroSkin = "hero_skin";
    public const string KnightSteel = "knight_steel";
    public const string KnightTabard = "knight_tabard";
    public const string KnightEyeGlow = "knight_eye_glow";

    private readonly record struct Spec(string Albedo, float Metallic, float Roughness, float Rim, string? Emission, float EmissionEnergy);

    private static readonly Dictionary<string, Spec> Specs = new()
    {
        [EyeWhite] = new("FFFFFF", 0.0f, 0.35f, 0.0f, "FFFFFF", 0.18f),
        [EyeDark] = new("161A2B", 0.0f, 0.3f, 0.0f, null, 0f),
        [Limb] = new("2B3350", 0.0f, 0.55f, 0.25f, null, 0f),
        [Gold] = new("E8A317", 0.45f, 0.3f, 0.3f, null, 0f),
        [BottleGlass] = new("8FE6DE", 0.05f, 0.1f, 0.65f, "1E5C58", 0.25f),
        [BottleLabel] = new("FF7AA2", 0.0f, 0.6f, 0.2f, null, 0f),
        [TrashCan] = new("45A866", 0.45f, 0.42f, 0.35f, null, 0f),
        [TrashLid] = new("2C7A47", 0.5f, 0.4f, 0.35f, null, 0f),
        [HeroSuit] = new("3D5AFE", 0.1f, 0.45f, 0.45f, null, 0f),
        [HeroSkin] = new("F2C8A0", 0.0f, 0.7f, 0.3f, null, 0f),
        [KnightSteel] = new("C9D1DC", 0.85f, 0.28f, 0.25f, null, 0f),
        [KnightTabard] = new("2F6FA8", 0.0f, 0.65f, 0.3f, null, 0f),
        [KnightEyeGlow] = new("A8F0FF", 0.0f, 0.5f, 0.0f, "7FE6FF", 2.2f),
    };

    public static IEnumerable<string> Keys => Specs.Keys;

    /// <summary>Lädt das Material; <paramref name="buildCache"/> sorgt für eine Instanz pro Schlüssel und Modellbau.</summary>
    public static Material Get(string key, Dictionary<string, Material> buildCache)
    {
        if (buildCache.TryGetValue(key, out Material? cached)) return cached;
        Material? mat = null;
        string path = Dir + key + ".tres";
        if (ResourceLoader.Exists(path))
            mat = ResourceLoader.Load<Material>(path);
        mat ??= Create(key);
        buildCache[key] = mat;
        return mat;
    }

    /// <summary>Baut das Material aus der Tabelle (Fallback ohne .tres).</summary>
    public static StandardMaterial3D Create(string key)
    {
        Spec s = Specs.TryGetValue(key, out Spec found) ? found : new Spec("FFFFFF", 0f, 0.5f, 0f, null, 0f);
        var m = new StandardMaterial3D
        {
            ResourceName = key,
            AlbedoColor = new Color(s.Albedo),
            Metallic = s.Metallic,
            Roughness = s.Roughness,
        };
        if (s.Metallic > 0f) m.MetallicSpecular = 0.6f;
        if (s.Rim > 0f)
        {
            m.RimEnabled = true;
            m.Rim = s.Rim;
            m.RimTint = 0.4f;
        }
        if (s.Emission != null)
        {
            m.EmissionEnabled = true;
            m.Emission = new Color(s.Emission);
            m.EmissionEnergyMultiplier = s.EmissionEnergy;
        }
        return m;
    }
}
