using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using JumpRun.Game;

namespace JumpRun.Tests;

/// <summary>
/// Tester: Savefile-Robustheit mit echten Dateien unter user:// (vorhandene Dateien werden gesichert und
/// am Ende wiederhergestellt). Prüft übergroße, binäre, BOM-, verschachtelte, negative und riesige Werte,
/// Verzeichnis statt Datei sowie das atomare Schreiben.
/// Start: godot --headless --path . res://tests/TesterSecurityTest.tscn → SECURITY_TEST_DONE pass=N fail=M
/// </summary>
public partial class TesterSecurityTest : Node
{
    private int _pass, _fail;
    private readonly Dictionary<string, byte[]?> _backup = new();
    private static readonly string[] Paths =
    {
        GameConstants.HighscoreFile, GameConstants.SettingsFile,
        GameConstants.HighscoreFile + ".tmp", GameConstants.SettingsFile + ".tmp",
    };

    public override void _Ready()
    {
        GameManager.Current.PersistenceEnabled = false;
        int exceptions = 0;
        string? first = null;
        Backup();
        try
        {
            SaveSystem.QuietWarnings = true;
            RunCases();
        }
        catch (Exception e)
        {
            exceptions++;
            first = e.ToString();
        }
        finally
        {
            SaveSystem.QuietWarnings = false;
            Restore();
        }
        Report("NoUnhandledException", exceptions == 0, first ?? "keine");
        GD.Print($"SECURITY_TEST_DONE pass={_pass} fail={_fail}");
        GetTree().Quit(_fail == 0 ? 0 : 1);
    }

    private void Report(string name, bool ok, string details)
    {
        if (ok) _pass++; else _fail++;
        GD.Print($"[Security] {(ok ? "PASS" : "FAIL")} {name}: {details}");
    }

    private void Backup()
    {
        foreach (string p in Paths)
            _backup[p] = Godot.FileAccess.FileExists(p) ? Godot.FileAccess.GetFileAsBytes(p) : null;
    }

    private void Restore()
    {
        foreach (string p in Paths)
        {
            RemoveAny(p);
            byte[]? b = _backup[p];
            if (b == null) continue;
            using var f = Godot.FileAccess.Open(p, Godot.FileAccess.ModeFlags.Write);
            f?.StoreBuffer(b);
        }
    }

    private static void RemoveAny(string p)
    {
        string abs = ProjectSettings.GlobalizePath(p);
        if (DirAccess.DirExistsAbsolute(abs)) DirAccess.RemoveAbsolute(abs);
        else if (Godot.FileAccess.FileExists(p)) DirAccess.RemoveAbsolute(abs);
    }

    private static void WriteBytes(string p, byte[] bytes)
    {
        RemoveAny(p);
        using var f = Godot.FileAccess.Open(p, Godot.FileAccess.ModeFlags.Write);
        f!.StoreBuffer(bytes);
    }

    private static void WriteText(string p, string s) => WriteBytes(p, Encoding.UTF8.GetBytes(s));

