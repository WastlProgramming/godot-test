using Godot;
using JumpRun.Game;
using JumpRun.Player;

namespace JumpRun.Characters;

/// <summary>
/// Prozedural animiertes Charaktermodell (CONTRACT_V2 §5.5). Liegt im Spieler unter <c>Player/Visual/Rig</c>
/// und liest dort <see cref="IPlayerAnimState"/>; ohne Zustand (Vorschau) läuft eine Idle-/Demo-Animation.
/// Animiert werden ausschließlich die Pivot-Rotationen (Hips/Body/Head/ArmL/ArmR/LegL/LegR + Accessoires).
/// Keine Allokationen in <see cref="_Process"/>.
/// </summary>
public partial class CharacterRig : Node3D
{
    /// <summary>Vom Modellbau befüllte Knoten-Referenzen.</summary>
    public struct Parts
    {
        public Node3D? Hips, Body, Head, ArmL, ArmR, LegL, LegR, Cape, Lid, Sword, Shield;
        public float HipHeight;
        public float ArmLength;
        public CharacterStyle Style;
    }

    /// <summary>Animations-Pose (Euler-Winkel in rad, Reihenfolge YXZ wie Node3D.Rotation).</summary>
    private struct Pose
    {
        public Vector3 ArmL, ArmR, LegL, LegR, Head, Body;
        public float HipsY, Cape, Lid;

        public void AddScaled(in Pose o, float w)
        {
            ArmL += o.ArmL * w; ArmR += o.ArmR * w; LegL += o.LegL * w; LegR += o.LegR * w;
            Head += o.Head * w; Body += o.Body * w; HipsY += o.HipsY * w; Cape += o.Cape * w; Lid += o.Lid * w;
        }
    }

    private const float StepLength = 1.4f;       // m pro Schritt (CONTRACT_V2 §5.5)
    private const float AttackDuration = 0.25f;
    private const float LandDuration = 0.22f;
    private const float JumpStretchDuration = 0.14f;
    private const float BlendRate = 11f;          // 1/s Moduswechsel
    private const float AmpRate = 9f;             // 1/s Laufamplitude
    private const float TurntableDemoCycle = 9f;  // s

    [Export] public string CharacterIdOverride { get; set; } = "";
    [Export] public bool PreviewMode { get; set; }

    public string CharacterId { get; private set; } = CharacterCatalog.DefaultId;
    public float ModelHeight { get; private set; } = 1.8f;
    public CharacterStyle Style => _parts.Style;

    private Parts _parts;
    private IPlayerAnimState? _state;
    private bool _built;

    // Laufzeitzustand der Animation
    private float _time;
    private const float TimeWrap = 20f * Mathf.Pi; // nur Frequenzen k·0,1 rad/s verwenden (siehe _Process)
    private float _phase;
    private float _amp;
    private float _wGround = 1f, _wJump, _wFall, _wFly, _wGrapple;
    private int _lastAttack, _lastLand, _lastJump;
    private float _attackT = -1f, _landT = -1f, _jumpT = -1f;
    private float _demoT;

    /// <summary>Zuletzt angewandte Pose (für Tests/Debug lesbar).</summary>
    public Vector3 CurrentArmR => _parts.ArmR?.Rotation ?? Vector3.Zero;
    public Vector3 CurrentLegL => _parts.LegL?.Rotation ?? Vector3.Zero;

    public override void _Ready()
    {
        _state = GetParent()?.GetParent() as IPlayerAnimState;
        if (!_built)
        {
            string id = !string.IsNullOrEmpty(CharacterIdOverride)
                ? CharacterIdOverride
                : GameManager.Instance?.SelectedCharacter ?? CharacterCatalog.DefaultId;
            Build(id);
        }
        else if (_state != null)
        {
            _state.ModelTopHeight = ModelHeight;
        }
        if (_state != null)
        {
            _lastAttack = _state.AttackCount;
            _lastLand = _state.LandCount;
            _lastJump = _state.JumpCount;
        }
    }

