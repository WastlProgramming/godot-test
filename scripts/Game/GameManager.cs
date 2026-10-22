using System;
using Godot;
using JumpRun.Characters;

namespace JumpRun.Game;

/// <summary>Autoload: Run-Zustand, Score, Highscore, Seed, Einstellungen.</summary>
public partial class GameManager : Node
{
    public const string MainScenePath = "res://scenes/Main.tscn";
    public const string MainMenuScenePath = "res://scenes/ui/MainMenu.tscn";
    public const string CharacterSelectScenePath = "res://scenes/ui/CharacterSelect.tscn";

    [Signal] public delegate void ScoreChangedEventHandler(int score, int distance);
    [Signal] public delegate void HighscoreChangedEventHandler(int highscore);
    [Signal] public delegate void PlayerRespawnedEventHandler();
    [Signal] public delegate void CharacterChangedEventHandler(string id);
    /// <summary>R V2-04: Grafikqualität umgeschaltet (true = niedrig).</summary>
    [Signal] public delegate void GraphicsQualityChangedEventHandler(bool low);

    /// <summary>Autoload-Instanz; null außerhalb des Baums (z. B. reine Logiktests). R-16: einheitlich nullable.</summary>
    public static GameManager? Instance { get; private set; }
    /// <summary>Nicht-null-Zugriff für Stellen, an denen der Autoload garantiert existiert (Main-Szene, Tests).</summary>
    public static GameManager Current => Instance ?? throw new InvalidOperationException("GameManager-Autoload fehlt.");

    public int Seed { get; set; }
    public int Score => Math.Min(GameConstants.MaxScoreValue, Distance + CrateBonus);
    public int Distance { get; private set; }
    public int Highscore { get; private set; }
    public int BestDistance { get; private set; }
    public int CrateBonus { get; private set; }
    public int Falls { get; private set; }
    /// <summary>Summe aller Floating-Origin-Verschiebungen (float-Sicht; präzise: <see cref="OriginOffsetZPrecise"/>).</summary>
    public float OriginOffsetZ => (float)_originOffsetZ;
    /// <summary>R V2-11: als double geführt, damit Distanz und Backdrop auch nach &gt; 100 km exakt bleiben.</summary>
    public double OriginOffsetZPrecise => _originOffsetZ;
    private double _originOffsetZ;
    public bool RunActive { get; private set; }
    /// <summary>false = keine Dateizugriffe (Tests sollen den echten Highscore nicht überschreiben).</summary>
    public bool PersistenceEnabled { get; set; } = true;

    private float _mouseSensitivity = GameConstants.MouseSensitivityDefault;
    public float MouseSensitivity
    {
        get => _mouseSensitivity;
        set
        {
            float v = SaveSystem.ClampSensitivity(value);
            if (!Mathf.IsEqualApprox(v, _mouseSensitivity)) { _mouseSensitivity = v; _settingsDirty = true; }
        }
    }

    private bool _invertY;
    public bool InvertY
    {
        get => _invertY;
        set { if (value != _invertY) { _invertY = value; _settingsDirty = true; } }
    }

    private string _selectedCharacter = CharacterCatalog.DefaultId;
    // V2-08: Wert, der gespeichert wird. Ein Debug-Override per Kommandozeile ändert nur _selectedCharacter.
    private string _persistedCharacter = CharacterCatalog.DefaultId;
    /// <summary>Gewählter Charakter (CONTRACT_V2 §5.6); ungültige Ids → Default.</summary>
    public string SelectedCharacter
    {
        get => _selectedCharacter;
        set
        {
            string v = CharacterCatalog.IsValid(value) ? value : CharacterCatalog.DefaultId;
            if (v != _persistedCharacter) { _persistedCharacter = v; _settingsDirty = true; }
            if (v == _selectedCharacter) return;
            _selectedCharacter = v;
            EmitSignal(SignalName.CharacterChanged, v);
        }
    }

    private float _musicVolume = SaveSystem.MusicVolumeDefault;
    /// <summary>Musiklautstärke linear 0..1 (nur Daten; Anbindung an AudioManager durch den Integrator).</summary>
    public float MusicVolume
    {
        get => _musicVolume;
        set
        {
            float v = SaveSystem.ClampVolume(value, _musicVolume);
            if (!Mathf.IsEqualApprox(v, _musicVolume)) { _musicVolume = v; _settingsDirty = true; }
        }
    }

