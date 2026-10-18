using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using JumpRun.Characters;
using JumpRun.Game;
using JumpRun.Player;
using JumpRun.UI;

namespace JumpRun.Tests;

/// <summary>Fake-Spielerzustand für das Rig (ersetzt PlayerController).</summary>
public partial class FakeAnimPlayer : Node3D, IPlayerAnimState
{
    public Vector3 FakeVelocity;
    public bool FakeOnFloor = true;
    public Vector3 Velocity => FakeVelocity;
    public bool IsOnFloor() => FakeOnFloor;
    public bool IsGrappling => AnimMode == PlayerAnimMode.Grapple;
    public bool IsFlying => AnimMode == PlayerAnimMode.Fly;
    public float MoveSpeedRatio { get; set; }
    public float HorizontalSpeed { get; set; }
    public Vector3 Facing => Vector3.Forward;
    public Vector3 GrappleTargetPosition { get; set; }
    public float FlightFuelRatio => IsFlying ? 0.5f : 0f;
    public int AttackCount { get; set; }
    public int JumpCount { get; set; }
    public int LandCount { get; set; }
    public PlayerAnimMode AnimMode { get; set; }
    public float ModelTopHeight { get; set; } = 2.0f;
}

/// <summary>
/// CHARACTERS-Test (TASKS_V2 C6): Katalog, Modellbau aller 4 Charaktere (Hierarchie, Limits, Höhe),
/// Rig-Animation mit Fake-IPlayerAnimState, Settings-Roundtrip/Validierung, Auswahl-Screen + Hauptmenü-Button.
/// </summary>
public partial class CharacterTest : Node
{
    private readonly List<string> _fails = new();
    private readonly List<FakeAnimPlayer> _fakes = new();
    private readonly List<CharacterRig> _fakeRigs = new();
    private CharacterSelect? _select;
    private int _frame;
    private float _maxLegRun;
    private float _turntableStartY;
    private string _selectStartId = "";

    private void Check(bool ok, string what)
    {
        if (!ok) { _fails.Add(what); GD.Print("  FAIL " + what); }
    }

    public override void _Ready()
    {
        if (GameManager.Instance != null) GameManager.Current.PersistenceEnabled = false;
        try
        {
            TestCatalog();
            TestModels();
            TestSettings();
            TestGameManager();
            SetupFakePlayers();
            SetupScreens();
        }
        catch (Exception e)
        {
            Check(false, "Exception in Setup: " + e);
        }
    }

    // ------------------------------------------------------------------ Katalog
    private void TestCatalog()
    {
        var all = CharacterCatalog.All;
        Check(all.Count == 4, "Catalog: 4 Einträge");
        string[] ids = { "bottle", "trashcan", "superhero", "knight" };
        string[] names = { "Flasche", "Mülleimer", "Superheld", "Ritter" };
        for (int i = 0; i < ids.Length && i < all.Count; i++)
        {
            Check(all[i].Id == ids[i], $"Catalog: Reihenfolge {i} = {ids[i]}");
            Check(all[i].DisplayName == names[i], $"Catalog: Name {ids[i]}");
            Check(!string.IsNullOrWhiteSpace(all[i].Description), $"Catalog: Beschreibung {ids[i]}");
        }
        Check(CharacterCatalog.DefaultId == "superhero", "Catalog: Default superhero");
        Check(CharacterCatalog.Get(null).Id == "superhero", "Catalog: Get(null) → Default");
        Check(CharacterCatalog.Get("").Id == "superhero", "Catalog: Get(\"\") → Default");
        Check(CharacterCatalog.Get("drache").Id == "superhero", "Catalog: unbekannt → Default");
        Check(CharacterCatalog.Get("knight").Id == "knight", "Catalog: Get(knight)");
        Check(!CharacterCatalog.IsValid("Knight") && !CharacterCatalog.IsValid(null) && CharacterCatalog.IsValid("bottle"),
            "Catalog: IsValid case-sensitive");
        foreach (string key in CharacterMaterials.Keys)
            Check(ResourceLoader.Exists(CharacterMaterials.Dir + key + ".tres"), $"Material-Datei {key}.tres");
    }

