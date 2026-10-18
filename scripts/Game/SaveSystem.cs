using System;
using System.IO;
using System.Text;
using System.Text.Json;
using Godot;
using JumpRun.Characters;

namespace JumpRun.Game;

public sealed class HighscoreData
{
    public int Version { get; set; } = 1;
    public int Highscore { get; set; }
    public int BestDistance { get; set; }
}

public sealed class SettingsData
{
    public int Version { get; set; } = 1;
    public float MouseSensitivity { get; set; } = GameConstants.MouseSensitivityDefault;
    public bool InvertY { get; set; }
    /// <summary>Charakter-Id (JSON "character"), CONTRACT_V2 §5.6.</summary>
    public string Character { get; set; } = CharacterCatalog.DefaultId;
    /// <summary>Linear 0..1 (JSON "music_volume").</summary>
    public float MusicVolume { get; set; } = SaveSystem.MusicVolumeDefault;
    /// <summary>Linear 0..1 (JSON "sfx_volume").</summary>
    public float SfxVolume { get; set; } = SaveSystem.SfxVolumeDefault;
    /// <summary>R V2-04: Grafik „niedrig“ (JSON "graphics_low"); null = automatisch nach Renderer.</summary>
    public bool? GraphicsLow { get; set; }
}

/// <summary>
/// Robustes Laden/Speichern der Spielstände. Nur feste Pfade unter user://,
/// Größenlimit, Parsen in try/catch, Werte-Clamping, atomares Schreiben (.tmp → rename).
/// </summary>
public static class SaveSystem
{
    private const int CurrentVersion = 1;
    public const float MusicVolumeDefault = 0.7f;
    public const float SfxVolumeDefault = 0.8f;
    public const int MaxCharacterIdLength = 16;
    private static readonly JsonDocumentOptions DocOptions = new()
    {
        MaxDepth = 8,
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
    };

    public static HighscoreData LoadHighscore()
    {
        string? json = ReadLimited(GameConstants.HighscoreFile);
        return json == null ? new HighscoreData() : ParseHighscore(json);
    }

    public static bool SaveHighscore(HighscoreData data)
    {
        var buffer = new ArrayBufferWriterSimple();
        using (var w = new Utf8JsonWriter(buffer.Stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteNumber("version", CurrentVersion);
            w.WriteNumber("highscore", ClampScore(data.Highscore));
            w.WriteNumber("best_distance", ClampScore(data.BestDistance));
            w.WriteEndObject();
        }
        return WriteAtomic(GameConstants.HighscoreFile, buffer.ToArray());
    }

    public static SettingsData LoadSettings()
    {
        string? json = ReadLimited(GameConstants.SettingsFile);
        return json == null ? new SettingsData() : ParseSettings(json);
    }

    public static bool SaveSettings(SettingsData data) => WriteAtomic(GameConstants.SettingsFile, SerializeSettings(data));

    /// <summary>Settings → JSON-Bytes (auch für Roundtrip-Tests ohne Dateizugriff).</summary>
    internal static byte[] SerializeSettings(SettingsData data)
    {
        var buffer = new ArrayBufferWriterSimple();
        using (var w = new Utf8JsonWriter(buffer.Stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteNumber("version", CurrentVersion);
            w.WriteNumber("mouse_sensitivity", Math.Round(ClampSensitivity(data.MouseSensitivity), 3));
            w.WriteBoolean("invert_y", data.InvertY);
            w.WriteString("character", IsAcceptableCharacterId(data.Character) ? data.Character : CharacterCatalog.DefaultId);
            w.WriteNumber("music_volume", Math.Round(ClampVolume(data.MusicVolume, MusicVolumeDefault), 3));
            w.WriteNumber("sfx_volume", Math.Round(ClampVolume(data.SfxVolume, SfxVolumeDefault), 3));
            if (data.GraphicsLow.HasValue) w.WriteBoolean("graphics_low", data.GraphicsLow.Value);
            w.WriteEndObject();
        }
        return buffer.ToArray();
    }

    /// <summary>Parst Highscore-JSON. Wirft nie; ungültige Daten → Defaults.</summary>
    internal static HighscoreData ParseHighscore(string json)
    {
        var result = new HighscoreData();
        try
        {
            if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > GameConstants.MaxSaveFileBytes)
            {
                Warn("Highscore-Datei leer oder zu groß – Defaults.");
                return result;
            }
            using JsonDocument doc = JsonDocument.Parse(json, DocOptions);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                Warn("Highscore-Datei: Wurzel ist kein Objekt – Defaults.");
                return result;
            }
            if (!TryGetVersion(root, out int version) || version != CurrentVersion)
            {
                Warn("Highscore-Datei: falsche Version – Defaults.");
                return result;
            }
            result.Highscore = TryGetInt(root, "highscore", out int hs) ? ClampScore(hs) : 0;
            result.BestDistance = TryGetInt(root, "best_distance", out int bd) ? ClampScore(bd) : 0;
            return result;
        }
        catch (Exception e)
        {
            Warn($"Highscore-Datei ungültig ({e.GetType().Name}) – Defaults.");
            return new HighscoreData();
        }
    }

