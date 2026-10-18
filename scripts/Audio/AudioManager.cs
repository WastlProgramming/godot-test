using System.Collections.Generic;
using Godot;

namespace JumpRun.Audio;

/// <summary>
/// Global audio service (autoload <c>AudioManager</c>, CONTRACT_V2 §6.3).
/// Music with cross-fades on bus <c>Music</c>, pooled one-shot SFX (12 × 2D, 8 × 3D, voice stealing of the
/// oldest voice) and named non-positional loops on bus <c>SFX</c>. Works headless with the dummy audio driver.
/// </summary>
public partial class AudioManager : Node
{
    public const string AudioDir = "res://art/audio/";
    public const string MusicBus = "Music";
    public const string SfxBus = "SFX";
    public const int Pool2DSize = 12;
    public const int Pool3DSize = 8;

    /// <summary>Base level of the music relative to SFX (all files are normalised to -3 dBFS).</summary>
    public const float MusicBaseDb = -7f;
    private const float SilentDb = -80f;

    public static AudioManager? Instance { get; private set; }

    private readonly Dictionary<string, AudioStream?> _cache = new();
    private readonly HashSet<string> _warned = new();
    private readonly Dictionary<string, AudioStreamPlayer> _loops = new();

    private readonly AudioStreamPlayer[] _pool2D = new AudioStreamPlayer[Pool2DSize];
    private readonly ulong[] _pool2DStart = new ulong[Pool2DSize];
    private readonly AudioStreamPlayer3D[] _pool3D = new AudioStreamPlayer3D[Pool3DSize];
    private readonly ulong[] _pool3DStart = new ulong[Pool3DSize];
    private ulong _voiceCounter;

    // Music: two players for cross-fades. Gains are linear 0..1 and faded in _Process.
    private readonly AudioStreamPlayer[] _music = new AudioStreamPlayer[2];
    private readonly float[] _musicGain = new float[2];
    private readonly float[] _musicTarget = new float[2];
    private readonly float[] _musicRate = new float[2];
    private int _musicActive;
    private string? _musicName;

    private readonly RandomNumberGenerator _rng = new();
    private float _masterVolume = 1f;
    private float _musicVolume = 0.7f;
    private float _sfxVolume = 0.8f;
    private bool _built;
    private bool _treeHooked;

    /// <summary>
    /// false = PlaySfx/SetLoop are no-ops. Default: off in headless runs (tests/CI), because voices still playing at
    /// <c>Quit()</c> make Godot report leaked AudioStreamPlayback instances. Tests that check playback set it to true.
    /// </summary>
    public bool PlaybackEnabled { get; set; } = DisplayServer.GetName() != "headless";

    /// <summary>Number of distinct unknown sound names warned about (each only once).</summary>
    public int WarnedCount => _warned.Count;

    /// <summary>Number of 2D pool voices currently playing (tests/diagnostics).</summary>
    public int ActiveVoices2D
    {
        get
        {
            int n = 0;
            for (int i = 0; i < Pool2DSize; i++)
                if (_pool2D[i] != null && _pool2D[i].Playing) n++;
            return n;
        }
    }

    /// <summary>Name of the currently playing (or fading-in) music, null when stopped.</summary>
    public string? CurrentMusic => _musicName;

    /// <summary>Master volume, linear 0..1 (0 = mute). Sets the Master bus dB.</summary>
    public float MasterVolume
    {
        get => _masterVolume;
        set { if (!float.IsFinite(value)) return; _masterVolume = Mathf.Clamp(value, 0f, 1f); ApplyBusVolume("Master", _masterVolume); }
    }

    /// <summary>Music volume, linear 0..1 (0 = mute). Sets the Music bus dB.</summary>
    public float MusicVolume
    {
        get => _musicVolume;
        set { if (!float.IsFinite(value)) return; _musicVolume = Mathf.Clamp(value, 0f, 1f); ApplyBusVolume(MusicBus, _musicVolume); }
    }

    /// <summary>SFX volume, linear 0..1 (0 = mute). Sets the SFX bus dB.</summary>
    public float SfxVolume
    {
        get => _sfxVolume;
        set { if (!float.IsFinite(value)) return; _sfxVolume = Mathf.Clamp(value, 0f, 1f); ApplyBusVolume(SfxBus, _sfxVolume); }
    }

