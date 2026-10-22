using Godot;
using JumpRun.Game;

namespace JumpRun.Player;

/// <summary>Third-Person-Kamera-Rig: Yaw am Rig, Pitch am Kind, SpringArm3D gegen Durchclippen.</summary>
public partial class ThirdPersonCamera : Node3D
{
    private const float MinPitchDeg = -60f;
    private const float MaxPitchDeg = 35f;
    private const float FollowSharpness = 14f;
    private const float RadiansPerPixel = 0.01f;
    private const float KeyTurnSpeed = 2.4f;        // rad/s für Pfeiltasten
    private const float EdgeMarginRatio = 0.06f;    // Randzone (Anteil der Fensterbreite) im absoluten Modus
    private const float EdgeTurnSpeed = 2.6f;       // rad/s, wenn der Zeiger ganz am Rand steht
    private const float MaxJumpRatio = 0.35f;       // größere Sprünge der Zeigerposition = Teleport, ignorieren
    private const float AutoFollowDelay = 1.2f;     // s ohne Kamera-Eingabe bis zum Nachführen
    private const float AutoFollowSharpness = 1.6f;
    private const float DefaultPitchDeg = -15f;
    private static readonly Vector3 TargetOffset = new(0f, 1.5f, 0f);

    public Camera3D Camera { get; private set; } = null!;

    public Vector3 FlatForward
    {
        get
        {
            Vector3 f = -GlobalBasis.Z;
            f.Y = 0f;
            return f.LengthSquared() < 0.0001f ? Vector3.Forward : f.Normalized();
        }
    }

    private Node3D? _pitch;
    private SpringArm3D? _arm;
    private Node3D? _target;
    private float _yaw;
    private float _pitchRad = Mathf.DegToRad(DefaultPitchDeg);
    private Vector2? _lastMousePos;
    private float _idleTime;

    public override void _Ready()
    {
        TopLevel = true;
        _pitch = GetNodeOrNull<Node3D>("Pitch");
        _arm = GetNodeOrNull<SpringArm3D>("Pitch/SpringArm3D");
        Camera3D? cam = GetNodeOrNull<Camera3D>("Pitch/SpringArm3D/Camera3D");
        if (cam == null)
        {
            // Fallback in den Baum hängen, sonst Fehler bei Current = true und Leck (R-11).
            GD.PushWarning("[ThirdPersonCamera] Pitch/SpringArm3D/Camera3D fehlt – Fallback-Kamera erzeugt.");
            cam = new Camera3D { Name = "FallbackCamera", Position = new Vector3(0f, 0f, 5f) };
            ((Node?)_arm ?? (Node?)_pitch ?? this).AddChild(cam);
        }
        Camera = cam;
        _target = GetParentOrNull<Node3D>();
        if (_arm != null && _target is CollisionObject3D co)
            _arm.AddExcludedObject(co.GetRid());
        if (_target != null)
            GlobalPosition = _target.GlobalPosition + TargetOffset;
        ApplyRotation();
        Camera.Current = true;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (MouseCapture.AbsoluteMode) return; // dort wird in _Process aus der Zeigerposition gerechnet
        if (@event is InputEventMouseMotion mm && Input.MouseMode == Input.MouseModeEnum.Captured)
            TurnBy(mm.Relative);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        bool turned = false;
        if (MouseCapture.AbsoluteMode) turned |= ProcessAbsoluteMouse(dt);
        turned |= ProcessKeys(dt);
        if (turned) _idleTime = 0f;
        else
        {
            _idleTime += dt;
            AutoFollow(dt);
        }

        if (_target == null || !IsInstanceValid(_target)) return;
        Vector3 goal = _target.GlobalPosition + TargetOffset;
        float t = 1f - Mathf.Exp(-FollowSharpness * dt);
        GlobalPosition = GlobalPosition.Lerp(goal, t);
    }

    private void TurnBy(Vector2 pixels)
    {
        GameManager? gm = GameManager.Instance;
        float sens = (gm?.MouseSensitivity ?? GameConstants.MouseSensitivityDefault) * RadiansPerPixel;
        bool invert = gm?.InvertY ?? false;
        _yaw -= pixels.X * sens;
        _pitchRad += (invert ? pixels.Y : -pixels.Y) * sens;
        ApplyRotation();
    }

