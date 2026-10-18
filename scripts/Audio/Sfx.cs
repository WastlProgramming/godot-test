namespace JumpRun.Audio;

/// <summary>Sound names (file stem under res://art/audio/) – CONTRACT_V2 §6.1/§6.3.</summary>
public static class Sfx
{
    public const string MusicMain = "music_main";

    public const string Jump = "jump";
    public const string Land = "land";
    public const string Swing = "swing";
    public const string CrateBreak = "crate_break";
    public const string Pickup = "pickup";
    public const string BridgePlace = "bridge_place";
    public const string TrampolinePlace = "trampoline_place";
    public const string TrampolineBounce = "trampoline_bounce";
    public const string GrappleShoot = "grapple_shoot";
    public const string GrappleAttach = "grapple_attach";
    public const string HelicopterStart = "helicopter_start";
    public const string HelicopterLoop = "helicopter_loop";
    public const string Respawn = "respawn";
    public const string Checkpoint = "checkpoint";
    public const string ItemFail = "item_fail";
    public const string UiClick = "ui_click";
    public const string UiHover = "ui_hover";

    /// <summary>All one-shot / loop SFX names (without music).</summary>
    public static readonly string[] All =
    {
        Jump, Land, Swing, CrateBreak, Pickup, BridgePlace, TrampolinePlace, TrampolineBounce,
        GrappleShoot, GrappleAttach, HelicopterStart, HelicopterLoop, Respawn, Checkpoint, ItemFail,
        UiClick, UiHover,
    };

    /// <summary>Streams that must loop (LoopMode is set at runtime, independent of import settings).</summary>
    public static bool IsLoop(string name) => name == HelicopterLoop || name.StartsWith("music_");

    /// <summary>
    /// Per-sound mix trim in dB (all files are normalised to -3 dBFS peak by tools/gen_audio.py,
    /// so the balance between sounds is done here).
    /// </summary>
    public static float TrimDb(string name) => name switch
    {
        Jump => -6f,
        Land => -5f,
        Swing => -4f,
        CrateBreak => -2f,
        Pickup => -4f,
        BridgePlace => -3f,
        TrampolinePlace => -3f,
        TrampolineBounce => -4f,
        GrappleShoot => -4f,
        GrappleAttach => -5f,
        HelicopterStart => -4f,
        HelicopterLoop => -9f,
        Respawn => -4f,
        Checkpoint => -5f,
        ItemFail => -7f,
        UiClick => -8f,
        UiHover => -16f,
        _ => 0f,
    };
}
