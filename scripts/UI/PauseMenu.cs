using Godot;
using JumpRun.Audio;
using JumpRun.Game;

namespace JumpRun.UI;

/// <summary>Pause-Menü (Szene vom Designer: res://scenes/ui/PauseMenu.tscn), process_mode = Always.</summary>
public partial class PauseMenu : Control
{
    public bool IsOpen => Visible;

    private Button? _resume, _restart, _menu, _quit;
    private HSlider? _sensitivity, _music, _sfx;
    private CheckButton? _graphicsLow;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Visible = false;
        _resume = GetNodeOrNull<Button>("%ResumeButton");
        _restart = GetNodeOrNull<Button>("%RestartButton");
        _menu = GetNodeOrNull<Button>("%MainMenuButton");
        _quit = GetNodeOrNull<Button>("%QuitButton");
        _sensitivity = GetNodeOrNull<HSlider>("%SensitivitySlider");
        if (_resume != null) _resume.Pressed += OnResume;
        if (_restart != null) _restart.Pressed += OnRestart;
        if (_menu != null) _menu.Pressed += OnMainMenu;
        if (_quit != null) _quit.Pressed += OnQuit;
        if (_sensitivity != null)
        {
            _sensitivity.MinValue = GameConstants.MouseSensitivityMin;
            _sensitivity.MaxValue = GameConstants.MouseSensitivityMax;
            _sensitivity.Step = 0.05;
            _sensitivity.Value = GameManager.Instance?.MouseSensitivity ?? GameConstants.MouseSensitivityDefault;
            _sensitivity.ValueChanged += OnSensitivityChanged;
        }
        _music = SetupVolumeSlider("%MusicSlider", GameManager.Instance?.MusicVolume ?? SaveSystem.MusicVolumeDefault);
        _sfx = SetupVolumeSlider("%SfxSlider", GameManager.Instance?.SfxVolume ?? SaveSystem.SfxVolumeDefault);
        if (_music != null) _music.ValueChanged += OnMusicChanged;
        if (_sfx != null) _sfx.ValueChanged += OnSfxChanged;
        _graphicsLow = GetNodeOrNull<CheckButton>("%GraphicsLowToggle");
        if (_graphicsLow != null)
        {
            _graphicsLow.SetPressedNoSignal(GameManager.Instance?.GraphicsLow ?? false);
            _graphicsLow.Toggled += OnGraphicsLowToggled;
        }
    }

    private void OnGraphicsLowToggled(bool on)
    {
        if (GameManager.Instance != null) GameManager.Instance.GraphicsLow = on;
    }

    private HSlider? SetupVolumeSlider(string path, float value)
    {
        HSlider? s = GetNodeOrNull<HSlider>(path);
        if (s == null) return null;
        s.MinValue = 0.0;
        s.MaxValue = 1.0;
        s.Step = 0.05;
        s.SetValueNoSignal(value);
        return s;
    }

    public override void _ExitTree()
    {
        // R-07: Kind-Signale bleiben verbunden (Trennung automatisch beim Freigeben).
        if (IsOpen && IsInsideTree()) GetTree().Paused = false;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("pause"))
        {
            Toggle();
            GetViewport().SetInputAsHandled();
        }
    }

    public void Toggle()
    {
        bool open = !IsOpen;
        Visible = open;
        GetTree().Paused = open;
        Input.MouseMode = open ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
        if (open)
        {
            if (_sensitivity != null && GameManager.Instance != null)
                _sensitivity.SetValueNoSignal(GameManager.Instance.MouseSensitivity);
            if (GameManager.Instance != null)
            {
                _music?.SetValueNoSignal(GameManager.Instance.MusicVolume);
                _sfx?.SetValueNoSignal(GameManager.Instance.SfxVolume);
                _graphicsLow?.SetPressedNoSignal(GameManager.Instance.GraphicsLow);
            }
            _resume?.GrabFocus();
        }
    }

    private void OnResume() { if (IsOpen) Toggle(); }

    private void OnRestart() => GameManager.Instance?.RestartRun();

    private void OnMainMenu() => GameManager.Instance?.GoToMainMenu();

    private void OnQuit() => GameManager.Instance?.QuitGame();

    private void OnSensitivityChanged(double value)
    {
        if (GameManager.Instance != null) GameManager.Instance.MouseSensitivity = (float)value;
    }

    private void OnMusicChanged(double value)
    {
        if (GameManager.Instance != null) GameManager.Instance.MusicVolume = (float)value;
        if (AudioManager.Instance != null) AudioManager.Instance.MusicVolume = (float)value;
    }

    private void OnSfxChanged(double value)
    {
        if (GameManager.Instance != null) GameManager.Instance.SfxVolume = (float)value;
        if (AudioManager.Instance != null) AudioManager.Instance.SfxVolume = (float)value;
    }
}