    /// <summary>WSLg: Delta aus der absoluten Zeigerposition, am Fensterrand kontinuierlich weiterdrehen.</summary>
    private bool ProcessAbsoluteMouse(float dt)
    {
        if (!MouseCapture.IsCaptured) { _lastMousePos = null; return false; }
        Vector2 size = DisplayServer.WindowGetSize();
        if (size.X < 1f || size.Y < 1f) return false;
        // Globale Position minus Fensterposition funktioniert auch, wenn der Zeiger das Fenster verlässt.
        Vector2 pos = DisplayServer.MouseGetPosition() - DisplayServer.WindowGetPosition();

        bool turned = false;
        if (_lastMousePos is Vector2 last)
        {
            Vector2 d = pos - last;
            if (d.Length() < size.X * MaxJumpRatio && d.LengthSquared() > 0.01f)
            {
                TurnBy(d);
                turned = true;
            }
        }
        _lastMousePos = pos;

        float margin = size.X * EdgeMarginRatio;
        float edge = 0f;
        if (pos.X < margin) edge = -Mathf.Clamp((margin - pos.X) / margin, 0f, 1f);
        else if (pos.X > size.X - margin) edge = Mathf.Clamp((pos.X - (size.X - margin)) / margin, 0f, 1f);
        if (edge != 0f)
        {
            float sens = (GameManager.Instance?.MouseSensitivity ?? GameConstants.MouseSensitivityDefault)
                / GameConstants.MouseSensitivityDefault;
            _yaw -= edge * EdgeTurnSpeed * sens * dt;
            ApplyRotation();
            turned = true;
        }
        return turned;
    }

    private bool ProcessKeys(float dt)
    {
        Vector2 k = Input.GetVector("cam_left", "cam_right", "cam_up", "cam_down");
        if (k == Vector2.Zero) return false;
        _yaw -= k.X * KeyTurnSpeed * dt;
        _pitchRad -= k.Y * KeyTurnSpeed * 0.6f * dt;
        ApplyRotation();
        return true;
    }

    /// <summary>Ohne Kamera-Eingabe dreht die Kamera sanft hinter die Laufrichtung (nicht beim Rückwärtslaufen).</summary>
    private void AutoFollow(float dt)
    {
        if (_idleTime < AutoFollowDelay || _target is not CharacterBody3D body) return;
        Vector3 v = body.Velocity;
        v.Y = 0f;
        if (v.LengthSquared() < 4f) return;
        Vector3 dir = v.Normalized();
        if (dir.Dot(FlatForward) < -0.2f) return;
        float targetYaw = Mathf.Atan2(-dir.X, -dir.Z);
        float t = 1f - Mathf.Exp(-AutoFollowSharpness * dt);
        _yaw = Mathf.LerpAngle(_yaw, targetYaw, t);
        _pitchRad = Mathf.Lerp(_pitchRad, Mathf.DegToRad(DefaultPitchDeg), t * 0.5f);
        ApplyRotation();
    }

    public void SnapBehind(Vector3 forward)
    {
        forward.Y = 0f;
        if (forward.LengthSquared() < 0.0001f) forward = Vector3.Forward;
        forward = forward.Normalized();
        _yaw = Mathf.Atan2(-forward.X, -forward.Z);
        _pitchRad = Mathf.DegToRad(DefaultPitchDeg);
        _idleTime = 0f;
        ApplyRotation();
        if (_target != null && IsInstanceValid(_target))
            GlobalPosition = _target.GlobalPosition + TargetOffset;
    }

    public void ApplyOriginShift(Vector3 offset)
    {
        GlobalPosition += offset;
    }

    private void ApplyRotation()
    {
        _pitchRad = Mathf.Clamp(_pitchRad, Mathf.DegToRad(MinPitchDeg), Mathf.DegToRad(MaxPitchDeg));
        Rotation = new Vector3(0f, _yaw, 0f);
        if (_pitch != null) _pitch.Rotation = new Vector3(_pitchRad, 0f, 0f);
    }
}
