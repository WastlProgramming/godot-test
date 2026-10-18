using Godot;
using JumpRun.Characters;
using JumpRun.Game;

namespace JumpRun.UI;

/// <summary>Hauptmenü (Szene vom Designer: res://scenes/ui/MainMenu.tscn).</summary>
public partial class MainMenu : Control
{
    public const int MaxSeedLength = 9;

    public const string HelicopterIconPath = "res://art/ui/icons/helicopter.svg";

    private Button? _start, _quit, _character;
    private Label? _highscore;
    private LineEdit? _seed;

    public override void _Ready()
    {
        Input.MouseMode = Input.MouseModeEnum.Visible;
        GetTree().Paused = false;
        _start = GetNodeOrNull<Button>("%StartButton");
        _quit = GetNodeOrNull<Button>("%QuitButton");
        _highscore = GetNodeOrNull<Label>("%HighscoreLabel");
        _seed = GetNodeOrNull<LineEdit>("%SeedEdit");
        if (_start != null) { _start.Pressed += OnStart; _start.GrabFocus(); }
        if (_quit != null) _quit.Pressed += OnQuit;
        _character = GetNodeOrNull<Button>("%CharacterButton");
        if (_character != null)
        {
            _character.Pressed += OnCharacter;
            _character.Text = CharacterButtonText(GameManager.Instance?.SelectedCharacter);
        }
        AddHelicopterIcon();
        if (_seed != null)
        {
            _seed.MaxLength = MaxSeedLength;
            _seed.TextChanged += OnSeedChanged;
            _seed.TextSubmitted += OnSeedSubmitted;
        }
        if (_highscore != null && GameManager.Instance != null)
            _highscore.Text = $"Rekord: {GameManager.Instance.Highscore}";
    }

    // R-07: Signale der eigenen Kind-Buttons werden einmal in _Ready verbunden und beim Freigeben
    // automatisch getrennt; kein Trennen in _ExitTree, damit ein erneutes Einhängen funktioniert.

    /// <summary>Nur ASCII-Ziffern, 1–9 Zeichen; sonst null (= zufälliger Seed).</summary>
    public static int? ParseSeed(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > MaxSeedLength) return null;
        int value = 0;
        foreach (char ch in text)
        {
            if (ch < '0' || ch > '9') return null;
            value = value * 10 + (ch - '0'); // max. 999 999 999 < int.MaxValue
        }
        return value;
    }

    private void OnSeedChanged(string text)
    {
        if (_seed == null) return;
        var sb = new System.Text.StringBuilder(MaxSeedLength);
        foreach (char ch in text)
            if (ch >= '0' && ch <= '9' && sb.Length < MaxSeedLength) sb.Append(ch);
        string clean = sb.ToString();
        if (clean != text)
        {
            int caret = Mathf.Min(_seed.CaretColumn, clean.Length);
            _seed.Text = clean;
            _seed.CaretColumn = caret;
        }
    }

    private void OnSeedSubmitted(string _) => OnStart();

    private void OnStart()
    {
        GameManager? gm = GameManager.Instance;
        if (gm == null) return;
        gm.StartNewRun(ParseSeed(_seed?.Text));
        GetTree().ChangeSceneToFile(GameManager.MainScenePath);
    }

    private void OnQuit() => GameManager.Instance?.QuitGame();

    /// <summary>„Charakter: &lt;Name&gt;“ bzw. „Charakter wählen“ ohne GameManager.</summary>
    public static string CharacterButtonText(string? id) =>
        id == null ? "Charakter wählen" : $"Charakter: {CharacterCatalog.Get(id).DisplayName}";

    private void OnCharacter()
    {
        if (ResourceLoader.Exists(GameManager.CharacterSelectScenePath))
            GetTree().ChangeSceneToFile(GameManager.CharacterSelectScenePath);
        else
            GD.PushWarning("[MainMenu] Charakterauswahl-Szene fehlt.");
    }

    /// <summary>Helikopter-Icon (v2, von GAMEPLAY) zur Item-Leiste hinzufügen, sofern vorhanden.</summary>
    private void AddHelicopterIcon()
    {
        var items = GetNodeOrNull<HBoxContainer>("Center/VBox/Items");
        if (items == null || items.HasNode("Helicopter") || !ResourceLoader.Exists(HelicopterIconPath)) return;
        var tex = ResourceLoader.Load<Texture2D>(HelicopterIconPath);
        if (tex == null) return;
        items.AddChild(new TextureRect
        {
            Name = "Helicopter",
            Texture = tex,
            CustomMinimumSize = new Vector2(48, 48),
            MouseFilter = MouseFilterEnum.Ignore,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        });
    }
}
