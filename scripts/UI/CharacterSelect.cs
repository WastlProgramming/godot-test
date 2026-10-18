using Godot;
using JumpRun.Characters;
using JumpRun.Game;

namespace JumpRun.UI;

/// <summary>
/// Charakterauswahl mit drehender 3D-Vorschau (CONTRACT_V2 §5.6).
/// A/← und D/→ wechseln, Enter bestätigt (speichert), Esc geht ohne Speichern zurück.
/// </summary>
public partial class CharacterSelect : Control
{
    public const float TurntableSpeed = 0.6f; // rad/s
    private const float PopDuration = 0.25f;

    private Button? _prev, _next, _confirm, _back;
    private Label? _name, _description;
    private HBoxContainer? _bar;
    private Button[] _cards = System.Array.Empty<Button>();
    private Node3D? _turntable;
    private CharacterRig? _rig;
    private int _index;
    private float _popT = -1f;
    private bool _leaving;

    public int SelectedIndex => _index;
    public string CurrentId => CharacterCatalog.All[_index].Id;
    public CharacterRig? PreviewRig => _rig;

    public override void _Ready()
    {
        Input.MouseMode = Input.MouseModeEnum.Visible;
        GetTree().Paused = false;
        _prev = GetNodeOrNull<Button>("%PrevButton");
        _next = GetNodeOrNull<Button>("%NextButton");
        _confirm = GetNodeOrNull<Button>("%ConfirmButton");
        _back = GetNodeOrNull<Button>("%BackButton");
        _name = GetNodeOrNull<Label>("%CharacterName");
        _description = GetNodeOrNull<Label>("%CharacterDescription");
        _bar = GetNodeOrNull<HBoxContainer>("%CharacterBar");
        var container = GetNodeOrNull<SubViewportContainer>("%PreviewContainer");
        _turntable = container?.GetNodeOrNull<Node3D>("SubViewport/Turntable");
        _rig = _turntable?.GetNodeOrNull<CharacterRig>("Rig");

        int idx = CharacterCatalog.IndexOf(GameManager.Instance?.SelectedCharacter);
        _index = idx >= 0 ? idx : Mathf.Max(0, CharacterCatalog.IndexOf(CharacterCatalog.DefaultId));

        if (_prev != null) _prev.Pressed += OnPrev;
        if (_next != null) _next.Pressed += OnNext;
        if (_confirm != null) _confirm.Pressed += Confirm;
        if (_back != null) _back.Pressed += Back;
        BuildCards();
        SetupPreviewView(container);
        UpdateView(false);
        _confirm?.GrabFocus();
    }

    private void SetupPreviewView(SubViewportContainer? container)
    {
        var cam = container?.GetNodeOrNull<Camera3D>("SubViewport/Camera3D");
        if (cam != null && cam.IsInsideTree()) cam.LookAt(new Vector3(0, 0.92f, 0), Vector3.Up);
        var sun = container?.GetNodeOrNull<DirectionalLight3D>("SubViewport/DirectionalLight3D");
        if (sun != null) sun.Rotation = new Vector3(Mathf.DegToRad(-42f), Mathf.DegToRad(32f), 0);
    }

    private void BuildCards()
    {
        if (_bar == null) return;
        var all = CharacterCatalog.All;
        _cards = new Button[all.Count];
        for (int i = 0; i < all.Count; i++)
        {
            int captured = i;
            var b = new Button
            {
                Name = "Card_" + all[i].Id,
                Text = all[i].DisplayName,
                CustomMinimumSize = new Vector2(150, 50),
                ThemeTypeVariation = "CharacterCard",
                FocusMode = FocusModeEnum.All,
            };
            b.Pressed += () => SelectIndex(captured);
            _bar.AddChild(b);
            _cards[i] = b;
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (_turntable != null)
        {
            Vector3 r = _turntable.Rotation;
            r.Y = Mathf.PosMod(r.Y + TurntableSpeed * dt, Mathf.Tau);
            _turntable.Rotation = r;
            if (_popT >= 0f)
            {
                _popT += dt;
                float u = Mathf.Clamp(_popT / PopDuration, 0f, 1f);
                float s = 0.82f + 0.18f * (1f - (1f - u) * (1f - u)) + Mathf.Sin(u * Mathf.Pi) * 0.06f;
                _turntable.Scale = new Vector3(s, s, s);
                if (u >= 1f) { _popT = -1f; _turntable.Scale = Vector3.One; }
            }
        }
    }

    public override void _Input(InputEvent e)
    {
        if (_leaving || e is not InputEventKey k || !k.Pressed || k.Echo) return;
        Key key = k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode;
        switch (key)
        {
            case Key.A:
            case Key.Left:
                SelectNext(-1);
                break;
            case Key.D:
            case Key.Right:
                SelectNext(1);
                break;
            case Key.Escape:
                Back();
                break;
            case Key.Enter:
            case Key.KpEnter:
                // Ein fokussierter anderer Button (z. B. „Zurück“) behält sein normales Enter-Verhalten.
                Control? focus = GetViewport()?.GuiGetFocusOwner();
                if (focus is BaseButton && focus != _confirm && !IsCard(focus)) return;
                Confirm();
                break;
            default:
                return;
        }
        GetViewport()?.SetInputAsHandled();
    }

    private bool IsCard(Control c)
    {
        foreach (Button b in _cards) if (b == c) return true;
        return false;
    }

    private void OnPrev() => SelectNext(-1);
    private void OnNext() => SelectNext(1);

    public void SelectNext(int dir)
    {
        int n = CharacterCatalog.All.Count;
        SelectIndex(((_index + dir) % n + n) % n);
    }

    public void SelectIndex(int i)
    {
        if (i < 0 || i >= CharacterCatalog.All.Count || i == _index) return;
        _index = i;
        UpdateView(true);
    }

    private void UpdateView(bool animate)
    {
        CharacterDefinition def = CharacterCatalog.All[_index];
        if (_name != null) _name.Text = def.DisplayName;
        if (_description != null) _description.Text = def.Description;
        for (int i = 0; i < _cards.Length; i++)
            _cards[i].ThemeTypeVariation = i == _index ? "CharacterCardSelected" : "CharacterCard";
        if (_rig != null && _rig.CharacterId != def.Id) _rig.Build(def.Id);
        if (animate && _turntable != null)
        {
            _popT = 0f;
            _turntable.Scale = new Vector3(0.82f, 0.82f, 0.82f);
        }
    }

    /// <summary>Übernimmt die Auswahl, speichert und kehrt ins Hauptmenü zurück.</summary>
    public void Confirm()
    {
        if (_leaving) return;
        GameManager? gm = GameManager.Instance;
        if (gm != null)
        {
            gm.SelectedCharacter = CurrentId;
            gm.SaveAll();
        }
        GoToMenu();
    }

    /// <summary>Verwirft die Auswahl.</summary>
    public void Back()
    {
        if (_leaving) return;
        GoToMenu();
    }

    private void GoToMenu()
    {
        _leaving = true;
        if (ResourceLoader.Exists(GameManager.MainMenuScenePath))
            GetTree().ChangeSceneToFile(GameManager.MainMenuScenePath);
    }
}
