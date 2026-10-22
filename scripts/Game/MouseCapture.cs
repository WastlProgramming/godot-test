using Godot;

namespace JumpRun.Game;

/// <summary>
/// Zentraler Maus-Modus fürs Spiel. Normal: <see cref="Input.MouseModeEnum.Captured"/> mit relativen Bewegungen.
/// Unter WSLg (RDP liefert nur absolute Mauspositionen, Warp/Pointer-Lock greift nicht) wird stattdessen der
/// "absolute" Modus verwendet: Zeiger versteckt, die Kamera rechnet Deltas aus der Position selbst und dreht am
/// Fensterrand weiter. Erzwingen per <c>-- --mouse=absolute</c> bzw. <c>-- --mouse=captured</c>.
/// </summary>
public static class MouseCapture
{
    private static bool? _absolute;

    public static bool AbsoluteMode => _absolute ??= DetectAbsoluteMode();

    public static bool IsCaptured =>
        Input.MouseMode == (AbsoluteMode ? Input.MouseModeEnum.ConfinedHidden : Input.MouseModeEnum.Captured);

    public static void Capture()
    {
        Input.MouseMode = AbsoluteMode ? Input.MouseModeEnum.ConfinedHidden : Input.MouseModeEnum.Captured;
    }

    public static void Release()
    {
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    private static bool DetectAbsoluteMode()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "--mouse=absolute") return true;
            if (arg == "--mouse=captured") return false;
        }
        if (OS.GetName() != "Linux") return false;
        if (!string.IsNullOrEmpty(OS.GetEnvironment("WSL_DISTRO_NAME"))) return true;
        try
        {
            return System.IO.File.Exists("/proc/version")
                && System.IO.File.ReadAllText("/proc/version").Contains("microsoft", System.StringComparison.OrdinalIgnoreCase);
        }
        catch (System.Exception)
        {
            return false;
        }
    }
}
