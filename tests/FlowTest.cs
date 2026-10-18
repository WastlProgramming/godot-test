using Godot;
using JumpRun.Game;
using JumpRun.UI;

namespace JumpRun.Tests;

/// <summary>Headless-Integrationstest: MainMenu -> Start -> Main (HUD/Pause/Environment) -> Pause -> Resume -> Hauptmenü.</summary>
public partial class FlowTest : Node
{
    public override void _Ready()
    {
        if (GameManager.Instance != null) GameManager.Current.PersistenceEnabled = false;
        var driver = new FlowDriver { Name = "FlowDriver", ProcessMode = ProcessModeEnum.Always };
        GetTree().Root.CallDeferred(Node.MethodName.AddChild, driver);
    }
}

public partial class FlowDriver : Node
{
    private int _step;
    private int _wait;

    public override void _Ready() => GetTree().ChangeSceneToFile("res://scenes/ui/MainMenu.tscn");

    private void Fail(string why) { GD.Print($"FLOW_TEST_FAIL: {why}"); GetTree().Paused = false; GetTree().Quit(1); _step = 99; }

    public override void _Process(double delta)
    {
        if (_step >= 99) return;
        if (++_wait < 10) return;
        _wait = 0;
        Node? scene = GetTree().CurrentScene;
        switch (_step)
        {
            case 0:
                if (scene is not MainMenu) { Fail("MainMenu nicht aktiv"); return; }
                var start = scene.GetNodeOrNull<Button>("%StartButton");
                var seed = scene.GetNodeOrNull<LineEdit>("%SeedEdit");
                if (start == null || seed == null) { Fail("StartButton/SeedEdit fehlt"); return; }
                seed.Text = "4242";
                start.EmitSignal(BaseButton.SignalName.Pressed);
                _step = 1; break;
            case 1:
                if (scene is not Main main) { Fail("Main nicht geladen nach Start"); return; }
                if (GameManager.Current.Seed != 4242) { Fail($"Seed nicht übernommen ({GameManager.Current.Seed})"); return; }
                var we = main.GetNodeOrNull<WorldEnvironment>("WorldEnvironment");
                if (we?.Environment == null || we.Environment.ResourcePath != "res://art/env/world_environment.tres") { Fail("Designer-Environment nicht aktiv"); return; }
                if (main.GetNodeOrNull("Lighting/Sun") == null) { Fail("Lighting/Sun fehlt"); return; }
                if (main.Hud == null || main.Pause == null) { Fail("HUD/PauseMenu nicht verdrahtet"); return; }
                if (main.GetNode("UI").GetChildCount() != 2) { Fail("UI hat doppelte Kinder"); return; }
                if (main.Hud.GetNodeOrNull("%GrappleIndicator") == null) { Fail("GrappleIndicator fehlt"); return; }
                var ev = new InputEventAction { Action = "pause", Pressed = true };
                Input.ParseInputEvent(ev);
                _step = 2; break;
            case 2:
                var m2 = (Main)scene!;
                if (!GetTree().Paused || !m2.Pause!.IsOpen) { Fail("Pause öffnet nicht"); return; }
                var resume = m2.Pause.GetNodeOrNull<Button>("%ResumeButton");
                if (resume == null) { Fail("ResumeButton fehlt"); return; }
                resume.EmitSignal(BaseButton.SignalName.Pressed);
                _step = 3; break;
            case 3:
                var m3 = (Main)scene!;
                if (GetTree().Paused || m3.Pause!.IsOpen) { Fail("Resume schließt Pause nicht"); return; }
                m3.Pause.Toggle();
                m3.Pause.GetNode<Button>("%MainMenuButton").EmitSignal(BaseButton.SignalName.Pressed);
                _step = 4; break;
            case 4:
                if (scene is not MainMenu) { Fail("Zurück zum Hauptmenü fehlgeschlagen"); return; }
                if (GetTree().Paused) { Fail("Baum nach Menüwechsel noch pausiert"); return; }
                GD.Print("FLOW_TEST_PASS");
                _step = 99;
                GetTree().Quit(0);
                break;
        }
    }
}