    // ------------------------------------------------------------------ Modelle
    private void TestModels()
    {
        var holder = new Node3D { Name = "ModelHolder" };
        AddChild(holder);
        foreach (CharacterDefinition def in CharacterCatalog.All)
        {
            var rig = new CharacterRig { Name = "Rig_" + def.Id, CharacterIdOverride = def.Id };
            holder.AddChild(rig);
            string id = def.Id;
            Check(rig.CharacterId == id, $"{id}: CharacterId");
            CheckHierarchy(rig, def);
            CheckLimits(rig, def);
        }
        // Mehrfacher Build: alte Kinder sofort weg, Namen bleiben exakt.
        var re = new CharacterRig { Name = "Rebuild", CharacterIdOverride = "bottle" };
        holder.AddChild(re);
        re.Build("knight");
        re.Build("trashcan");
        Check(re.GetChildCount() == 1 && re.GetNodeOrNull("Hips/Body/Head/Lid") != null, "Rebuild: genau ein Hips mit neuem Modell");
        Check(re.GetNodeOrNull("Hips/Body/ArmR/Sword") == null, "Rebuild: altes Schwert entfernt");
        re.Build("unbekannt");
        Check(re.CharacterId == CharacterCatalog.DefaultId, "Rebuild: unbekannte Id → Default");
    }

    private void CheckHierarchy(CharacterRig rig, CharacterDefinition def)
    {
        string[] paths = { "Hips", "Hips/Body", "Hips/Body/Head", "Hips/Body/ArmL", "Hips/Body/ArmR", "Hips/LegL", "Hips/LegR" };
        foreach (string p in paths)
            Check(rig.GetNodeOrNull<Node3D>(p) != null, $"{def.Id}: Knoten {p}");
        switch (def.Style)
        {
            case CharacterStyle.Superhero: Check(rig.GetNodeOrNull("Hips/Body/Cape") != null, $"{def.Id}: Cape"); break;
            case CharacterStyle.Trashcan: Check(rig.GetNodeOrNull("Hips/Body/Head/Lid") != null, $"{def.Id}: Lid"); break;
            case CharacterStyle.Knight:
                Check(rig.GetNodeOrNull("Hips/Body/ArmR/Sword") != null, $"{def.Id}: Sword");
                Check(rig.GetNodeOrNull("Hips/Body/ArmL/Shield") != null, $"{def.Id}: Shield");
                break;
        }
        foreach (string limb in new[] { "Hips/Body/ArmL", "Hips/Body/ArmR", "Hips/LegL", "Hips/LegR" })
        {
            Node? n = rig.GetNodeOrNull(limb);
            bool hasMesh = false;
            if (n != null) foreach (Node c in n.GetChildren()) if (c is MeshInstance3D) hasMesh = true;
            Check(hasMesh, $"{def.Id}: {limb} hat Meshes");
        }
    }