    /// <summary>Baut das Modell neu (alte Kinder werden sofort entfernt und freigegeben).</summary>
    public void Build(string characterId)
    {
        for (int i = GetChildCount() - 1; i >= 0; i--)
        {
            Node c = GetChild(i);
            RemoveChild(c);
            c.QueueFree();
        }
        CharacterDefinition def = CharacterCatalog.Get(characterId);
        CharacterId = def.Id;
        ModelHeight = def.ModelHeight;
        _parts = default;
        CharacterModelFactory.Build(def, this);
        _built = true;
        Scale = Vector3.One;
        _attackT = _landT = _jumpT = -1f;
        if (_state != null) _state.ModelTopHeight = ModelHeight;
        ApplyPose(BuildGroundPose(0f, 0f, 0f), 0f);
    }

    internal void SetParts(in Parts parts) => _parts = parts;

    public override void _Process(double delta)
    {
        if (_parts.Hips == null) return;
        float dt = Mathf.Clamp((float)delta, 0f, 0.1f);
        _time += dt;
        // R V2-09: Alle Oszillator-Frequenzen sind Vielfache von 0,1 rad/s → Periode 20π s ist für jedes sin()
        // eine ganze Zahl von Perioden; der Umbruch ist dadurch nahtlos (vorher Sprung bei 1000 s).
        if (_time > TimeWrap) _time -= TimeWrap;

        // ---- Eingaben sammeln (echter Spieler oder Demo)
        PlayerAnimMode mode;
        float hs, ratio, vy;
        bool grappleValid = false;
        Vector3 grappleTarget = Vector3.Zero;
        if (_state != null)
        {
            mode = _state.AnimMode;
            hs = _state.HorizontalSpeed;
            ratio = _state.MoveSpeedRatio;
            vy = _state.Velocity.Y;
            if (mode == PlayerAnimMode.Grapple)
            {
                grappleTarget = _state.GrappleTargetPosition;
                grappleValid = true;
            }
            int a = _state.AttackCount, l = _state.LandCount, j = _state.JumpCount;
            if (a != _lastAttack) { _lastAttack = a; _attackT = 0f; }
            if (l != _lastLand) { _lastLand = l; _landT = 0f; }
            if (j != _lastJump) { _lastJump = j; _jumpT = 0f; }
        }
        else
        {
            DemoInputs(dt, out mode, out hs, out ratio, out vy);
        }
        if (!float.IsFinite(hs)) hs = 0f;
        if (!float.IsFinite(vy)) vy = 0f;
        ratio = Mathf.Clamp(float.IsFinite(ratio) ? ratio : 0f, 0f, 1f);

        // ---- Modus-Gewichte weich blenden
        float tg = 0, tj = 0, tf = 0, tfl = 0, tgr = 0;
        switch (mode)
        {
            case PlayerAnimMode.Run:
            case PlayerAnimMode.Idle: tg = 1; break;
            case PlayerAnimMode.Jump: tj = 1; break;
            case PlayerAnimMode.Fall: tf = 1; break;
            case PlayerAnimMode.Fly: tfl = 1; break;
            case PlayerAnimMode.Grapple: tgr = 1; break;
        }
        float k = 1f - Mathf.Exp(-BlendRate * dt);
        _wGround += (tg - _wGround) * k;
        _wJump += (tj - _wJump) * k;
        _wFall += (tf - _wFall) * k;
        _wFly += (tfl - _wFly) * k;
        _wGrapple += (tgr - _wGrapple) * k;
        float sum = _wGround + _wJump + _wFall + _wFly + _wGrapple;
        if (sum < 1e-4f) { _wGround = 1f; sum = 1f; }

        // ---- Laufzyklus: Phase ∝ zurückgelegter Weg → kein Fußrutschen
        bool grounded = mode == PlayerAnimMode.Run || mode == PlayerAnimMode.Idle;
        float targetAmp = grounded && hs > 0.3f
            ? Mathf.SmoothStep(0f, 0.25f, ratio) * (0.55f + 0.45f * ratio)
            : 0f;
        _amp += (targetAmp - _amp) * (1f - Mathf.Exp(-AmpRate * dt));
        if (grounded) _phase += hs * dt * Mathf.Pi / StepLength;
        else _phase += dt * 2f * _amp; // ausklingen statt abrupt stoppen
        _phase = Mathf.PosMod(_phase, Mathf.Tau);

        // ---- Posen mischen
        Pose pose = default;
        float inv = 1f / sum;
        if (_wGround > 1e-3f) { Pose g = BuildGroundPose(_phase, _amp, ratio); pose.AddScaled(g, _wGround * inv); }
        if (_wJump > 1e-3f) { Pose g = BuildJumpPose(vy); pose.AddScaled(g, _wJump * inv); }
        if (_wFall > 1e-3f) { Pose g = BuildFallPose(vy); pose.AddScaled(g, _wFall * inv); }
        if (_wFly > 1e-3f) { Pose g = BuildFlyPose(ratio); pose.AddScaled(g, _wFly * inv); }
        if (_wGrapple > 1e-3f) { Pose g = BuildGrapplePose(grappleValid, grappleTarget); pose.AddScaled(g, _wGrapple * inv); }

        // ---- Umhang reagiert auf Tempo/Fallen
        float flutter = Mathf.Sin(_time * 11f) * 0.06f * (0.3f + ratio);
        float capeAir = -(0.55f + Mathf.Clamp(-vy * 0.06f, 0f, 0.8f));
        pose.Cape = (_wGround * -(0.1f + 1.05f * ratio) + (_wJump + _wFall) * capeAir + _wFly * -1.25f
                     + _wGrapple * -0.9f) * inv + flutter;

        // ---- Schlag (ArmR-Schwung 0,25 s)
        float swordSwing = 0f;
        if (_attackT >= 0f)
        {
            _attackT += dt;
            float u = Mathf.Clamp(_attackT / AttackDuration, 0f, 1f);
            float w = u < 0.15f ? u / 0.15f : (u > 0.8f ? (1f - u) / 0.2f : 1f);
            float s = Mathf.SmoothStep(0.2f, 0.75f, u);
            Vector3 strike = new(Mathf.Lerp(2.7f, -0.45f, s), 0f, Mathf.Lerp(0.25f, -0.1f, s));
            pose.ArmR = pose.ArmR.Lerp(strike, w);
            pose.Body.Y += Mathf.Lerp(0.3f, -0.35f, s) * w;
            swordSwing = Mathf.Lerp(-0.6f, 0.9f, s) * w;
            if (_attackT >= AttackDuration) _attackT = -1f;
        }

        // ---- Lande-Stauchung / Sprung-Streckung (Skalierung um die Füße)
        float squash = 0f;
        if (_landT >= 0f)
        {
            _landT += dt;
            float u = _landT / LandDuration;
            squash = u >= 1f ? 0f : Mathf.Sin(u * Mathf.Pi) * (1f - u * 0.4f);
            if (u >= 1f) _landT = -1f;
        }
        float stretch = 0f;
        if (_jumpT >= 0f)
        {
            _jumpT += dt;
            float u = _jumpT / JumpStretchDuration;
            stretch = u >= 1f ? 0f : Mathf.Sin(u * Mathf.Pi);
            if (u >= 1f) _jumpT = -1f;
        }
        Scale = new Vector3(1f + 0.12f * squash - 0.05f * stretch, 1f - 0.17f * squash + 0.08f * stretch,
            1f + 0.12f * squash - 0.05f * stretch);
        pose.Lid += squash * 0.5f + stretch * 0.4f;
        pose.HipsY -= squash * 0.04f;

        ApplyPose(pose, swordSwing);
    }