    /// <summary>Parst Settings-JSON. Wirft nie; ungültige Daten → Defaults.</summary>
    internal static SettingsData ParseSettings(string json)
    {
        var result = new SettingsData();
        try
        {
            if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > GameConstants.MaxSaveFileBytes)
            {
                Warn("Settings-Datei leer oder zu groß – Defaults.");
                return result;
            }
            using JsonDocument doc = JsonDocument.Parse(json, DocOptions);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return result;
            if (!TryGetVersion(root, out int version) || version != CurrentVersion)
            {
                Warn("Settings-Datei: falsche Version – Defaults.");
                return result;
            }
            if (root.TryGetProperty("mouse_sensitivity", out JsonElement ms) && ms.ValueKind == JsonValueKind.Number
                && ms.TryGetDouble(out double sens) && double.IsFinite(sens))
            {
                result.MouseSensitivity = ClampSensitivity((float)sens);
            }
            if (root.TryGetProperty("invert_y", out JsonElement inv)
                && (inv.ValueKind == JsonValueKind.True || inv.ValueKind == JsonValueKind.False))
            {
                result.InvertY = inv.GetBoolean();
            }
            if (root.TryGetProperty("character", out JsonElement ch) && ch.ValueKind == JsonValueKind.String)
            {
                string? id = ch.GetString();
                if (IsAcceptableCharacterId(id)) result.Character = id!;
            }
            if (TryGetFiniteNumber(root, "music_volume", out double mv))
                result.MusicVolume = ClampVolume((float)mv, MusicVolumeDefault);
            if (TryGetFiniteNumber(root, "sfx_volume", out double sv))
                result.SfxVolume = ClampVolume((float)sv, SfxVolumeDefault);
            if (root.TryGetProperty("graphics_low", out JsonElement gl)
                && (gl.ValueKind == JsonValueKind.True || gl.ValueKind == JsonValueKind.False))
            {
                result.GraphicsLow = gl.GetBoolean();
            }
            return result;
        }
        catch (Exception e)
        {
            Warn($"Settings-Datei ungültig ({e.GetType().Name}) – Defaults.");
            return new SettingsData();
        }
    }

    internal static int ClampScore(long v) => (int)Math.Clamp(v, 0L, GameConstants.MaxScoreValue);

    internal static float ClampSensitivity(float v)
    {
        if (!float.IsFinite(v)) return GameConstants.MouseSensitivityDefault;
        return Math.Clamp(v, GameConstants.MouseSensitivityMin, GameConstants.MouseSensitivityMax);
    }

    /// <summary>Lautstärke linear 0..1; nicht-endlich → <paramref name="fallback"/>.</summary>
    internal static float ClampVolume(float v, float fallback)
    {
        if (!float.IsFinite(v)) return Math.Clamp(float.IsFinite(fallback) ? fallback : 1f, 0f, 1f);
        return Math.Clamp(v, 0f, 1f);
    }

    /// <summary>Charakter-Id nur als kurzer ASCII-[a-z]-String, der im Katalog existiert.</summary>
    internal static bool IsAcceptableCharacterId(string? id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > MaxCharacterIdLength) return false;
        foreach (char c in id)
            if (c < 'a' || c > 'z') return false;
        return CharacterCatalog.IsValid(id);
    }

    private static bool TryGetFiniteNumber(JsonElement obj, string name, out double value)
    {
        value = 0;
        return obj.TryGetProperty(name, out JsonElement e) && e.ValueKind == JsonValueKind.Number
            && e.TryGetDouble(out value) && double.IsFinite(value);
    }

    /// <summary>Version nur als echte Ganzzahl akzeptieren – 1.9 ist keine Version 1 (F-06).</summary>
    private static bool TryGetVersion(JsonElement obj, out int version)
    {
        version = 0;
        return obj.TryGetProperty("version", out JsonElement e) && e.ValueKind == JsonValueKind.Number
            && e.TryGetInt32(out version);
    }

    private static bool TryGetInt(JsonElement obj, string name, out int value)
    {
        value = 0;
        if (!obj.TryGetProperty(name, out JsonElement e) || e.ValueKind != JsonValueKind.Number)
            return false;
        if (e.TryGetInt64(out long l))
        {
            value = (int)Math.Clamp(l, int.MinValue, int.MaxValue);
            return true;
        }
        if (e.TryGetDouble(out double d) && double.IsFinite(d))
        {
            value = (int)Math.Clamp(Math.Floor(d), int.MinValue, int.MaxValue);
            return true;
        }
        return false;
    }

    private static bool IsAllowedPath(string path) =>
        path == GameConstants.HighscoreFile || path == GameConstants.SettingsFile;

    private static string? ReadLimited(string path)
    {
        if (!IsAllowedPath(path)) return null;
        try
        {
            if (!Godot.FileAccess.FileExists(path)) return null;
            using var f = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
            if (f == null) return null;
            ulong len = f.GetLength();
            if (len == 0 || len > (ulong)GameConstants.MaxSaveFileBytes)
            {
                Warn($"{path}: Datei leer oder größer als {GameConstants.MaxSaveFileBytes} Bytes – ignoriert.");
                return null;
            }
            byte[] bytes = f.GetBuffer((long)len);
            // UTF-8-BOM (z. B. von Windows-Editoren) überspringen statt die Datei zu verwerfen (F-05).
            int start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            return new UTF8Encoding(false, true).GetString(bytes, start, bytes.Length - start);
        }
        catch (Exception e)
        {
            Warn($"{path}: Lesefehler ({e.GetType().Name}).");
            return null;
        }
    }

    private static bool WriteAtomic(string path, byte[] bytes)
    {
        if (!IsAllowedPath(path)) return false;
        string tmp = path + ".tmp";
        try
        {
            using (var f = Godot.FileAccess.Open(tmp, Godot.FileAccess.ModeFlags.Write))
            {
                if (f == null)
                {
                    Warn($"{tmp}: kann nicht geschrieben werden ({Godot.FileAccess.GetOpenError()}).");
                    return false;
                }
                f.StoreBuffer(bytes);
                f.Flush();
            }
            string absTmp = ProjectSettings.GlobalizePath(tmp);
            string absPath = ProjectSettings.GlobalizePath(path);
            Error err = DirAccess.RenameAbsolute(absTmp, absPath);
            if (err != Error.Ok)
            {
                Warn($"{path}: Umbenennen fehlgeschlagen ({err}).");
                return false;
            }
            return true;
        }
        catch (Exception e)
        {
            Warn($"{path}: Schreibfehler ({e.GetType().Name}).");
            return false;
        }
    }

    /// <summary>Für Logiktests: erwartete Warnungen nicht ins Log schreiben.</summary>
    internal static bool QuietWarnings { get; set; }

    private static void Warn(string msg)
    {
        if (QuietWarnings) return;
        try { GD.PushWarning("[SaveSystem] " + msg); }
        catch { /* außerhalb der Engine (Tests) ignorieren */ }
    }

    /// <summary>Kleiner Puffer für Utf8JsonWriter (vermeidet Reflection-Serialisierung).</summary>
    private sealed class ArrayBufferWriterSimple
    {
        public MemoryStream Stream { get; } = new();
        public byte[] ToArray() => Stream.ToArray();
    }
}
