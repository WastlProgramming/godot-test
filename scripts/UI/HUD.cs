using Godot;
using JumpRun.Game;
using JumpRun.Items;
using JumpRun.Player;

namespace JumpRun.UI;

/// <summary>HUD-Logik (Szene vom Designer: res://scenes/ui/HUD.tscn).</summary>
public partial class HUD : Control
{
    private static readonly string[] IconPaths =
    {
        "res://art/ui/icons/bridge.svg",
        "res://art/ui/icons/trampoline.svg",
        "res://art/ui/icons/grapple.svg",
        "res://art/ui/icons/helicopter.svg",
    };
    private const int Slots = GameConstants.ItemSlotCount;

    private Label? _distance, _score, _highscore, _hint;
    private readonly PanelContainer?[] _slots = new PanelContainer?[Slots];
    private readonly Label?[] _counts = new Label?[Slots];
    private readonly int[] _shownCounts = { -1, -1, -1, -1 };
    private Control? _fuelPanel;
    private ProgressBar? _fuelBar;
    private ColorRect? _flash;
    private Control? _grappleIndicator;
    private Tween? _flashTween;
    private float _hintTimer;
    private PlayerController? _player;
    private GameManager? _gm;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        _distance = GetNodeOrNull<Label>("%DistanceLabel");
        _score = GetNodeOrNull<Label>("%ScoreLabel");
        _highscore = GetNodeOrNull<Label>("%HighscoreLabel");
        _hint = GetNodeOrNull<Label>("%HintLabel");
        _flash = GetNodeOrNull<ColorRect>("%RespawnFlash");
        _grappleIndicator = GetNodeOrNull<Control>("%GrappleIndicator");
        if (_grappleIndicator != null) { _grappleIndicator.Visible = false; _grappleIndicator.MouseFilter = MouseFilterEnum.Ignore; }
        _fuelPanel = GetNodeOrNull<Control>("%FuelPanel");
        _fuelBar = GetNodeOrNull<ProgressBar>("%FuelBar");
        if (_fuelPanel != null) { _fuelPanel.Visible = false; _fuelPanel.MouseFilter = MouseFilterEnum.Ignore; }
        if (_fuelBar != null) { _fuelBar.MinValue = 0; _fuelBar.MaxValue = 1; _fuelBar.Step = 0; _fuelBar.MouseFilter = MouseFilterEnum.Ignore; }
        for (int i = 0; i < Slots; i++)
        {
            _slots[i] = GetNodeOrNull<PanelContainer>($"%ItemSlot{i + 1}");
            _counts[i] = GetNodeOrNull<Label>($"%ItemSlot{i + 1}Count");
            TextureRect? icon = GetNodeOrNull<TextureRect>($"%ItemSlot{i + 1}Icon");
            if (icon != null && icon.Texture == null && ResourceLoader.Exists(IconPaths[i]))
                icon.Texture = ResourceLoader.Load<Texture2D>(IconPaths[i]);
        }
        if (_hint != null) _hint.Visible = false;
        if (_flash != null)
        {
            _flash.MouseFilter = MouseFilterEnum.Ignore;
            Color m = _flash.Modulate; m.A = 0f; _flash.Modulate = m;
        }
        CallDeferred(MethodName.ConnectSources);
    }

    private void ConnectSources()
    {
        _gm = GameManager.Instance;
        if (_gm != null)
        {
            _gm.ScoreChanged += OnScoreChanged;
            _gm.HighscoreChanged += OnHighscoreChanged;
            OnScoreChanged(_gm.Score, _gm.Distance);
            OnHighscoreChanged(_gm.Highscore);
        }
        _player = GetTree().GetFirstNodeInGroup("player") as PlayerController;
        if (_player != null)
        {
            _player.ItemUseFailed += OnItemUseFailed;
            _player.Respawned += OnRespawned;
            _player.Inventory.InventoryChanged += OnInventoryChanged;
            _player.Inventory.SelectionChanged += OnSelectionChanged;
            Inventory inv = _player.Inventory;
            OnInventoryChanged(inv.GetCount(ItemType.Bridge), inv.GetCount(ItemType.Trampoline),
                inv.GetCount(ItemType.GrappleHook), inv.GetCount(ItemType.Helicopter));
            OnSelectionChanged((int)inv.Selected);
        }
    }

    public override void _ExitTree()
    {
        if (_gm != null && IsInstanceValid(_gm))
        {
            _gm.ScoreChanged -= OnScoreChanged;
            _gm.HighscoreChanged -= OnHighscoreChanged;
        }
        if (_player != null && IsInstanceValid(_player))
        {
            _player.ItemUseFailed -= OnItemUseFailed;
            _player.Respawned -= OnRespawned;
            if (IsInstanceValid(_player.Inventory))
            {
                _player.Inventory.InventoryChanged -= OnInventoryChanged;
                _player.Inventory.SelectionChanged -= OnSelectionChanged;
            }
        }
    }

    public override void _Process(double delta)
    {
        if (_grappleIndicator != null)
        {
            bool on = _player != null && IsInstanceValid(_player) && _player.HasGrappleTarget;
            if (_grappleIndicator.Visible != on) _grappleIndicator.Visible = on;
        }
        if (_fuelPanel != null)
        {
            bool flying = _player != null && IsInstanceValid(_player) && _player.IsFlying;
            if (_fuelPanel.Visible != flying) _fuelPanel.Visible = flying;
            if (flying && _fuelBar != null) _fuelBar.Value = _player!.FlightFuelRatio;
        }
        if (_hintTimer > 0f)
        {
            _hintTimer -= (float)delta;
            if (_hintTimer <= 0f && _hint != null) _hint.Visible = false;
        }
    }

    public void ShowHint(string text, float seconds = 2f)
    {
        if (_hint == null) return;
        _hint.Text = text;
        _hint.Visible = true;
        _hintTimer = Mathf.Max(0.1f, seconds);
    }

    public void FlashRespawn()
    {
        if (_flash == null) return;
        _flashTween?.Kill();
        Color m = _flash.Modulate; m.A = 0.85f; _flash.Modulate = m;
        _flashTween = CreateTween();
        _flashTween.TweenProperty(_flash, "modulate:a", 0f, 0.6f);
    }

    private void OnScoreChanged(int score, int distance)
    {
        if (_distance != null) _distance.Text = $"{distance} m";
        if (_score != null) _score.Text = $"Punkte: {score}";
    }

    private void OnHighscoreChanged(int highscore)
    {
        if (_highscore != null) _highscore.Text = $"Rekord: {highscore}";
    }

    private void OnInventoryChanged(int bridge, int trampoline, int grapple, int helicopter)
    {
        SetCount(0, bridge);
        SetCount(1, trampoline);
        SetCount(2, grapple);
        SetCount(3, helicopter);
    }

    /// <summary>Zähler ohne Obergrenze („x12“, „x1234“); Text nur bei Änderung neu setzen.</summary>
    private void SetCount(int i, int n)
    {
        if (_counts[i] == null || _shownCounts[i] == n) return;
        _shownCounts[i] = n;
        _counts[i]!.Text = FormatCount(n);
    }

    public static string FormatCount(int n) => "x" + Mathf.Max(0, n).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private void OnSelectionChanged(int itemType)
    {
        for (int i = 0; i < Slots; i++)
            if (_slots[i] != null)
                _slots[i]!.ThemeTypeVariation = (i + 1 == itemType) ? "ItemSlotSelected" : "ItemSlot";
    }

    private void OnItemUseFailed(string reason) => ShowHint(reason);

    private void OnRespawned() => FlashRespawn();
}