    // ------------------------------------------------------------------ Posen
    private Pose BuildGroundPose(float phase, float amp, float ratio)
    {
        float s = Mathf.Sin(phase);
        float c = Mathf.Cos(phase);
        float idle = 1f - Mathf.Clamp(amp * 2f, 0f, 1f);
        float breathe = Mathf.Sin(_time * 2.1f);
        var p = new Pose
        {
            LegL = new Vector3(s * 0.8f * amp, 0, 0),
            LegR = new Vector3(-s * 0.8f * amp, 0, 0),
            ArmL = new Vector3(-s * 0.7f * amp, 0, -(0.1f + 0.03f * breathe * idle)),
            ArmR = new Vector3(s * 0.7f * amp, 0, 0.1f + 0.03f * breathe * idle),
            Body = new Vector3(-0.14f * amp * (0.5f + ratio) + 0.015f * breathe * idle, s * 0.12f * amp, 0),
            Head = new Vector3(0.05f * amp + Mathf.Sin(_time * 1.3f) * 0.04f * idle, -s * 0.06f * amp, Mathf.Sin(_time * 0.7f) * 0.03f * idle),
            HipsY = (1f - Mathf.Abs(s)) * 0.06f * amp + breathe * 0.008f * idle,
        };
        p.Lid = _parts.Style == CharacterStyle.Trashcan
            ? 0.14f + Mathf.Abs(c) * 0.22f * amp + Mathf.Max(0f, Mathf.Sin(_time * 1.7f)) * 0.06f * idle
            : 0f;
        return p;
    }