    private float _sfxVolume = SaveSystem.SfxVolumeDefault;
    /// <summary>Effektlautstärke linear 0..1.</summary>
    public float SfxVolume
    {
        get => _sfxVolume;
        set
        {
            float v = SaveSystem.ClampVolume(value, _sfxVolume);
            if (!Mathf.IsEqualApprox(v, _sfxVolume)) { _sfxVolume = v; _settingsDirty = true; }
        }
    }

    // R V2-04: null = automatisch (Compatibility/OpenGL → niedrig, Forward+ → hoch).
    private bool? _graphicsLowSaved;
    private bool? _graphicsLowOverride; // Kommandozeile --quality=, wird nicht gespeichert

    /// <summary>
    /// Grafik „niedrig“: SSAO aus, 2 statt 4 Schatten-Splits ohne Überblendung, Schattendistanz 60 m.
    /// Ohne gespeicherten Wert automatisch: niedrig im Compatibility-Renderer (OpenGL, run-linux.sh).
    /// </summary>
    public bool GraphicsLow
    {
        get => _graphicsLowOverride ?? _graphicsLowSaved ?? DefaultGraphicsLow();
        set
        {
            bool before = GraphicsLow;
            _graphicsLowOverride = null;
            if (_graphicsLowSaved != value) { _graphicsLowSaved = value; _settingsDirty = true; }
            if (before != value) EmitSignal(SignalName.GraphicsQualityChanged, value);
        }
    }

    private static bool DefaultGraphicsLow() =>
        RenderingServer.GetCurrentRenderingMethod() is "gl_compatibility" or "mobile";

    private bool _settingsDirty;
    private bool _highscoreDirty;
    private double _autosaveTimer;
    private int _lastEmittedScore = -1;
    private readonly Random _seedRng = new();