    /// <summary>Linear 0..1 → bus dB (0 → -80 dB and muted). Exposed for tests.</summary>
    public static float LinearToBusDb(float linear) => linear <= 0.0001f ? SilentDb : Mathf.LinearToDb(linear);

    public override void _EnterTree()
    {
        if (Instance == null || !IsInstanceValid(Instance))
            Instance = this;
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
        if (_treeHooked)
        {
            GetTree().NodeAdded -= OnNodeAdded;
            _treeHooked = false;
        }
        StopAll();
        // Release the managed references to the cached streams; an autoload's wrapper is otherwise only collected
        // after Godot's resource cleanup at exit ("resources still in use").
        foreach (AudioStream? st in _cache.Values) st?.Dispose();
        _cache.Clear();
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _rng.Seed = 0x4A524155; // deterministic jitter
        EnsureBuses();
        Build();
        // Saved settings (GameManager autoload is ready before AudioManager, CONTRACT_V2 §10.3).
        if (JumpRun.Game.GameManager.Instance is { } gm)
        {
            if (float.IsFinite(gm.MusicVolume)) _musicVolume = Mathf.Clamp(gm.MusicVolume, 0f, 1f);
            if (float.IsFinite(gm.SfxVolume)) _sfxVolume = Mathf.Clamp(gm.SfxVolume, 0f, 1f);
        }
        MasterVolume = _masterVolume;
        MusicVolume = _musicVolume;
        SfxVolume = _sfxVolume;

        if (!_treeHooked)
        {
            GetTree().NodeAdded += OnNodeAdded;
            _treeHooked = true;
        }
        HookExistingButtons(GetTree().Root);

        // Headless runs (tests, CI) skip the automatic music start: no audible output anyway, and a long stream
        // still playing at Quit() makes Godot report leaked playbacks. PlayMusic() works headless when called.
        if (DisplayServer.GetName() != "headless")
            PlayMusic(Sfx.MusicMain, 1f);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        for (int i = 0; i < 2; i++)
        {
            if (_musicGain[i] == _musicTarget[i]) continue;
            float step = _musicRate[i] * dt;
            if (_musicGain[i] < _musicTarget[i])
                _musicGain[i] = Mathf.Min(_musicTarget[i], _musicGain[i] + step);
            else
                _musicGain[i] = Mathf.Max(_musicTarget[i], _musicGain[i] - step);
            ApplyMusicGain(i);
            if (_musicGain[i] <= 0f && _musicTarget[i] <= 0f)
                _music[i].Stop();
        }
    }

    // ------------------------------------------------------------------------------------------
    // Music
    // ------------------------------------------------------------------------------------------

    public void PlayMusic(string name = "music_main", float fadeSeconds = 1f)
    {
        if (!_built) Build();
        if (_musicName == name && _music[_musicActive].Playing && _musicTarget[_musicActive] > 0f)
            return;
        AudioStream? stream = GetStream(name);
        if (stream == null) return;

        int next = 1 - _musicActive;
        // fade the old one out
        SetMusicFade(_musicActive, 0f, fadeSeconds);
        AudioStreamPlayer p = _music[next];
        p.Stop();
        p.Stream = stream;
        _musicGain[next] = fadeSeconds > 0f ? 0f : 1f;
        ApplyMusicGain(next);
        p.Play();
        SetMusicFade(next, 1f, fadeSeconds);
        _musicActive = next;
        _musicName = name;
    }

    public void StopMusic(float fadeSeconds = 0.5f)
    {
        if (!_built) return;
        SetMusicFade(0, 0f, fadeSeconds);
        SetMusicFade(1, 0f, fadeSeconds);
        _musicName = null;
    }

    private void SetMusicFade(int i, float target, float seconds)
    {
        _musicTarget[i] = target;
        if (seconds <= 0f)
        {
            _musicGain[i] = target;
            _musicRate[i] = 0f;
            ApplyMusicGain(i);
            if (target <= 0f) _music[i].Stop();
        }
        else
        {
            _musicRate[i] = 1f / seconds;
        }
    }