    private Pose BuildJumpPose(float vy)
    {
        float t = Mathf.Clamp(vy / 8f, 0f, 1f);
        return new Pose
        {
            ArmL = new Vector3(2.4f + 0.2f * t, 0, -0.35f),
            ArmR = new Vector3(2.4f + 0.2f * t, 0, 0.35f),
            LegL = new Vector3(0.9f, 0, -0.05f),
            LegR = new Vector3(-0.25f, 0, 0.05f),
            Body = new Vector3(-0.1f, 0, 0),
            Head = new Vector3(0.15f, 0, 0),
            HipsY = 0.02f,
            Lid = 0.95f,
        };
    }

    private Pose BuildFallPose(float vy)
    {
        float row = Mathf.Sin(_time * 9f) * 0.35f;
        float kick = Mathf.Sin(_time * 7f) * 0.15f;
        float fast = Mathf.Clamp(-vy / 15f, 0f, 1f);
        return new Pose
        {
            ArmL = new Vector3(0.3f + row, 0, -1.25f - 0.2f * fast),
            ArmR = new Vector3(0.3f - row, 0, 1.25f + 0.2f * fast),
            LegL = new Vector3(0.35f + kick, 0, -0.12f),
            LegR = new Vector3(-0.1f - kick, 0, 0.12f),
            Body = new Vector3(0.05f, 0, 0),
            Head = new Vector3(-0.2f, 0, 0),
            Lid = 0.45f + Mathf.Sin(_time * 14f) * 0.12f + 0.3f * fast,
        };
    }

    private Pose BuildFlyPose(float ratio)
    {
        float dangle = Mathf.Sin(_time * 3f) * 0.25f;
        if (_parts.Style == CharacterStyle.Superhero)
        {
            // Superheld: rechter Arm nach vorn, Körper geneigt, Beine gestreckt nach hinten.
            return new Pose
            {
                ArmR = new Vector3(2.6f, 0, 0.08f),
                ArmL = new Vector3(0.15f, 0, -0.25f),
                LegL = new Vector3(-0.25f + dangle * 0.3f, 0, -0.04f),
                LegR = new Vector3(-0.35f - dangle * 0.3f, 0, 0.04f),
                Body = new Vector3(-0.3f - 0.25f * ratio, 0, 0),
                Head = new Vector3(0.25f, 0, 0),
            };
        }
        return new Pose
        {
            ArmL = new Vector3(2.95f, 0, -0.28f),
            ArmR = new Vector3(2.95f, 0, 0.28f),
            LegL = new Vector3(0.15f + dangle, 0, -0.06f),
            LegR = new Vector3(0.15f - dangle, 0, 0.06f),
            Body = new Vector3(-0.08f * ratio, 0, 0),
            Head = new Vector3(0.12f, 0, 0),
            Lid = 0.3f + Mathf.Sin(_time * 20f) * 0.1f,
        };
    }