    private void CheckLimits(CharacterRig rig, CharacterDefinition def)
    {
        var meshes = new List<MeshInstance3D>();
        Collect(rig, meshes);
        var mats = new HashSet<Material>();
        int tris = 0;
        float minY = float.MaxValue, maxY = float.MinValue, maxR = 0f;
        Transform3D inv = rig.GlobalTransform.AffineInverse();
        foreach (MeshInstance3D mi in meshes)
        {
            if (mi.MaterialOverride != null) mats.Add(mi.MaterialOverride);
            if (mi.Mesh is PrimitiveMesh pm)
            {
                var arrays = pm.GetMeshArrays();
                if (arrays.Count > (int)Mesh.ArrayType.Index)
                {
                    var idx = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                    var verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                    tris += idx.Length > 0 ? idx.Length / 3 : verts.Length / 3;
                }
            }
            if (mi.Mesh == null) continue;
            Aabb box = mi.Mesh.GetAabb();
            Transform3D t = inv * mi.GlobalTransform;
            for (int c = 0; c < 8; c++)
            {
                Vector3 p = t * box.GetEndpoint(c);
                minY = Mathf.Min(minY, p.Y);
                maxY = Mathf.Max(maxY, p.Y);
                if (mi.GetParent()?.Name == "Body" || mi.GetParent()?.Name == "Head")
                    maxR = Mathf.Max(maxR, Mathf.Max(Mathf.Abs(p.X), Mathf.Abs(p.Z)));
            }
        }
        string id = def.Id;
        Check(meshes.Count > 0 && meshes.Count <= 24, $"{id}: MeshInstance3D {meshes.Count} ≤ 24");
        Check(tris > 0 && tris <= 4000, $"{id}: Dreiecke {tris} ≤ 4000");
        Check(mats.Count > 0 && mats.Count <= 6, $"{id}: Materialien {mats.Count} ≤ 6");
        Check(def.ModelHeight >= 1.6f && def.ModelHeight <= 2.0f, $"{id}: ModelHeight {def.ModelHeight} in 1,6–2,0");
        Check(Mathf.Abs(rig.ModelHeight - def.ModelHeight) < 1e-4f, $"{id}: rig.ModelHeight");
        Check(Mathf.Abs(minY) <= 0.03f, $"{id}: Füße bei y=0 (minY={minY:F3})");
        Check(maxY >= 1.6f && maxY <= 2.0f, $"{id}: AABB-Höhe {maxY:F3} in 1,6–2,0");
        Check(Mathf.Abs(maxY - def.ModelHeight) <= 0.08f, $"{id}: ModelHeight {def.ModelHeight} ≈ AABB {maxY:F3}");
        Check(maxR <= 0.46f, $"{id}: Rumpf/Kopf-Ausdehnung {maxR:F3} ≤ ~0,45");
        GD.Print($"  {id}: meshes={meshes.Count} tris={tris} mats={mats.Count} height={maxY:F3} radius={maxR:F3}");
    }

    private static void Collect(Node n, List<MeshInstance3D> list)
    {
        foreach (Node c in n.GetChildren())
        {
            if (c is MeshInstance3D mi) list.Add(mi);
            Collect(c, list);
        }
    }

    // ------------------------------------------------------------------ Settings
    private void TestSettings()
    {
        SaveSystem.QuietWarnings = true;
        try
        {
            var d = new SettingsData { MouseSensitivity = 0.004f, InvertY = true, Character = "knight", MusicVolume = 0.25f, SfxVolume = 0.5f };
            string json = Encoding.UTF8.GetString(SaveSystem.SerializeSettings(d));
            Check(json.Contains("\"character\"") && json.Contains("\"music_volume\"") && json.Contains("\"sfx_volume\""), "Settings: neue JSON-Felder");
            Check(json.Contains("\"version\": 1"), "Settings: version bleibt 1");
            SettingsData r = SaveSystem.ParseSettings(json);
            Check(r.Character == "knight" && Mathf.IsEqualApprox(r.MusicVolume, 0.25f) && Mathf.IsEqualApprox(r.SfxVolume, 0.5f) && r.InvertY,
                "Settings: Roundtrip");

            SettingsData old = SaveSystem.ParseSettings("{\"version\":1,\"mouse_sensitivity\":0.003,\"invert_y\":false}");
            Check(old.Character == CharacterCatalog.DefaultId && Mathf.IsEqualApprox(old.MusicVolume, 0.7f) && Mathf.IsEqualApprox(old.SfxVolume, 0.8f),
                "Settings: fehlende Felder → Defaults");

            string[] badChars =
            {
                "5", "true", "null", "\"\"", "\"drache\"", "\"../../x\"", "\"Knight\"", "\"knight \"", "\"kn\\u0000ight\"",
                "[\"knight\"]", "{\"id\":\"knight\"}", "\"" + new string('a', 10_000) + "\"", "\"bottlebottlebottle\"",
            };
            foreach (string bad in badChars)
            {
                SettingsData s = SaveSystem.ParseSettings("{\"version\":1,\"character\":" + bad + "}");
                Check(s.Character == CharacterCatalog.DefaultId, $"Settings: ungültiger character {Short(bad)} → Default");
            }
            CheckVol("-5", 0f, "−5 → 0");
            CheckVol("7", 1f, "7 → 1");
            CheckVol("\"NaN\"", -1f, "\"NaN\" → Default");
            CheckVol("1e400", -1f, "1e400 → Default/kein Absturz", allowAny: true);
            CheckVol("null", -1f, "null → Default");
            CheckVol("\"0.5\"", -1f, "String → Default");
            CheckVol("0.35", 0.35f, "0.35 bleibt");

            var nanData = new SettingsData { Character = "../../x", MusicVolume = float.NaN, SfxVolume = float.PositiveInfinity };
            SettingsData nr = SaveSystem.ParseSettings(Encoding.UTF8.GetString(SaveSystem.SerializeSettings(nanData)));
            Check(nr.Character == CharacterCatalog.DefaultId && float.IsFinite(nr.MusicVolume) && float.IsFinite(nr.SfxVolume)
                  && nr.MusicVolume is >= 0f and <= 1f, "Settings: Schreiben klemmt NaN/ungültige Id");
        }
        finally
        {
            SaveSystem.QuietWarnings = false;
        }
    }

