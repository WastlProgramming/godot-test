using Godot;

namespace JumpRun.Player;

// ============================================================================================
// FROZEN – CONTRACT_V2 §5. Owner: PM. Do not edit in v2 builder roles (changes only via PM).
// GAMEPLAY implements this on PlayerController; CHARACTERS (CharacterRig) only reads it.
// ============================================================================================

/// <summary>Coarse animation mode derived by PlayerController each physics frame.</summary>
public enum PlayerAnimMode
{
    Idle = 0,     // on floor, horizontal speed <= 0.5 m/s
    Run = 1,      // on floor, horizontal speed > 0.5 m/s
    Jump = 2,     // airborne, Velocity.Y > 0
    Fall = 3,     // airborne, Velocity.Y <= 0
    Grapple = 4,  // IsGrappling (has priority over everything)
    Fly = 5,      // IsFlying (helicopter), priority after Grapple
}

/// <summary>
/// Read-only player state for procedural character animation (CharacterRig).
/// Implemented by PlayerController (GAMEPLAY). The rig finds it via
/// <c>GetParent()?.GetParent() as IPlayerAnimState</c> (Player/Visual/Rig) and must
/// work (idle pose) when it is null, e.g. in the character-select preview.
/// </summary>
public interface IPlayerAnimState
{
    /// <summary>Current velocity in m/s (global). Provided by CharacterBody3D.Velocity.</summary>
    Vector3 Velocity { get; }

    /// <summary>Provided by CharacterBody3D.IsOnFloor().</summary>
    bool IsOnFloor();

    bool IsGrappling { get; }

    /// <summary>True while the helicopter flight is active.</summary>
    bool IsFlying { get; }

    /// <summary>Horizontal speed / GameConstants.SprintSpeed, clamped 0..1.</summary>
    float MoveSpeedRatio { get; }

    /// <summary>Horizontal speed in m/s (length of Velocity with Y = 0).</summary>
    float HorizontalSpeed { get; }

    /// <summary>Normalized horizontal facing direction (model forward = -Z of Player/Visual).</summary>
    Vector3 Facing { get; }

    /// <summary>Global position of the grapple anchor; only meaningful while IsGrappling.</summary>
    Vector3 GrappleTargetPosition { get; }

    /// <summary>Remaining helicopter fuel 1..0 while flying, 0 otherwise.</summary>
    float FlightFuelRatio { get; }

    /// <summary>Incremented on every attack swing (also when no crate is hit).</summary>
    int AttackCount { get; }

    /// <summary>Incremented on every jump (same moment as signal Jumped).</summary>
    int JumpCount { get; }

    /// <summary>Incremented on every landing (same moment as signal Landed).</summary>
    int LandCount { get; }

    PlayerAnimMode AnimMode { get; }

    /// <summary>
    /// Height of the model's top above the feet in metres (default 2.0).
    /// Set once by CharacterRig after building the model; PlayerController uses it to mount the
    /// helicopter rotor (Player/HelicopterRotor at local Y = ModelTopHeight).
    /// </summary>
    float ModelTopHeight { get; set; }
}