    private void ApplyMusicGain(int i)
    {
        float g = _musicGain[i];
        _music[i].VolumeDb = g <= 0.0001f ? SilentDb : MusicBaseDb + Mathf.LinearToDb(g);
    }

    // ------------------------------------------------------------------------------------------
    // SFX
    // ------------------------------------------------------------------------------------------

    public void PlaySfx(string name, Vector3? position = null, float volumeDb = 0f, float pitchJitter = 0.05f)
    {
        if (!PlaybackEnabled) return;
        if (!_built) Build();
        AudioStream? stream = GetStream(name);
        if (stream == null) return;
        float pitch = 1f;
        if (pitchJitter > 0f)
            pitch = Mathf.Max(0.05f, 1f + _rng.RandfRange(-pitchJitter, pitchJitter));
        float db = Sfx.TrimDb(name) + volumeDb;
        _voiceCounter++;

        if (position.HasValue)
        {
            int idx = PickVoice3D();
            AudioStreamPlayer3D p = _pool3D[idx];
            p.Stop();
            p.Stream = stream;
            p.GlobalPosition = position.Value;
            p.VolumeDb = db;
            p.PitchScale = pitch;
            p.Play();
            _pool3DStart[idx] = _voiceCounter;
        }
        else
        {
            int idx = PickVoice2D();
            AudioStreamPlayer p = _pool2D[idx];
            p.Stop();
            p.Stream = stream;
            p.VolumeDb = db;
            p.PitchScale = pitch;
            p.Play();
            _pool2DStart[idx] = _voiceCounter;
        }
    }

    /// <summary>Starts/stops a named non-positional loop (e.g. <c>helicopter_loop</c>). Idempotent.</summary>
    public void SetLoop(string name, bool on)
    {
        if (!_built) Build();
        _loops.TryGetValue(name, out AudioStreamPlayer? p);
        if (!on)
        {
            p?.Stop();
            return;
        }
        if (p != null && p.Playing) return;
        if (!PlaybackEnabled) return;
        if (p == null)
        {
            AudioStream? stream = GetStream(name, forceLoop: true);
            if (stream == null) return;
            // V2-07: Gameplay-Loops pausieren mit dem Spiel (der AudioManager selbst läuft mit ProcessMode.Always,
            // damit Musik und UI-Klicks im Pause-Menü weiterspielen).
            p = new AudioStreamPlayer { Name = "Loop_" + name, Bus = SfxBus, Stream = stream, ProcessMode = ProcessModeEnum.Pausable };
            AddChild(p);
            _loops[name] = p;
        }
        p.VolumeDb = Sfx.TrimDb(name);
        p.Play();
    }

    /// <summary>True when the named loop is currently active.</summary>
    public bool IsLoopActive(string name) =>
        _loops.TryGetValue(name, out AudioStreamPlayer? p) && p.Playing && !p.StreamPaused && p.CanProcess();

    /// <summary>Loads (cached) <c>res://art/audio/{name}.wav</c>; null + one-time warning when missing.</summary>
    public AudioStream? GetStream(string name, bool forceLoop = false)
    {
        if (_cache.TryGetValue(name, out AudioStream? cached))
        {
            if (cached != null && forceLoop) MakeLoop(cached);
            return cached;
        }
        string path = AudioDir + name + ".wav";
        AudioStream? stream = null;
        if (ResourceLoader.Exists(path))
            stream = ResourceLoader.Load<AudioStream>(path);
        if (stream == null)
        {
            if (_warned.Add(name))
                GD.PushWarning($"[AudioManager] Unbekannter Sound '{name}' ({path} fehlt)");
        }
        else if (forceLoop || Sfx.IsLoop(name))
        {
            MakeLoop(stream);
        }
        _cache[name] = stream;
        return stream;
    }

    private static void MakeLoop(AudioStream stream)
    {
        if (stream is AudioStreamWav wav)
        {
            if (wav.LoopMode == AudioStreamWav.LoopModeEnum.Forward && wav.LoopEnd > 0) return;
            wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            wav.LoopBegin = 0;
            wav.LoopEnd = (int)(wav.GetLength() * wav.MixRate);
        }
    }

