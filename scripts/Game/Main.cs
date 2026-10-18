using Godot;
using JumpRun.Player;
using JumpRun.UI;
using JumpRun.World;

namespace JumpRun.Game;

/// <summary>Wurzel von Main.tscn: verdrahtet Welt, Spieler, HUD und Pause-Menü.</summary>
public partial class Main : Node3D
{
    private const string EnvironmentPath = "res://art/env/world_environment.tres";
    private const string LightingPath = "res://art/env/Lighting.tscn";
    private const string HudPath = "res://scenes/ui/HUD.tscn";
    private const string PausePath = "res://scenes/ui/PauseMenu.tscn";

    public WorldGenerator? Generator { get; private set; }
    public PlayerController? Player { get; private set; }
    public HUD? Hud { get; private set; }
    public PauseMenu? Pause { get; private set; }

    public override void _Ready()
    {
        SetupEnvironment();
        SetupLighting();
        GameManager.Current.GraphicsQualityChanged += ApplyGraphicsQuality;
        ApplyGraphicsQuality(GameManager.Current.GraphicsLow);

        Node? ui = GetNodeOrNull("UI");
        if (ui == null) { ui = new CanvasLayer { Name = "UI" }; AddChild(ui); }
        Hud = ui.GetNodeOrNull<HUD>("HUD") ?? InstanceUi<HUD>(ui, HudPath, "HUD");
        Pause = ui.GetNodeOrNull<PauseMenu>("PauseMenu") ?? InstanceUi<PauseMenu>(ui, PausePath, "PauseMenu");

        Player = GetNodeOrNull<PlayerController>("Player");
        Generator = GetNodeOrNull<WorldGenerator>("WorldRoot/WorldGenerator");
        GetNodeOrNull("WorldRoot/Placeables")?.AddToGroup(Items.Placeables.RootGroup);

        GameManager gm = GameManager.Current;
        if (!gm.RunActive) gm.StartNewRun();
        Generator?.Reset(gm.Seed);

        if (Player?.GetNodeOrNull<Characters.CharacterRig>("Visual/Rig") is { } rig)
            GD.Print($"[Main] Charakter: {rig.CharacterId}");

        GetTree().Paused = false;
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _ExitTree()
    {
        if (GameManager.Instance is { } gm) gm.GraphicsQualityChanged -= ApplyGraphicsQuality;
    }

    // Werte für „hoch“, beim ersten Anwenden gemerkt. Das Environment ist die geteilte (gecachte) .tres-Ressource
    // und bleibt über Szenenwechsel verändert – sein Originalwert wird deshalb statisch gemerkt.
    private static bool? _highSsaoStatic;
    private bool _qualityCaptured;
    private bool _highSsao;
    private DirectionalLight3D.ShadowMode _highShadowMode;
    private bool _highBlendSplits;
    private float _highShadowDistance;

    /// <summary>R V2-04: Grafikqualität anwenden (niedrig = SSAO aus, PSSM 2 Splits, 60 m Schattendistanz).</summary>
    public void ApplyGraphicsQuality(bool low)
    {
        Environment? env = GetNodeOrNull<WorldEnvironment>("WorldEnvironment")?.Environment;
        DirectionalLight3D? sun = GetNodeOrNull<DirectionalLight3D>("Lighting/Sun");
        if (!_qualityCaptured)
        {
            _qualityCaptured = true;
            _highSsaoStatic ??= env?.SsaoEnabled;
            _highSsao = _highSsaoStatic ?? false;
            _highShadowMode = sun?.DirectionalShadowMode ?? DirectionalLight3D.ShadowMode.Parallel4Splits;
            _highBlendSplits = sun?.DirectionalShadowBlendSplits ?? false;
            _highShadowDistance = sun?.DirectionalShadowMaxDistance ?? 120f;
        }
        if (env != null) env.SsaoEnabled = !low && _highSsao;
        if (sun != null)
        {
            sun.DirectionalShadowMode = low ? DirectionalLight3D.ShadowMode.Parallel2Splits : _highShadowMode;
            sun.DirectionalShadowBlendSplits = !low && _highBlendSplits;
            sun.DirectionalShadowMaxDistance = low ? Mathf.Min(60f, _highShadowDistance) : _highShadowDistance;
        }
    }

    public override void _Notification(int what)
    {
        // Nach Fokuswechsel (Alt-Tab, Klick in anderes Fenster) die Maus wieder einfangen.
        if (what == NotificationApplicationFocusIn && !GetTree().Paused)
            Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // Fallback ohne Pause-Menü: Esc gibt die Maus frei.
        if (Pause == null && @event.IsActionPressed("pause"))
            Input.MouseMode = Input.MouseModeEnum.Visible;
        // Klick ins Fenster fängt die Maus wieder ein (nur im laufenden Spiel).
        if (@event is InputEventMouseButton { Pressed: true } && !GetTree().Paused
            && Input.MouseMode != Input.MouseModeEnum.Captured)
            Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    private void SetupEnvironment()
    {
        WorldEnvironment? we = GetNodeOrNull<WorldEnvironment>("WorldEnvironment");
        if (we == null) { we = new WorldEnvironment { Name = "WorldEnvironment" }; AddChild(we); MoveChild(we, 0); }
        if (we.Environment != null) return;
        Environment? env = MaterialLibrary.TryLoad<Environment>(EnvironmentPath);
        if (env == null)
        {
            GD.PushWarning($"[Main] {EnvironmentPath} fehlt – Default-Environment.");
            env = new Environment
            {
                BackgroundMode = Environment.BGMode.Sky,
                Sky = new Sky { SkyMaterial = new ProceduralSkyMaterial() },
                AmbientLightSource = Environment.AmbientSource.Sky,
            };
        }
        we.Environment = env;
    }

    private void SetupLighting()
    {
        if (GetNodeOrNull("Lighting") != null) return;
        PackedScene? scene = MaterialLibrary.TryLoad<PackedScene>(LightingPath);
        Node3D? lighting = scene?.Instantiate() as Node3D;
        if (lighting == null)
        {
            GD.PushWarning($"[Main] {LightingPath} fehlt – Fallback-Licht.");
            lighting = new Node3D();
            var sun = new DirectionalLight3D
            {
                Name = "Sun",
                ShadowEnabled = true,
                RotationDegrees = new Vector3(-50f, 30f, 0f),
            };
            lighting.AddChild(sun);
        }
        lighting.Name = "Lighting";
        AddChild(lighting);
        MoveChild(lighting, Mathf.Min(1, GetChildCount() - 1));
    }

    private static T? InstanceUi<T>(Node parent, string path, string name) where T : Control
    {
        PackedScene? scene = MaterialLibrary.TryLoad<PackedScene>(path);
        if (scene == null)
        {
            GD.PushWarning($"[Main] UI-Szene fehlt: {path}");
            return null;
        }
        Node n = scene.Instantiate();
        if (n is not T t)
        {
            GD.PushWarning($"[Main] {path} hat nicht den erwarteten Skripttyp {typeof(T).Name}.");
            n.QueueFree();
            return null;
        }
        t.Name = name;
        parent.AddChild(t);
        return t;
    }
}