    private Pose BuildGrapplePose(bool valid, Vector3 target)
    {
        var p = new Pose
        {
            ArmR = new Vector3(2.6f, 0, 0.2f),
            ArmL = new Vector3(-0.45f, 0, -0.6f),
            LegL = new Vector3(0.45f, 0, -0.05f),
            LegR = new Vector3(-0.15f, 0, 0.05f),
            Body = new Vector3(-0.12f, 0, 0),
            Head = new Vector3(0.2f, 0, 0),
            Lid = 0.6f,
        };
        Node3D? body = _parts.Body, arm = _parts.ArmR;
        if (valid && body != null && arm != null && body.IsInsideTree() && target.IsFinite())
        {
            Vector3 toTarget = target - arm.GlobalPosition;
            Basis b = body.GlobalBasis.Orthonormalized();
            Vector3 local = b.Inverse() * toTarget;
            if (local.LengthSquared() > 1e-4f)
            {
                var q = new Quaternion(Vector3.Down, local.Normalized());
                Vector3 e = q.GetEuler();
                if (e.IsFinite()) p.ArmR = e;
            }
        }
        return p;
    }

    private void ApplyPose(in Pose p, float swordSwing)
    {
        Parts pt = _parts;
        if (pt.Hips != null) pt.Hips.Position = new Vector3(0, pt.HipHeight + p.HipsY, 0);
        if (pt.Body != null) pt.Body.Rotation = p.Body;
        if (pt.ArmL != null) pt.ArmL.Rotation = p.ArmL;
        if (pt.ArmR != null) pt.ArmR.Rotation = p.ArmR;
        if (pt.LegL != null) pt.LegL.Rotation = p.LegL;
        if (pt.LegR != null) pt.LegR.Rotation = p.LegR;
        if (pt.Head != null)
            pt.Head.Rotation = pt.Style == CharacterStyle.Trashcan
                ? new Vector3(Mathf.Clamp(p.Lid, 0.05f, 1.2f), 0, 0) // Deckel klappt am Scharnier
                : p.Head;
        if (pt.Cape != null) pt.Cape.Rotation = new Vector3(Mathf.Clamp(p.Cape, -1.45f, 0.1f), 0, 0);
        if (pt.Sword != null) pt.Sword.Rotation = new Vector3(0.35f + swordSwing, 0, 0);
    }

    // ------------------------------------------------------------------ Vorschau-Demo
    private void DemoInputs(float dt, out PlayerAnimMode mode, out float hs, out float ratio, out float vy)
    {
        hs = 0f; ratio = 0f; vy = 0f;
        mode = PlayerAnimMode.Idle;
        if (!PreviewMode) return;
        float prev = _demoT;
        _demoT += dt;
        if (_demoT >= TurntableDemoCycle) { _demoT -= TurntableDemoCycle; prev = -1f; }
        float t = _demoT;
        if (t >= 3.0f && t < 5.4f)
        {
            mode = PlayerAnimMode.Run;
            ratio = Mathf.SmoothStep(3.0f, 3.5f, t) * (1f - Mathf.SmoothStep(5.0f, 5.4f, t)) * 0.8f;
            hs = ratio * GameConstants.SprintSpeed;
        }
        else if (t >= 5.4f && t < 6.3f)
        {
            if (prev < 5.4f) _jumpT = 0f;
            vy = 8f - (t - 5.4f) * 18f;
            mode = vy > 0f ? PlayerAnimMode.Jump : PlayerAnimMode.Fall;
        }
        else if (t >= 6.3f && prev < 6.3f)
        {
            _landT = 0f;
        }
        if (t >= 7.4f && prev < 7.4f) _attackT = 0f;
    }
}