    private void RunCases()
    {
        string hs = GameConstants.HighscoreFile;
        string st = GameConstants.SettingsFile;

        // 1. übergroße Datei (5 KB gültiges JSON mit Padding)
        WriteText(hs, "{\"version\":1,\"highscore\":500,\"pad\":\"" + new string('a', 5000) + "\"}");
        HighscoreData d = SaveSystem.LoadHighscore();
        Report("Oversize5KB", d.Highscore == 0, $"highscore={d.Highscore}");

        // 2. 1 MB Datei
        WriteBytes(hs, new byte[1024 * 1024]);
        d = SaveSystem.LoadHighscore();
        Report("Oversize1MB", d.Highscore == 0, $"highscore={d.Highscore}");

        // 3. ungültiges UTF-8
        WriteBytes(hs, new byte[] { 0x7B, 0xFF, 0xFE, 0xC3, 0x28, 0x7D });
        d = SaveSystem.LoadHighscore();
        Report("InvalidUtf8", d.Highscore == 0, $"highscore={d.Highscore}");

        // 4. UTF-8-BOM (z. B. mit Windows-Editor gespeichert) – gültige Daten sollten erhalten bleiben
        var bom = new List<byte> { 0xEF, 0xBB, 0xBF };
        bom.AddRange(Encoding.UTF8.GetBytes("{\"version\":1,\"highscore\":321,\"best_distance\":300}"));
        WriteBytes(hs, bom.ToArray());
        d = SaveSystem.LoadHighscore();
        Report("Utf8BomAccepted", d.Highscore == 321, $"highscore={d.Highscore} (BOM-Datei wird {(d.Highscore == 321 ? "akzeptiert" : "verworfen")})");

        // 5. negative / riesige / Exponent-Werte
        WriteText(hs, "{\"version\":1,\"highscore\":-2147483649,\"best_distance\":1e999}");
        d = SaveSystem.LoadHighscore();
        Report("NegativeAndHuge", d.Highscore == 0 && d.BestDistance is 0 or GameConstants.MaxScoreValue, $"highscore={d.Highscore} best={d.BestDistance}");

        // 6. tiefe Verschachtelung exakt an der 4-KB-Grenze
        string deep = "{\"version\":1,\"x\":" + new string('[', 1000) + new string(']', 1000) + "}";
        WriteText(hs, deep);
        d = SaveSystem.LoadHighscore();
        Report("DeepNesting", d.Highscore == 0, $"highscore={d.Highscore}");

        // 7. Duplikat-Schlüssel
        WriteText(hs, "{\"version\":1,\"highscore\":5,\"highscore\":9999999999}");
        d = SaveSystem.LoadHighscore();
        Report("DuplicateKeysClamped", d.Highscore <= GameConstants.MaxScoreValue, $"highscore={d.Highscore}");

        // 8. Version als Gleitkommazahl
        WriteText(hs, "{\"version\":1.9,\"highscore\":42}");
        d = SaveSystem.LoadHighscore();
        Report("FractionalVersionRejected", d.Highscore == 0, $"version 1.9 → highscore={d.Highscore}");

        // 9. Verzeichnis statt Datei: Laden und Speichern dürfen nicht werfen
        RemoveAny(hs);
        DirAccess.MakeDirAbsolute(ProjectSettings.GlobalizePath(hs));
        d = SaveSystem.LoadHighscore();
        bool saved = SaveSystem.SaveHighscore(new HighscoreData { Highscore = 1, BestDistance = 1 });
        Report("DirectoryInsteadOfFile", d.Highscore == 0, $"load={d.Highscore} save={saved}");
        RemoveAny(hs);
        RemoveAny(hs + ".tmp");

        // 10. Atomares Schreiben: tmp ist Verzeichnis → Original bleibt erhalten
        WriteText(hs, "{\"version\":1,\"highscore\":777,\"best_distance\":1}");
        DirAccess.MakeDirAbsolute(ProjectSettings.GlobalizePath(hs + ".tmp"));
        saved = SaveSystem.SaveHighscore(new HighscoreData { Highscore = 1, BestDistance = 1 });
        d = SaveSystem.LoadHighscore();
        Report("AtomicWriteKeepsOriginal", !saved && d.Highscore == 777, $"save={saved} afterLoad={d.Highscore}");
        RemoveAny(hs + ".tmp");

        // 11. Roundtrip mit Clamping beim Schreiben
        saved = SaveSystem.SaveHighscore(new HighscoreData { Highscore = int.MaxValue, BestDistance = -5 });
        d = SaveSystem.LoadHighscore();
        Report("WriteClamp", saved && d.Highscore == GameConstants.MaxScoreValue && d.BestDistance == 0,
            $"highscore={d.Highscore} best={d.BestDistance} tmpLeft={Godot.FileAccess.FileExists(hs + ".tmp")}");

        // 12. Settings: NaN/Infinity/Strings/Riesenwerte
        WriteText(st, "{\"version\":1,\"mouse_sensitivity\":1e39,\"invert_y\":1}");
        SettingsData s = SaveSystem.LoadSettings();
        Report("SettingsHugeFloat", s.MouseSensitivity >= GameConstants.MouseSensitivityMin && s.MouseSensitivity <= GameConstants.MouseSensitivityMax && !s.InvertY,
            $"sens={s.MouseSensitivity} invert={s.InvertY}");
        WriteText(st, "{\"version\":1,\"mouse_sensitivity\":\"NaN\",\"invert_y\":null}");
        s = SaveSystem.LoadSettings();
        Report("SettingsWrongTypes", Mathf.IsEqualApprox(s.MouseSensitivity, GameConstants.MouseSensitivityDefault), $"sens={s.MouseSensitivity}");

        // 13. Leere Datei
        WriteBytes(st, Array.Empty<byte>());
        s = SaveSystem.LoadSettings();
        Report("SettingsEmptyFile", Mathf.IsEqualApprox(s.MouseSensitivity, GameConstants.MouseSensitivityDefault), $"sens={s.MouseSensitivity}");

        // 14. Laufzeit-Setter klemmen
        GameManager gm = GameManager.Current;
        float old = gm.MouseSensitivity;
        gm.MouseSensitivity = float.NaN;
        bool nanOk = float.IsFinite(gm.MouseSensitivity);
        gm.MouseSensitivity = 1000f;
        bool hiOk = gm.MouseSensitivity <= GameConstants.MouseSensitivityMax;
        gm.MouseSensitivity = old;
        Report("SensitivitySetterClamp", nanOk && hiOk, $"nanOk={nanOk} hiOk={hiOk}");

        // 15. ReportPlayerZ / AddOriginOffset mit NaN/Inf
        gm.StartNewRun(1);
        gm.ReportPlayerZ(float.NaN);
        gm.ReportPlayerZ(float.NegativeInfinity);
        gm.AddOriginOffset(float.PositiveInfinity);
        gm.ReportPlayerZ(-1e30f);
        Report("ScoreNonFiniteInputs", gm.Distance == GameConstants.MaxScoreValue && float.IsFinite(gm.OriginOffsetZ) && gm.Score <= GameConstants.MaxScoreValue,
            $"distance={gm.Distance} offset={gm.OriginOffsetZ} score={gm.Score}");
        gm.StartNewRun(1);
    }
}