    private static string Short(string s) => s.Length > 20 ? s.Substring(0, 20) + "…" : s;

    private void CheckVol(string raw, float expected, string what, bool allowAny = false)
    {
        SettingsData s = SaveSystem.ParseSettings("{\"version\":1,\"music_volume\":" + raw + ",\"sfx_volume\":" + raw + "}");
        bool finite = float.IsFinite(s.MusicVolume) && float.IsFinite(s.SfxVolume)
                      && s.MusicVolume is >= 0f and <= 1f && s.SfxVolume is >= 0f and <= 1f;
        bool ok = finite && (allowAny || (expected < 0f
            ? Mathf.IsEqualApprox(s.MusicVolume, 0.7f) && Mathf.IsEqualApprox(s.SfxVolume, 0.8f)
            : Mathf.IsEqualApprox(s.MusicVolume, expected) && Mathf.IsEqualApprox(s.SfxVolume, expected)));
        Check(ok, $"Settings: Volume {what} (music={s.MusicVolume} sfx={s.SfxVolume})");
    }

    // ------------------------------------------------------------------ GameManager
    private void TestGameManager()
    {
        GameManager? gm = GameManager.Instance;
        if (gm == null) { Check(false, "GameManager-Autoload fehlt"); return; }
        Check(GameManager.CharacterSelectScenePath == "res://scenes/ui/CharacterSelect.tscn", "GameManager.CharacterSelectScenePath");
        Check(InputMap.HasAction("item_4"), "InputMap item_4");
        string oldChar = gm.SelectedCharacter;
        float oldM = gm.MusicVolume, oldS = gm.SfxVolume;
        int signals = 0;
        string last = "";
        void OnChanged(string id) { signals++; last = id; }
        gm.CharacterChanged += OnChanged;
        gm.SelectedCharacter = "bottle";
        gm.SelectedCharacter = "bottle";
        Check(gm.SelectedCharacter == "bottle" && signals == (oldChar == "bottle" ? 0 : 1), "GameManager: SelectedCharacter + CharacterChanged einmal");
        gm.SelectedCharacter = "../../x";
        Check(gm.SelectedCharacter == CharacterCatalog.DefaultId && last == CharacterCatalog.DefaultId, "GameManager: ungültig → Default");
        gm.CharacterChanged -= OnChanged;
        gm.MusicVolume = 7f; bool hi = Mathf.IsEqualApprox(gm.MusicVolume, 1f);
        gm.MusicVolume = -5f; bool lo = Mathf.IsEqualApprox(gm.MusicVolume, 0f);
        gm.MusicVolume = float.NaN; bool nan = float.IsFinite(gm.MusicVolume);
        gm.SfxVolume = 0.33f; bool sfx = Mathf.IsEqualApprox(gm.SfxVolume, 0.33f);
        gm.SfxVolume = float.NegativeInfinity; bool sfxInf = float.IsFinite(gm.SfxVolume);
        Check(hi && lo && nan && sfx && sfxInf, $"GameManager: Volume-Clamp hi={hi} lo={lo} nan={nan} sfx={sfx} inf={sfxInf}");
        gm.SelectedCharacter = oldChar;
        gm.MusicVolume = oldM;
        gm.SfxVolume = oldS;
        gm.SelectedCharacter = CharacterCatalog.DefaultId; // Vorschau/Screens starten deterministisch
    }