    /// <summary>Stops every voice, music and loop and releases the streams (clean shutdown, no leaked playbacks).</summary>
    public void StopAll()
    {
        if (!_built) return;
        foreach (AudioStreamPlayer p in _pool2D) { p.Stop(); p.Stream = null; }
        foreach (AudioStreamPlayer3D p in _pool3D) { p.Stop(); p.Stream = null; }
        foreach (AudioStreamPlayer p in _music) { p.Stop(); p.Stream = null; }
        foreach (AudioStreamPlayer p in _loops.Values) { p.Stop(); p.Stream = null; p.QueueFree(); }
        _loops.Clear();
        _musicName = null;
        _musicGain[0] = _musicGain[1] = _musicTarget[0] = _musicTarget[1] = 0f;
    }

    private int PickVoice2D()
    {
        int oldest = 0;
        for (int i = 0; i < Pool2DSize; i++)
        {
            if (!_pool2D[i].Playing) return i;
            if (_pool2DStart[i] < _pool2DStart[oldest]) oldest = i;
        }
        return oldest;
    }

    private int PickVoice3D()
    {
        int oldest = 0;
        for (int i = 0; i < Pool3DSize; i++)
        {
            if (!_pool3D[i].Playing) return i;
            if (_pool3DStart[i] < _pool3DStart[oldest]) oldest = i;
        }
        return oldest;
    }

    // ------------------------------------------------------------------------------------------
    // Setup
    // ------------------------------------------------------------------------------------------

    private void Build()
    {
        if (_built) return;
        _built = true;
        for (int i = 0; i < 2; i++)
        {
            _music[i] = new AudioStreamPlayer { Name = "Music" + i, Bus = MusicBus, VolumeDb = SilentDb };
            AddChild(_music[i]);
        }
        for (int i = 0; i < Pool2DSize; i++)
        {
            _pool2D[i] = new AudioStreamPlayer { Name = "Sfx2D_" + i, Bus = SfxBus };
            AddChild(_pool2D[i]);
        }
        for (int i = 0; i < Pool3DSize; i++)
        {
            _pool3D[i] = new AudioStreamPlayer3D
            {
                Name = "Sfx3D_" + i,
                Bus = SfxBus,
                UnitSize = 8f,
                MaxDistance = 70f,
                AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance,
                DopplerTracking = AudioStreamPlayer3D.DopplerTrackingEnum.Disabled,
            };
            AddChild(_pool3D[i]);
        }
    }

    /// <summary>Fallback when default_bus_layout.tres is missing: create Music/SFX buses at runtime.</summary>
    private static void EnsureBuses()
    {
        foreach (string bus in new[] { MusicBus, SfxBus })
        {
            if (AudioServer.GetBusIndex(bus) >= 0) continue;
            int idx = AudioServer.BusCount;
            AudioServer.AddBus(idx);
            AudioServer.SetBusName(idx, bus);
            AudioServer.SetBusSend(idx, "Master");
        }
    }

    private static void ApplyBusVolume(string bus, float linear)
    {
        int idx = AudioServer.GetBusIndex(bus);
        if (idx < 0) return;
        AudioServer.SetBusVolumeDb(idx, LinearToBusDb(linear));
        AudioServer.SetBusMute(idx, linear <= 0.0001f);
    }

    // ------------------------------------------------------------------------------------------
    // Automatic UI sounds
    // ------------------------------------------------------------------------------------------

    private static readonly StringName HookedMeta = "_jr_audio_hooked";

    private void OnNodeAdded(Node node)
    {
        if (node is BaseButton b) HookButton(b);
    }

    private void HookExistingButtons(Node root)
    {
        if (root is BaseButton b) HookButton(b);
        foreach (Node child in root.GetChildren())
            HookExistingButtons(child);
    }

    private void HookButton(BaseButton b)
    {
        if (b.HasMeta(HookedMeta)) return;
        b.SetMeta(HookedMeta, true);
        b.Pressed += OnButtonPressed;
        b.MouseEntered += () =>
        {
            if (IsInstanceValid(this) && IsInsideTree() && IsInstanceValid(b) && !b.Disabled) PlaySfx(Sfx.UiHover, null, 0f, 0.02f);
        };
    }

    private void OnButtonPressed() => PlaySfx(Sfx.UiClick, null, 0f, 0.03f);
}
