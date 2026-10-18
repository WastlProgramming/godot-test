namespace JumpRun.Game;

/// <summary>Verbindliche Spielkonstanten (CONTRACT Abschnitt 3).</summary>
public static class GameConstants
{
    /// <summary>Nominale Chunk-Länge – einzige Quelle für die Schwierigkeit (DifficultyForIndex), R-14.</summary>
    public const float ChunkLength = 40f;
    public const int ChunksAhead = 6;
    public const int MaxAliveChunks = 12;
    public const float DespawnDistance = 30f;
    public const float MaxLateralOffset = 12f;
    public const float MinWorldY = -10f;
    public const float MaxWorldY = 40f;
    public const float OriginShiftThreshold = 1000f;

    public const float WalkSpeed = 7f;
    public const float SprintSpeed = 11f;
    public const float GroundAccel = 60f;
    public const float AirAccel = 24f;
    public const float Gravity = 24f;
    public const float JumpVelocity = 10.5f;
    public const float CoyoteTime = 0.12f;
    public const float JumpBufferTime = 0.12f;

    public const float MaxSafeGap = 6.0f;
    public const float MaxSafeStep = 2.0f;
    public const float FallDeathDepth = 20f;

    public const float AttackRange = 2.2f;
    public const float AttackCooldown = 0.35f;
    public const int CrateBonusScore = 25;

    /// <summary>Reiner Überlaufschutz (Sättigung) – kein Gameplay-Limit (CONTRACT_V2 §2).</summary>
    public const int InventoryHardCap = 999_999;
    public const int ItemSlotCount = 4;
    public const int MaxPlaceables = 3;
    public const float BridgeLength = 16f;
    /// <summary>Maximale BigGap-Lücke: Brückenlänge minus 2,5 m Reserve (Abstand zur Kante, Zielwinkel).</summary>
    public const float BridgeSnapDeg = 20f;
    public const float BigGapMax = BridgeLength - 2.5f;
    public const float BridgeWidth = 2f;
    public const float BridgeLifetime = 20f;
    public const float TrampolineBounceVelocity = 22f;
    public const float TrampolineLifetime = 30f;
    public const float GrappleRange = 28f;
    public const float GrappleConeDeg = 25f;
    public const float GrapplePullSpeed = 22f;
    public const float GrappleReleaseDistance = 1.5f;

    // Helikopter (CONTRACT_V2 §2/§3.3)
    public const float HelicopterFlightTime = 3.5f;
    public const float HelicopterHorizontalSpeed = 9f;
    public const float HelicopterAccel = 30f;
    public const float HelicopterAscendSpeed = 6f;
    public const float HelicopterSinkSpeed = 1.2f;
    public const float HelicopterVerticalAccel = 20f;
    public const float HelicopterMaxRise = 14f;
    /// <summary>
    /// V2-01: Die Flugdecke bezieht sich auf min(Flugstart, letzte Bodenhöhe + diese Reserve). Verkettete Flüge in der
    /// Luft steigen so nicht unbegrenzt (Reserve ≈ Sprunghöhe 2,3 m + Spiel).
    /// </summary>
    public const float HelicopterAirStartAllowance = 4f;

    // Plattform-Formen (CONTRACT_V2 §2/§4)
    public const float RotatingMaxAngularSpeed = 0.9f;
    public const float MinWalkableInnerRadius = 1.2f;
    public const float MaxRampSlopeDeg = 25f;

    public const float DifficultyDistance = 1500f;
    public const float MouseSensitivityDefault = 0.25f;
    public const float MouseSensitivityMin = 0.05f;
    public const float MouseSensitivityMax = 1.0f;

    public const string HighscoreFile = "user://highscore.json";
    public const string SettingsFile = "user://settings.json";
    public const int MaxSaveFileBytes = 4096;
    public const int MaxScoreValue = 10_000_000;

    // Physik-Ebenen (Bitwerte, CONTRACT Abschnitt 4)
    public const uint LayerWorld = 1;
    public const uint LayerPlayer = 2;
    public const uint LayerCrate = 4;
    public const uint LayerPickup = 8;
    public const uint LayerTrigger = 16;
    public const uint LayerGrappleAnchor = 32;
    public const uint LayerPlaceable = 64;
}
