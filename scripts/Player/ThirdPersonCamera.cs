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
    private float _pitchRad = Mathf.DegToRad(-15f);

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
        if (@event is InputEventMouseMotion mm && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            GameManager? gm = GameManager.Instance;
            float sens = (gm?.MouseSensitivity ?? GameConstants.MouseSensitivityDefault) * RadiansPerPixel;
            bool invert = gm?.InvertY ?? false;
            _yaw -= mm.Relative.X * sens;
            _pitchRad += (invert ? mm.Relative.Y : -mm.Relative.Y) * sens;
            ApplyRotation();
        }
    }

    public override void _Process(double delta)
    {
        if (_target == null || !IsInstanceValid(_target)) return;
        Vector3 goal = _target.GlobalPosition + TargetOffset;
        float t = 1f - Mathf.Exp(-FollowSharpness * (float)delta);
        GlobalPosition = GlobalPosition.Lerp(goal, t);
    }

    public void SnapBehind(Vector3 forward)
    {
        forward.Y = 0f;
        if (forward.LengthSquared() < 0.0001f) forward = Vector3.Forward;
        forward = forward.Normalized();
        _yaw = Mathf.Atan2(-forward.X, -forward.Z);
        _pitchRad = Mathf.DegToRad(-15f);
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