    // ------------------------------------------------------------------ Animation
    private void SetupFakePlayers()
    {
        for (int i = 0; i < CharacterCatalog.All.Count; i++)
        {
            var fake = new FakeAnimPlayer { Name = "Fake_" + i, Position = new Vector3(i * 3f, 0, 0) };
            var visual = new Node3D { Name = "Visual" };
            var rig = new CharacterRig { Name = "Rig", CharacterIdOverride = CharacterCatalog.All[i].Id };
            visual.AddChild(rig);
            fake.AddChild(visual);
            AddChild(fake);
            Check(Mathf.IsEqualApprox(fake.ModelTopHeight, rig.ModelHeight), $"{rig.CharacterId}: ModelTopHeight gesetzt ({fake.ModelTopHeight})");
            _fakes.Add(fake);
            _fakeRigs.Add(rig);
        }
    }

    private void SetupScreens()
    {
        var menuScene = ResourceLoader.Load<PackedScene>("res://scenes/ui/MainMenu.tscn");
        var menu = menuScene?.Instantiate<Control>();
        if (menu == null) { Check(false, "MainMenu lädt nicht"); }
        else
        {
            AddChild(menu);
            var btn = menu.GetNodeOrNull<Button>("%CharacterButton");
            Check(btn != null && btn.Text == "Charakter: Superheld", $"MainMenu: %CharacterButton Text ({btn?.Text})");
            foreach (string n in new[] { "%StartButton", "%QuitButton", "%HighscoreLabel", "%SeedEdit" })
                Check(menu.GetNodeOrNull(n) != null, $"MainMenu: {n} vorhanden");
            menu.QueueFree();
        }

        var selScene = ResourceLoader.Load<PackedScene>(GameManager.CharacterSelectScenePath);
        _select = selScene?.Instantiate<CharacterSelect>();
        if (_select == null) { Check(false, "CharacterSelect lädt nicht"); return; }
        AddChild(_select);
        foreach (string n in new[] { "%PrevButton", "%NextButton", "%ConfirmButton", "%BackButton", "%CharacterName", "%CharacterDescription", "%PreviewContainer" })
            Check(_select.GetNodeOrNull(n) != null, $"CharacterSelect: {n}");
        var pc = _select.GetNodeOrNull<SubViewportContainer>("%PreviewContainer");
        var vp = pc?.GetNodeOrNull<SubViewport>("SubViewport");
        Check(pc != null && pc.Stretch, "CharacterSelect: PreviewContainer.stretch");
        Check(vp != null && vp.OwnWorld3D, "CharacterSelect: SubViewport own_world_3d");
        Check(vp?.GetNodeOrNull<Camera3D>("Camera3D") != null && vp.GetNodeOrNull<DirectionalLight3D>("DirectionalLight3D") != null,
            "CharacterSelect: Camera3D + DirectionalLight3D");
        var rig = vp?.GetNodeOrNull<CharacterRig>("Turntable/Rig");
        Check(rig != null && rig.PreviewMode, "CharacterSelect: Turntable/Rig mit PreviewMode");
        Check(_select.GetNodeOrNull<Button>("%ConfirmButton")?.Text == "Auswählen" && _select.GetNodeOrNull<Button>("%BackButton")?.Text == "Zurück",
            "CharacterSelect: Button-Texte");
        Check(_select.CurrentId == CharacterCatalog.DefaultId, "CharacterSelect: startet beim gespeicherten Charakter");
        _selectStartId = _select.CurrentId;
        _turntableStartY = vp?.GetNodeOrNull<Node3D>("Turntable")?.Rotation.Y ?? 0f;
        _select.SelectNext(1);
        Check(_select.CurrentId == "knight" && rig?.CharacterId == "knight", $"CharacterSelect: Next → knight ({_select.CurrentId}/{rig?.CharacterId})");
        _select.SelectNext(1);
        Check(_select.CurrentId == "bottle", "CharacterSelect: Next zyklisch → bottle");
        _select.SelectNext(-1);
        _select.SelectNext(-1);
        Check(_select.CurrentId == "superhero" && _select.GetNodeOrNull<Label>("%CharacterName")?.Text == "Superheld",
            "CharacterSelect: Prev + Name-Label");
    }