    public override void _EnterTree()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;
        EnsureInputMap();
    }

    public override void _Ready()
    {
        HighscoreData hs = SaveSystem.LoadHighscore();
        Highscore = hs.Highscore;
        BestDistance = hs.BestDistance;
        SettingsData s = SaveSystem.LoadSettings();
        _mouseSensitivity = s.MouseSensitivity;
        _invertY = s.InvertY;
        _selectedCharacter = CharacterCatalog.IsValid(s.Character) ? s.Character : CharacterCatalog.DefaultId;
        _persistedCharacter = _selectedCharacter;
        _musicVolume = s.MusicVolume;
        _sfxVolume = s.SfxVolume;
        _graphicsLowSaved = s.GraphicsLow;
        ApplyCommandLineOverrides();
    }

    /// <summary>
    /// Debug/Test: <c>-- --character=&lt;id&gt;</c> wählt einen Charakter für diesen Start, ohne die Settings zu ändern.
    /// Unbekannte Ids werden ignoriert.
    /// </summary>
    private void ApplyCommandLineOverrides()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "--quality=low") { _graphicsLowOverride = true; continue; }
            if (arg == "--quality=high") { _graphicsLowOverride = false; continue; }
            const string prefix = "--character=";
            if (!arg.StartsWith(prefix, StringComparison.Ordinal)) continue;
            string id = arg.Substring(prefix.Length);
            if (CharacterCatalog.IsValid(id)) _selectedCharacter = id;
            else GD.PushWarning($"[GameManager] Unbekannter Charakter '{id}' ignoriert.");
        }
    }

    public override void _Process(double delta)
    {
        if (!RunActive) return;
        _autosaveTimer += delta;
        if (_autosaveTimer >= 30.0)
        {
            _autosaveTimer = 0;
            if (_highscoreDirty || _settingsDirty) SaveAll(); // R-06: auch Einstellungen autospeichern
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest) SaveAll();
    }

    public override void _ExitTree()
    {
        // Statische Caches freigeben, bevor der RenderingServer herunterfährt (F-10 / R-15).
        World.MaterialLibrary.ClearCache();
        World.ChunkBuilder.ClearSceneCache();
        Items.TrampolineItem.ReleaseStatics();
        if (Instance == this) Instance = null;
    }

    public void StartNewRun(int? seed = null)
    {
        Seed = seed ?? _seedRng.Next(1, 999_999_999);
        Distance = 0;
        CrateBonus = 0;
        Falls = 0;
        _originOffsetZ = 0.0;
        RunActive = true;
        _lastEmittedScore = -1;
        _autosaveTimer = 0;
        EmitScore();
    }

    /// <summary>Rechnet Distanz = max(-(localZ) + OriginOffsetZ).</summary>
    public void ReportPlayerZ(float localZ)
    {
        if (!float.IsFinite(localZ)) return;
        double raw = Math.Clamp(Math.Floor(-(double)localZ + _originOffsetZ), -1e15, 1e15); // kein long-Überlauf
        int d = SaveSystem.ClampScore((long)raw);
        if (d > Distance)
        {
            Distance = d;
            EmitScore();
        }
    }

    public void AddOriginOffset(float dz)
    {
        if (!float.IsFinite(dz)) return;
        double next = _originOffsetZ + dz;
        if (double.IsFinite(next) && Math.Abs(next) < float.MaxValue) _originOffsetZ = next;
    }

    public void AddCrateBonus(int points)
    {
        if (points <= 0) return;
        CrateBonus = Math.Min(GameConstants.MaxScoreValue, CrateBonus + points);
        EmitScore();
    }

    public void RegisterFall()
    {
        Falls++;
        EmitSignal(SignalName.PlayerRespawned);
    }

    private void EmitScore()
    {
        int score = Score;
        if (score == _lastEmittedScore) return;
        _lastEmittedScore = score;
        EmitSignal(SignalName.ScoreChanged, score, Distance);
        if (score > Highscore)
        {
            Highscore = score;
            _highscoreDirty = true;
            EmitSignal(SignalName.HighscoreChanged, Highscore);
        }
        if (Distance > BestDistance)
        {
            BestDistance = Distance;
            _highscoreDirty = true;
        }
    }

    public void SaveAll()
    {
        if (!PersistenceEnabled) return;
        SaveSystem.SaveHighscore(new HighscoreData { Highscore = Highscore, BestDistance = BestDistance });
        SaveSystem.SaveSettings(new SettingsData
        {
            MouseSensitivity = _mouseSensitivity,
            InvertY = _invertY,
            Character = _persistedCharacter,
            MusicVolume = _musicVolume,
            SfxVolume = _sfxVolume,
            GraphicsLow = _graphicsLowSaved,
        });
        _highscoreDirty = false;
        _settingsDirty = false;
    }

    public void GoToMainMenu()
    {
        SaveAll();
        RunActive = false;
        GetTree().Paused = false;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        if (ResourceLoader.Exists(MainMenuScenePath))
            GetTree().ChangeSceneToFile(MainMenuScenePath);
        else
            GD.PushWarning("[GameManager] Hauptmenü-Szene fehlt.");
    }

    public void RestartRun()
    {
        SaveAll();
        GetTree().Paused = false;
        StartNewRun(Seed);
        GetTree().ChangeSceneToFile(MainScenePath);
    }

    public void QuitGame()
    {
        SaveAll();
        JumpRun.Audio.AudioManager.Instance?.StopAll();
        GetTree().Quit();
    }

    /// <summary>Sicherheitsnetz: fehlende InputMap-Aktionen (z. B. kaputtes project.godot) ergänzen.</summary>
    private static void EnsureInputMap()
    {
        AddKey("move_forward", Key.W);
        AddKey("move_back", Key.S);
        AddKey("move_left", Key.A);
        AddKey("move_right", Key.D);
        AddKey("jump", Key.Space);
        AddKey("sprint", Key.Shift);
        AddKey("attack", Key.F, MouseButton.Left);
        AddKey("item_1", Key.Key1);
        AddKey("item_2", Key.Key2);
        AddKey("item_3", Key.Key3);
        AddKey("item_4", Key.Key4);
        AddKey("item_next", Key.None, MouseButton.WheelDown);
        AddKey("item_prev", Key.None, MouseButton.WheelUp);
        AddKey("use_item", Key.E, MouseButton.Right);
        AddKey("pause", Key.Escape);
        AddKey("cam_left", Key.Left);
        AddKey("cam_right", Key.Right);
        AddKey("cam_up", Key.Up);
        AddKey("cam_down", Key.Down);
    }

    private static void AddKey(string action, Key key, MouseButton mouse = MouseButton.None)
    {
        if (InputMap.HasAction(action)) return;
        InputMap.AddAction(action);
        if (key != Key.None)
            InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
        if (mouse != MouseButton.None)
            InputMap.ActionAddEvent(action, new InputEventMouseButton { ButtonIndex = mouse });
    }
}
