using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using JumpRun.Audio;
using JumpRun.Characters;
using JumpRun.Game;
using JumpRun.Items;
using JumpRun.Player;
using JumpRun.UI;
using JumpRun.World;

namespace JumpRun.Tests;

/// <summary>
/// Tester v2 (Bugs + Sicherheit): unbegrenztes Inventar + HUD, Save-Validierung der neuen Felder, Trampolin auf
/// Drehplattform, Audio-Pool/NaN-Lautstärke/Loop in der Pause, use_item+Helikopter im Enterhaken,
/// Sturztod auf gültiger Plattform nach übersprungenen Checkpoints, verketteter Helikopterflug (Höhe, Checkpoints,
/// Chunk-/Node-Wachstum, Origin-Shift im Flug, Rig endlich).
/// Start: godot --headless --fixed-fps 60 --path . res://tests/TesterV2Test.tscn
/// Ausgabe je Fall "[V2] PASS|KNOWN|FAIL name: details", letzte Zeile TESTER_V2_DONE pass=N known=K fail=M.
/// Exit-Code 0 = keine neuen FAILs (bekannte Befunde V2-xx aus docs/TEST_REPORT_V2.md zählen als KNOWN),
/// 1 = mindestens ein FAIL (mit "-- --strict" auch KNOWN), 2 = Testtreiber-Absturz.
/// </summary>
public partial class TesterV2Test : Node
{
    private Main _main = null!;
    private WorldGenerator _gen = null!;
    private PlayerController _p = null!;
    private int _pass, _fail;
    private int _respawns, _exceptions;
    private string? _firstException;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        GameManager.Current.PersistenceEnabled = false;
        GameManager.Current.StartNewRun(4242);
        _main = ResourceLoader.Load<PackedScene>(GameManager.MainScenePath).Instantiate<Main>();
        _main.ProcessMode = ProcessModeEnum.Pausable;
        AddChild(_main);
        _gen = _main.Generator!;
        _p = _main.Player!;
        _p.Respawned += () => _respawns++;
        AppDomain.CurrentDomain.FirstChanceException += OnEx;
        Run();
    }

    public override void _ExitTree() => AppDomain.CurrentDomain.FirstChanceException -= OnEx;

    private void OnEx(object? s, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs e)
    {
        _exceptions++;
        _firstException ??= e.Exception.GetType().Name + ": " + e.Exception.Message;
    }

    private SignalAwaiter Frame() => ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    private async Task Frames(int n) { for (int i = 0; i < n; i++) await Frame(); }

    /// <summary>
    /// Bekannte Befunde aus docs/TEST_REPORT_V2.md: ein FAIL wird als KNOWN gemeldet und lässt den Lauf nicht scheitern
    /// (Gesamtlauf bleibt grün); ist ein Befund behoben, erscheint „PASS … (V2-xx behoben)“. Mit
    /// <c>-- --strict</c> zählen auch bekannte Befunde als FAIL.
    /// </summary>
    private static readonly Dictionary<string, string> Known = new()
    {
        ["HeliChainAltitudeBounded"] = "V2-01",
        ["HeliChainAliveChunksBounded"] = "V2-02",
        ["HeliChainSkipsCheckpoints"] = "V2-03",
        ["TrampolineOnRotatingPlatform"] = "V2-04",
        ["UseItemHeliDuringGrapple"] = "V2-05",
        ["AudioVolumeNaN"] = "V2-06",
        ["AudioHeliLoopPausedWithGame"] = "V2-07",
    };
    private int _known;
    private readonly bool _strict = Array.IndexOf(OS.GetCmdlineUserArgs(), "--strict") >= 0;

    private void Report(string name, bool ok, string details)
    {
        bool known = Known.TryGetValue(name, out string? id);
        if (ok) { _pass++; GD.Print($"[V2] PASS {name}: {details}{(known ? $" ({id} behoben)" : "")}"); return; }
        if (known && !_strict) { _known++; GD.Print($"[V2] KNOWN {id} {name}: {details}"); return; }
        _fail++;
        GD.Print($"[V2] FAIL {name}: {details}");
    }

    private async void Run()
    {
        try
        {
            await Frames(10);
            InventoryOverflow();
            await HudLargeCounts();
            SaveValidation();
            await TrampolineOnRotating();
            await UseItemHeliWhileGrappling();
            await AudioChecks();
            await FallDeathOnValidPlatform();
            await ChainedFlight();
            FixRoundChecks();
            Report("Exceptions", _exceptions == 0, _exceptions == 0 ? "keine" : $"{_exceptions}× z. B. {_firstException}");
        }
        catch (Exception e)
        {
            GD.PrintErr("TESTER_V2_CRASH: " + e);
            AudioManager.Instance?.StopAll();
            GetTree().Quit(2);
            return;
        }
        foreach (string a in new[] { "jump", "move_forward", "sprint", "use_item" }) Input.ActionRelease(a);
        AudioManager.Instance?.StopAll();
        GD.Print($"TESTER_V2_DONE pass={_pass} known={_known} fail={_fail}");
        GetTree().Quit(_fail == 0 ? 0 : 1);
    }

    // ------------------------------------------------------------------ Inventar

    private void InventoryOverflow()
    {
        var inv = new Inventory();
        AddChild(inv);
        int added = 0;
        inv.ItemAdded += (_, _) => added++;
        bool a1 = inv.Add(ItemType.Bridge, int.MaxValue);
        bool a2 = inv.Add(ItemType.Bridge, int.MaxValue);
        bool a3 = inv.Add(ItemType.Bridge);
        int c = inv.GetCount(ItemType.Bridge);
        Report("InventorySaturates", a1 && a2 && a3 && c == GameConstants.InventoryHardCap && added == 3,
            $"Add(int.Max)×2 + Add(1) → {c} (Cap {GameConstants.InventoryHardCap}), ItemAdded={added}");
        bool rejects = !inv.Add(ItemType.None) && !inv.Add((ItemType)99) && !inv.Add(ItemType.Helicopter, 0)
                       && !inv.Add(ItemType.Helicopter, -5) && inv.GetCount(ItemType.Helicopter) == 0
                       && !inv.Consume(ItemType.Helicopter) && inv.GetCount((ItemType)(-3)) == 0;
        Report("InventoryInvalidArgs", rejects, "None/99/0/-5 abgelehnt, Consume bei 0 false, keine negativen Zähler");
        inv.Select(ItemType.Helicopter);
        inv.SelectNext(1);
        bool wrap = inv.Selected == ItemType.Bridge;
        inv.SelectNext(-1);
        Report("InventorySelectWrap", wrap && inv.Selected == ItemType.Helicopter, $"4 → 1 → 4 ({inv.Selected})");
        inv.QueueFree();
    }

    private async Task HudLargeCounts()
    {
        Inventory inv = _p.Inventory;
        inv.Clear();
        foreach (ItemType t in new[] { ItemType.Bridge, ItemType.Trampoline, ItemType.GrappleHook, ItemType.Helicopter })
            inv.Add(t, int.MaxValue);
        await Frames(5);
        HUD? hud = _main.Hud;
        Label? l4 = hud?.GetNodeOrNull<Label>("%ItemSlot4Count");
        Control? bar = hud?.GetNodeOrNull<Control>("BottomBar");
        // Headless-Viewport ist 64×64 → gegen die Projektauflösung 1280×720 prüfen (Bar ist mittig verankert).
        var vp = new Rect2(0, 0, 1280, 720);
        bool textOk = l4 != null && l4.Text == "x999999";
        Control? slots = hud?.GetNodeOrNull<Control>("BottomBar/Slots");
        float needW = slots?.GetCombinedMinimumSize().X ?? 0f;
        bool inside = bar != null && needW <= vp.Size.X - 32f;
        float clip = 0f;
        for (int i = 1; i <= 4; i++)
        {
            Label? l = hud?.GetNodeOrNull<Label>($"%ItemSlot{i}Count");
            Control? slot = hud?.GetNodeOrNull<Control>($"%ItemSlot{i}");
            if (l == null || slot == null) continue;
            float need = l.GetMinimumSize().X;
            clip = Mathf.Max(clip, need - slot.Size.X);
        }
        Report("HudHugeCounts", textOk && inside && clip <= 0f,
            $"Slot4='{l4?.Text}' Slot-Leiste min. Breite={needW:F0}px (Bar {bar?.Size.X:F0}px) bei 1280 px, maxLabelOverflow={clip:F1}px");
        inv.Clear();
    }

    // ------------------------------------------------------------------ Save-Validierung (neue Felder)

    private void SaveValidation()
    {
        SaveSystem.QuietWarnings = true;
        try
        {
            var cases = new (string Json, string Expect, string Name)[]
            {
                ("{\"version\":1,\"character\":\"knight\",\"character\":\"../../etc\"}", CharacterCatalog.DefaultId + "|knight", "Doppelter Schlüssel"),
                ("{\"version\":1,\"character\":\"knight\\u0000\"}", CharacterCatalog.DefaultId, "NUL-Byte"),
                ("{\"version\":1,\"character\":\"KNIGHT\"}", CharacterCatalog.DefaultId, "Großbuchstaben"),
                ("{\"version\":1,\"character\":\"knight \"}", CharacterCatalog.DefaultId, "Leerzeichen"),
                ("{\"version\":1,\"character\":\"\\u006bnight\"}", "knight", "Unicode-Escape gültig"),
                ("{\"version\":1,\"character\":\"kn\\u0131ght\"}", CharacterCatalog.DefaultId, "Homoglyph (dotless i)"),
                ("{\"version\":1,\"character\":[\"knight\"]}", CharacterCatalog.DefaultId, "Array"),
                ("{\"version\":1,\"character\":{\"id\":\"knight\"}}", CharacterCatalog.DefaultId, "Objekt"),
                ("{\"version\":1,\"character\":3}", CharacterCatalog.DefaultId, "Zahl"),
                ("{\"version\":1,\"character\":\"" + new string('a', 5000) + "\"}", CharacterCatalog.DefaultId, "Überlang (> 4 KB)"),
            };
            foreach (var c in cases)
            {
                SettingsData s = SaveSystem.ParseSettings(c.Json);
                bool ok = Array.IndexOf(c.Expect.Split('|'), s.Character) >= 0;
                Report("SaveCharacter " + c.Name, ok, $"→ '{s.Character}'");
            }
            var vol = new (string Json, string Name)[]
            {
                ("{\"version\":1,\"music_volume\":1e309,\"sfx_volume\":-1e309}", "±1e309"),
                ("{\"version\":1,\"music_volume\":\"NaN\",\"sfx_volume\":\"Infinity\"}", "Strings"),
                ("{\"version\":1,\"music_volume\":-0.0,\"sfx_volume\":1.0000001}", "-0 / >1"),
                ("{\"version\":1,\"music_volume\":4.9e-324,\"sfx_volume\":null}", "Denormal / null"),
                ("{\"version\":1,\"music_volume\":true,\"sfx_volume\":[1]}", "bool/array"),
            };
            foreach (var v in vol)
            {
                SettingsData s = SaveSystem.ParseSettings(v.Json);
                bool ok = float.IsFinite(s.MusicVolume) && float.IsFinite(s.SfxVolume) && s.MusicVolume >= 0f && s.MusicVolume <= 1f
                          && s.SfxVolume >= 0f && s.SfxVolume <= 1f;
                Report("SaveVolume " + v.Name, ok, $"music={s.MusicVolume} sfx={s.SfxVolume}");
            }
            // Serialisierung schreibt nie eine ungültige Id / Volumes.
            byte[] bytes = SaveSystem.SerializeSettings(new SettingsData { Character = "../x\"}", MusicVolume = float.NaN, SfxVolume = float.PositiveInfinity });
            string json = System.Text.Encoding.UTF8.GetString(bytes);
            SettingsData back = SaveSystem.ParseSettings(json);
            Report("SaveSerializeSanitizes", back.Character == CharacterCatalog.DefaultId && float.IsFinite(back.MusicVolume)
                    && float.IsFinite(back.SfxVolume) && !json.Contains("../") && bytes.Length < GameConstants.MaxSaveFileBytes,
                $"{json.Replace('\n', ' ')}");
        }
        finally { SaveSystem.QuietWarnings = false; }
    }

    // ------------------------------------------------------------------ Trampolin auf Drehplattform

    private async Task TrampolineOnRotating()
    {
        var layout = new ChunkLayout { Index = 90001, Pattern = PatternType.SpinningDiscs, EntryPoint = new Vector3(400f, 60f, 0f) };
        layout.CheckpointLocal = new Vector3(0f, -400f, 0f); // Trigger weit weg
        layout.Platforms.Add(new PlatformSpec
        {
            Center = new Vector3(0f, -0.3f, -5f), Size = new Vector3(6f, 0.6f, 6f), Shape = PlatformShape.Triangle,
            SpinSpeed = GameConstants.RotatingMaxAngularSpeed, Material = MaterialNames.PlatformSpin,
        });
        Chunk chunk = ChunkBuilder.Instantiate(layout);
        AddChild(chunk);
        await Frames(2);
        _p.StopFlight();
        _p.GlobalPosition = layout.EntryPoint + new Vector3(0f, 0.5f, -5.5f); // Trampolin 1,5 m voraus → 2,0 m vor der Mitte (Ecke bei 3,0 m, Innenkreis 1,5 m)
        _p.Velocity = Vector3.Zero;
        _p.CameraRig!.SnapBehind(Vector3.Forward);
        _p.SetCheckpoint(layout.EntryPoint + new Vector3(0f, 0f, -5f), _p.ActiveCheckpointChunk);
        await Frames(30);
        bool onRot = _p.IsOnMovingPlatform();
        // Bis zu 7 s lang versuchen (Dreieck dreht; Ziel 2,0 m vor der Mitte liegt nur zeitweise über der Fläche).
        bool placed = false;
        string reason = "";
        for (int i = 0; i < 420 && !placed; i++)
        {
            placed = TrampolineItem.TryUse(_p, out reason);
            if (!placed) await Frame();
        }
        int floating = 0, frames = 0;
        Node3D? tramp = null;
        foreach (Node n in GetTree().GetNodesInGroup("placeable"))
            if (n is Trampoline t) tramp = t;
        if (placed && tramp != null)
        {
            Vector3 tp = tramp.GlobalPosition;
            // Spieler wegnehmen, damit er das Trampolin nicht auslöst
            _p.GlobalPosition = layout.EntryPoint + new Vector3(0f, 30f, 30f);
            for (int i = 0; i < 420; i++)
            {
                await Frame();
                PhysicsDirectSpaceState3D space = _p.GetWorld3D().DirectSpaceState;
                var q = PhysicsRayQueryParameters3D.Create(tp + Vector3.Up * 0.05f, tp + Vector3.Down * 0.6f, GameConstants.LayerWorld);
                if (space.IntersectRay(q).Count == 0) floating++;
                frames++;
            }
        }
        Report("TrampolineOnRotatingPlatform", !placed || floating == 0,
            $"onRotating={onRot} placed={placed} reason='{reason}' trampolineWithoutGround={floating}/{frames} Frames (7 s, Dreieck 0,9 rad/s)");
        foreach (Node n in GetTree().GetNodesInGroup("placeable")) n.QueueFree();
        chunk.QueueFree();
        await Frames(2);
    }

    // ------------------------------------------------------------------ use_item mit gewähltem Helikopter im Enterhaken

    private async Task UseItemHeliWhileGrappling()
    {
        await ToStart();
        var anchorScene = ResourceLoader.Load<PackedScene>("res://scenes/objects/GrappleAnchor.tscn");
        var anchor = anchorScene.Instantiate<GrappleAnchor>();
        AddChild(anchor);
        anchor.GlobalPosition = _p.GlobalPosition + new Vector3(0f, 12f, -20f);
        _p.Inventory.Add(ItemType.Helicopter, 2);
        _p.StartGrapple(anchor);
        await Frames(2);
        _p.Inventory.SelectNext(1); // Mausrad während des Hakens → Helikopter (Bridge→...)
        while (_p.Inventory.Selected != ItemType.Helicopter) _p.Inventory.SelectNext(1);
        Input.ActionPress("use_item");
        await Frame();
        Input.ActionRelease("use_item");
        await Frames(2);
        bool flying = _p.IsFlying;
        Report("UseItemHeliDuringGrapple", flying,
            $"grappling={_p.IsGrappling} flying={flying} (CONTRACT_V2 §3.3: Haken endet und Flug startet; use_item mit gewähltem Helikopter)");
        _p.StopFlight();
        anchor.QueueFree();
        _p.Inventory.Clear();
        await Frames(5);
    }

    // ------------------------------------------------------------------ Audio

    private async Task AudioChecks()
    {
        AudioManager? am = AudioManager.Instance;
        if (am == null) { Report("AudioAutoload", false, "AudioManager.Instance null"); return; }
        bool prev = am.PlaybackEnabled;
        am.PlaybackEnabled = true;
        int playersBefore = CountPlayers(GetTree().Root);
        for (int i = 0; i < 400; i++)
        {
            am.PlaySfx(Sfx.Jump);
            am.PlaySfx(Sfx.CrateBreak, new Vector3(i, 0, 0));
            am.PlaySfx("nicht_vorhanden_" + (i % 3));
            if (i % 2 == 0) am.SetLoop(Sfx.HelicopterLoop, i % 4 == 0);
            if (i % 50 == 0) await Frame();
        }
        await Frames(2);
        int playersAfter = CountPlayers(GetTree().Root);
        Report("AudioNoPlayerGrowth", playersAfter <= playersBefore + 1 && am.ActiveVoices2D <= AudioManager.Pool2DSize,
            $"AudioStreamPlayer(2D/3D) im Baum vorher={playersBefore} nachher={playersAfter}, aktiv2D={am.ActiveVoices2D}");

        // NaN-Lautstärke direkt am AudioManager (öffentliche API) darf den Bus nicht vergiften.
        am.MusicVolume = float.NaN;
        am.SfxVolume = float.PositiveInfinity;
        float mdb = AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex(AudioManager.MusicBus));
        float sdb = AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex(AudioManager.SfxBus));
        Report("AudioVolumeNaN", float.IsFinite(mdb) && float.IsFinite(sdb) && float.IsFinite(am.MusicVolume),
            $"MusicVolume=NaN → get={am.MusicVolume} busDb={mdb}; SfxVolume=+Inf → busDb={sdb}");
        am.MusicVolume = 0.7f;
        am.SfxVolume = 0.8f;

        // Helikopter-Loop im Pause-Menü
        await ToStart();
        _p.Inventory.Add(ItemType.Helicopter, 1);
        _p.ActivateItem(ItemType.Helicopter);
        await Frames(5);
        bool loopFlying = am.IsLoopActive(Sfx.HelicopterLoop);
        PauseMenu? pause = _main.Pause;
        pause?.Toggle();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        bool loopPaused = am.IsLoopActive(Sfx.HelicopterLoop);
        if (pause != null && pause.IsOpen) pause.Toggle();
        await Frames(2);
        Report("AudioHeliLoopPausedWithGame", loopFlying && !loopPaused,
            $"Loop im Flug={loopFlying}, Loop bei geöffnetem Pause-Menü={loopPaused} (Flug ist eingefroren)");
        _p.StopFlight();
        await Frames(2);
        am.StopAll();
        am.PlaybackEnabled = prev;
    }

    /// <summary>Fix-Runde v2: Grafikqualität (R V2-04) und Speichern des Charakters ohne Debug-Override (V2-08).</summary>
    private void FixRoundChecks()
    {
        GameManager gm = GameManager.Current;
        bool old = gm.GraphicsLow;
        var env = _main.GetNodeOrNull<WorldEnvironment>("WorldEnvironment")?.Environment;
        var sun = _main.GetNodeOrNull<DirectionalLight3D>("Lighting/Sun");
        gm.GraphicsLow = true;
        bool lowOk = sun != null && sun.DirectionalShadowMode == DirectionalLight3D.ShadowMode.Parallel2Splits
                     && !sun.DirectionalShadowBlendSplits && sun.DirectionalShadowMaxDistance <= 60f
                     && env != null && !env.SsaoEnabled;
        gm.GraphicsLow = false;
        bool highOk = sun != null && sun.DirectionalShadowMode == DirectionalLight3D.ShadowMode.Parallel4Splits
                      && sun.DirectionalShadowMaxDistance > 60f && env != null && env.SsaoEnabled;
        gm.GraphicsLow = old;
        Report("GraphicsQualityToggle", lowOk && highOk, $"low={lowOk} high={highOk}");

        SettingsData back = SaveSystem.ParseSettings(System.Text.Encoding.UTF8.GetString(
            SaveSystem.SerializeSettings(new SettingsData { GraphicsLow = true })));
        SettingsData none = SaveSystem.ParseSettings("{\"version\":1,\"graphics_low\":\"yes\"}");
        Report("GraphicsQualitySave", back.GraphicsLow == true && none.GraphicsLow == null,
            $"roundtrip={back.GraphicsLow} invalid={none.GraphicsLow?.ToString() ?? "null"}");
    }

    private static int CountPlayers(Node n)
    {
        int c = n is AudioStreamPlayer or AudioStreamPlayer3D ? 1 : 0;
        foreach (Node ch in n.GetChildren()) c += CountPlayers(ch);
        return c;
    }

    private async Task ToStart()
    {
        _p.StopGrapple(false);
        _p.StopFlight();
        Chunk? c0 = _gen.GetChunk(_p.ActiveCheckpointChunk);
        _p.Respawn(false);
        _p.Velocity = Vector3.Zero;
        await Frames(15);
        _ = c0;
    }

    // ------------------------------------------------------------------ Sturztod auf gültiger Plattform

    private async Task FallDeathOnValidPlatform()
    {
        // Suche: Seed/Chunk k, dessen Checkpoint-Höhe mehr als FallDeathDepth über einer statischen Plattform eines
        // späteren Chunks j ≤ k+6 liegt (erreichbar per verkettetem Helikopterflug über die Checkpoint-Trigger).
        int foundSeed = -1, foundK = -1, foundJ = -1, foundP = -1;
        float maxDrop = 0f;
        for (int seed = 1; seed <= 400 && foundSeed < 0; seed++)
        {
            var b = new ChunkBuilder(seed);
            var layouts = new List<ChunkLayout>();
            Vector3 entry = Vector3.Zero;
            PatternType? prev = null;
            int streak = 0;
            for (int i = 0; i < 70; i++)
            {
                bool prevItem = prev.HasValue && prev.Value.IsItemRequired();
                ChunkLayout l = b.Plan(i, entry, prev, streak, prevItem, ChunkBuilder.DifficultyForIndex(i));
                layouts.Add(l);
                streak = prev.HasValue && prev.Value == l.Pattern ? streak + 1 : 1;
                prev = l.Pattern;
                entry = l.ExitPoint;
            }
            for (int k = 1; k < 40 && foundSeed < 0; k++)
            {
                float respawnY = layouts[k].CheckpointPosition.Y + 1f;
                for (int j = k + 1; j <= k + 25 && foundSeed < 0; j++)
                {
                    ChunkLayout lj = layouts[j];
                    for (int pi = 1; pi < lj.Platforms.Count; pi++)
                    {
                        PlatformSpec p = lj.Platforms[pi];
                        if (p.IsMoving || p.IsRotating || p.Shape == PlatformShape.Ramp) continue;
                        maxDrop = Mathf.Max(maxDrop, respawnY - (lj.EntryPoint.Y + p.TopY));
                        if (lj.EntryPoint.Y + p.TopY < respawnY - GameConstants.FallDeathDepth - 0.5f)
                        {
                            foundSeed = seed; foundK = k; foundJ = j; foundP = pi;
                            break;
                        }
                    }
                }
            }
        }
        if (foundSeed < 0) { Report("FallDeathOnValidPlatform", true, $"kein Fall in 400 Seeds × 40 Checkpoints × 25 Folge-Chunks (max. Abstieg {maxDrop:F1} m < {GameConstants.FallDeathDepth} m)"); return; }

        _gen.Reset(foundSeed);
        await Frames(10);
        // Legal bis Chunk k laufen (Checkpoint-Trigger je Pad auslösen)
        for (int idx = 1; idx <= foundK; idx++)
        {
            Chunk? c = null;
            for (int w = 0; w < 30 && (c = _gen.GetChunk(idx)) == null; w++) await Frame();
            if (c == null) { Report("FallDeathOnValidPlatform", false, $"Chunk {idx} nicht gespawnt"); return; }
            _p.GlobalPosition = c.CheckpointPosition + Vector3.Up * 0.3f;
            _p.Velocity = Vector3.Zero;
            await Frames(8);
        }
        int cp = _p.ActiveCheckpointChunk;
        // Hoch über die Pads k+1..j „fliegen“ (Teleport 35 m über dem Pfad, löst keinen Checkpoint aus).
        Chunk? cj = null;
        for (int idx = foundK + 1; idx <= foundJ; idx++)
        {
            Chunk? c = null;
            for (int w = 0; w < 30 && (c = _gen.GetChunk(idx)) == null; w++) await Frame();
            if (c == null) break;
            _p.GlobalPosition = c.CheckpointPosition + Vector3.Up * 35f;
            _p.Velocity = Vector3.Zero;
            await Frames(3);
            cj = c;
        }
        cp = Mathf.Min(cp, _p.ActiveCheckpointChunk == cp ? cp : -1);
        if (cj == null || cp != foundK) { Report("FallDeathOnValidPlatform", false, $"Setup: cp={cp} k={foundK} cj={cj != null}"); return; }
        // Zustand wie nach einem Flug über die Pads k+1..j hinweg: Spieler steht auf Plattform foundP in Chunk j.
        Node3D plat = cj.GetNode<Node3D>($"Platform_{foundP}");
        var aabb = new ChunkBuilder(foundSeed); _ = aabb;
        int r0 = _respawns;
        Vector3 standAt = plat.GlobalPosition + Vector3.Up * 2f;
        _p.GlobalPosition = standAt;
        _p.Velocity = Vector3.Zero;
        bool everOnFloor = false;
        for (int i = 0; i < 60; i++) { await Frame(); everOnFloor |= _p.IsOnFloor(); }
        int died = _respawns - r0;
        Report("FallDeathOnValidPlatform", died == 0,
            $"seed={foundSeed} checkpoint={foundK} (respawnY={_p.RespawnPosition.Y:F1}) Plattform {foundP} in Chunk {foundJ} bei y≈{plat.GlobalPosition.Y:F1}: " +
            $"Respawns in 1 s={died}, berührte Boden={everOnFloor}");
    }

    // ------------------------------------------------------------------ Verketteter Helikopterflug

    private async Task ChainedFlight()
    {
        _gen.Reset(777);
        await Frames(20);
        _p.Inventory.Clear();
        _p.Inventory.Add(ItemType.Helicopter, 40);
        int cp0 = _p.ActiveCheckpointChunk;
        float startY = _p.GlobalPosition.Y;
        float maxY = startY;
        int maxAlive = 0, maxNodes = 0;
        int shifts = 0;
        float shiftTotal = GameManager.Current.OriginOffsetZ;
        bool velJump = false, rigFinite = true;
        Vector3 lastV = _p.Velocity;
        _gen.OriginShifted += _ => shifts++;
        _p.DebugAutoRun = true;
        Input.ActionPress("jump");
        int flights = 0;
        int nodesStart = GetTree().GetNodeCount();
        Node3D? rig = _p.GetNodeOrNull<Node3D>("Visual/Rig");
        for (int f = 0; f < 40; f++)
        {
            if (_p.IsFlying) _p.ActivateItem(ItemType.Helicopter); // beenden …
            _p.ActivateItem(ItemType.Helicopter);                    // … und sofort neu starten
            if (_p.IsFlying) flights++;
            for (int i = 0; i < 195 && _p.IsFlying; i++)
            {
                await Frame();
                maxY = Mathf.Max(maxY, _p.GlobalPosition.Y);
                maxAlive = Mathf.Max(maxAlive, _gen.AliveChunkCount);
                if ((_p.Velocity - lastV).Length() > 8f) velJump = true;
                lastV = _p.Velocity;
                if (rig != null && i % 30 == 0) rigFinite &= AllFinite(rig);
            }
            maxNodes = Mathf.Max(maxNodes, GetTree().GetNodeCount());
        }
        Input.ActionRelease("jump");
        int cpAfter = _p.ActiveCheckpointChunk;
        int current = _p.CurrentChunkIndex;
        float dist = GameManager.Current.Distance;
        Report("HeliChainSkipsCheckpoints", cpAfter > cp0 || current <= cp0 + 1,
            $"{flights} Flüge, Strecke {dist} m, Spieler-Chunk {current}, aktiver Checkpoint {cp0} → {cpAfter}");
        Report("HeliChainAltitudeBounded", maxY - startY <= GameConstants.HelicopterMaxRise + 6f,
            $"max. Höhe über Start {maxY - startY:F0} m (pro Flug ≤ {GameConstants.HelicopterMaxRise} m, kein globales Limit; MaxWorldY={GameConstants.MaxWorldY})");
        Report("HeliChainAliveChunksBounded", maxAlive <= GameConstants.MaxAliveChunks,
            $"max. AliveChunkCount={maxAlive} (MaxAliveChunks={GameConstants.MaxAliveChunks}), Nodes {nodesStart} → max {maxNodes}");
        Report("HeliOriginShiftInFlight", shifts > 0 && !velJump && rigFinite,
            $"OriginShifts im Flug={shifts} (Offset {GameManager.Current.OriginOffsetZ - shiftTotal:F0}), Geschwindigkeitssprung={velJump}, Rig endlich={rigFinite}");

        // Absturz nach dem Flug → Respawn am alten Checkpoint, Pfad muss lückenlos sein.
        _p.DebugAutoRun = false;
        _p.StopFlight();
        int r0 = _respawns;
        for (int i = 0; i < 60 * 25 && _respawns == r0; i++) await Frame();
        await Frames(30);
        bool contiguous = true;
        int cpNow = _p.ActiveCheckpointChunk;
        for (int idx = cpNow; idx <= cpNow + 4; idx++) contiguous &= _gen.GetChunk(idx) != null;
        Report("HeliChainRespawnPathIntact", contiguous,
            $"Respawn={_respawns > r0} (Spieler y={_p.GlobalPosition.Y:F1}, Boden={_p.IsOnFloor()}) Checkpoint {cpNow}, Chunks {cpNow}..{cpNow + 4} vorhanden={contiguous}, alive={_gen.AliveChunkCount}");
    }

    private static bool AllFinite(Node n)
    {
        if (n is Node3D n3 && (!n3.Position.IsFinite() || !n3.Rotation.IsFinite() || !n3.Scale.IsFinite())) return false;
        foreach (Node c in n.GetChildren())
            if (!AllFinite(c)) return false;
        return true;
    }
}