    private void SetMode(PlayerAnimMode mode, float hs, float ratio, float vy)
    {
        foreach (FakeAnimPlayer f in _fakes)
        {
            f.AnimMode = mode;
            f.HorizontalSpeed = hs;
            f.MoveSpeedRatio = ratio;
            f.FakeVelocity = new Vector3(0, vy, -hs);
            f.FakeOnFloor = mode == PlayerAnimMode.Idle || mode == PlayerAnimMode.Run;
            f.GrappleTargetPosition = f.GlobalPosition + new Vector3(0, 6f, -6f);
        }
    }

    public override void _Process(double delta)
    {
        _frame++;
        try { Step(); }
        catch (Exception e) { Check(false, "Exception: " + e); Finish(); }
    }

    private void Step()
    {
        switch (_frame)
        {
            case 1: SetMode(PlayerAnimMode.Idle, 0, 0, 0); break;
            case 20: SetMode(PlayerAnimMode.Run, 9f, 0.82f, 0); break;
            case 70:
                Check(_maxLegRun > 0.3f, $"Run: Beine schwingen (max {_maxLegRun:F2} rad)");
                foreach (var f in _fakes) f.JumpCount++;
                SetMode(PlayerAnimMode.Jump, 4f, 0.4f, 7f);
                break;
            case 95:
                foreach (var r in _fakeRigs) Check(ArmDir(r).Y > 0.3f, $"{r.CharacterId}: Jump → Arme hoch ({ArmDir(r)})");
                SetMode(PlayerAnimMode.Fall, 4f, 0.4f, -9f);
                break;
            case 120:
                foreach (var r in _fakeRigs)
                    Check(ArmDir(r).X > 0.6f, $"{r.CharacterId}: Fall → Arme seitlich ({ArmDir(r)})");
                foreach (var f in _fakes) f.LandCount++;
                SetMode(PlayerAnimMode.Idle, 0, 0, 0);
                break;
            case 126:
                foreach (var r in _fakeRigs) Check(r.Scale.Y < 0.99f, $"{r.CharacterId}: Lande-Stauchung ({r.Scale.Y:F3})");
                break;
            case 140: SetMode(PlayerAnimMode.Fly, 6f, 0.6f, 2f); break;
            case 175:
                foreach (var r in _fakeRigs)
                {
                    Vector3 d = ArmDir(r);
                    bool ok = r.Style == CharacterStyle.Superhero ? d.Z < -0.6f && d.Y > 0.2f : d.Y > 0.8f;
                    Check(ok, $"{r.CharacterId}: Fly → Arm(e) hoch bzw. Superheld vor ({d})");
                }
                SetMode(PlayerAnimMode.Grapple, 8f, 0.7f, 3f);
                break;
            case 215:
                foreach (var r in _fakeRigs) CheckGrappleArm(r);
                foreach (var f in _fakes) f.AttackCount++;
                SetMode(PlayerAnimMode.Idle, 0, 0, 0);
                break;
            case 222:
                foreach (var r in _fakeRigs) Check(ArmDir(r).Y > -0.8f, $"{r.CharacterId}: Schlag hebt ArmR ({ArmDir(r)})");
                break;
            case 240:
                CheckFinitePoses();
                CheckNoAllocations();
                CheckTurntable();
                Finish();
                break;
        }
        if (_frame > 20 && _frame < 70)
            foreach (var r in _fakeRigs) _maxLegRun = Mathf.Max(_maxLegRun, Mathf.Abs(r.CurrentLegL.X));
    }

    /// <summary>Globale Zeigerichtung des rechten Arms (Arm hängt im Ruhezustand entlang −Y).</summary>
    private static Vector3 ArmDir(CharacterRig r)
    {
        var arm = r.GetNodeOrNull<Node3D>("Hips/Body/ArmR");
        return arm == null ? Vector3.Zero : -arm.GlobalBasis.Y.Normalized();
    }

    private void CheckGrappleArm(CharacterRig r)
    {
        var arm = r.GetNodeOrNull<Node3D>("Hips/Body/ArmR");
        var fake = r.GetParent()?.GetParent() as FakeAnimPlayer;
        if (arm == null || fake == null) { Check(false, $"{r.CharacterId}: Grapple-Arm fehlt"); return; }
        Vector3 armDir = -arm.GlobalBasis.Y.Normalized();
        Vector3 toTarget = (fake.GrappleTargetPosition - arm.GlobalPosition).Normalized();
        float dot = armDir.Dot(toTarget);
        Check(dot > 0.85f, $"{r.CharacterId}: Grapple → ArmR zeigt zum Ziel (dot={dot:F2})");
    }

    private void CheckFinitePoses()
    {
        foreach (var r in _fakeRigs)
        {
            bool ok = true;
            foreach (string p in new[] { "Hips", "Hips/Body", "Hips/Body/Head", "Hips/Body/ArmL", "Hips/Body/ArmR", "Hips/LegL", "Hips/LegR" })
            {
                var n = r.GetNodeOrNull<Node3D>(p);
                if (n == null || !n.Transform.Origin.IsFinite() || !n.Rotation.IsFinite()) ok = false;
            }
            Check(ok && r.Scale.IsFinite(), $"{r.CharacterId}: alle Pivots endlich");
        }
    }

    /// <summary>_Process des Rigs darf keine verwalteten Allokationen erzeugen (CONTRACT_V2 §0.8).</summary>
    private void CheckNoAllocations()
    {
        PlayerAnimMode[] modes = { PlayerAnimMode.Run, PlayerAnimMode.Jump, PlayerAnimMode.Fall, PlayerAnimMode.Fly, PlayerAnimMode.Grapple, PlayerAnimMode.Idle };
        foreach (var m in modes)
        {
            SetMode(m, 8f, 0.7f, m == PlayerAnimMode.Fall ? -6f : 3f);
            foreach (var r in _fakeRigs) for (int i = 0; i < 5; i++) r._Process(1.0 / 60.0); // warm-up
            long before = GC.GetAllocatedBytesForCurrentThread();
            foreach (var r in _fakeRigs)
                for (int i = 0; i < 60; i++)
                {
                    if (i == 10) ((FakeAnimPlayer)r.GetParent().GetParent()).AttackCount++;
                    if (i == 20) ((FakeAnimPlayer)r.GetParent().GetParent()).LandCount++;
                    r._Process(1.0 / 60.0);
                }
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Check(bytes < 512, $"Rig._Process {m}: keine Allokationen ({bytes} B / 240 Frames)");
        }
    }

    private void CheckTurntable()
    {
        if (_select == null) return;
        var tt = _select.GetNodeOrNull<Node3D>("%PreviewContainer/SubViewport/Turntable");
        Check(tt != null && Mathf.Abs(tt.Rotation.Y - _turntableStartY) > 0.5f, $"CharacterSelect: Turntable dreht ({tt?.Rotation.Y:F2})");
        Check(_selectStartId == CharacterCatalog.DefaultId, "CharacterSelect: Start-Id");
        var rig = tt?.GetNodeOrNull<CharacterRig>("Rig");
        Check(rig?.GetNodeOrNull("Hips/Body/Cape") != null, "CharacterSelect: Vorschau zeigt Superheld");
    }

    private void Finish()
    {
        if (_frame >= 100000) return;
        _frame = 100000;
        SetProcess(false);
        if (_fails.Count == 0)
        {
            GD.Print("CHARACTER_TEST_PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"CHARACTER_TEST_FAIL: {_fails.Count} Fehler – {_fails[0]}");
            GetTree().Quit(1);
        }
    }
}
