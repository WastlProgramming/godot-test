using System;
using Godot;
using JumpRun.Game;

namespace JumpRun.World;

/// <summary>
/// Distant scenery (CONTRACT_V2 §7.1). Root of <c>scenes/Backdrop.tscn</c>, instanced as <c>Main/Backdrop</c>
/// (not under WorldRoot, so the Floating Origin does not move it).
/// <para>
/// Follows the active camera in X/Z (Y damped). Mountain rings are locked to the camera (they are "infinitely" far);
/// floating islands and the cloud sea are world-stable: their MultiMesh instances are wrapped on the GPU
/// (backdrop.gdshader) around the camera's absolute position <c>absZ = local.Z − GameManager.OriginOffsetZ</c>, which is
/// continuous across origin shifts. Per frame this node only writes a few shader uniforms (no allocations).
/// </para>
/// Budget: 7 draw calls, ~35k triangles, no shadows, no textures.
/// </summary>
public partial class Backdrop : Node3D
{
    [Export] public int Seed { get; set; } = 7331;
    /// <summary>1/s – how fast the backdrop height follows the camera.</summary>
    [Export] public float VerticalFollowRate { get; set; } = 0.8f;
    /// <summary>Maximum vertical lag behind the camera (m).</summary>
    [Export] public float MaxVerticalLag { get; set; } = 18f;
    /// <summary>Height of the cloud sea relative to the (damped) camera height.</summary>
    [Export] public float CloudLevel { get; set; } = -78f;
    [Export] public int IslandsPerVariant { get; set; } = 9;
    [Export] public int CloudPuffs { get; set; } = 190;

    public const float IslandPeriodZ = 1600f;
    public const float CloudPeriod = 1500f;
    public const float IslandMinSide = 250f;
    public const float IslandMaxSide = 600f;

    private static readonly StringName ScrollParam = "scroll";
    private static readonly StringName AnchorParam = "anchor";
    private static readonly StringName SunDirParam = "sun_dir";
    private static readonly StringName SunColorParam = "sun_color";
    private static readonly StringName PeriodParam = "wrap_period";

    private const int MaxMaterials = 8;
    private readonly ShaderMaterial?[] _materials = new ShaderMaterial?[MaxMaterials];
    private readonly Vector2[] _periods = new Vector2[MaxMaterials];
    private int _materialCount;

    private Node3D? _motes;
    private float _y;
    private bool _hasY;
    private bool _sunApplied;
    private int _sunTries;

    /// <summary>Camera absolute XZ used by the last update (X = local X, Y = absolute Z).</summary>
    public Vector2 LastScroll { get; private set; }
    public int TotalTriangles { get; private set; }

    public override void _Ready()
    {
        // Run after the camera rig (default priority 0) so the backdrop never lags one frame behind.
        ProcessPriority = 100;
        var rng = new Random(Seed);

        BuildRing("MountainsFar", new BackdropMeshes.RingSpec
        {
            Segments = 110, Radius = 1180f, RadiusJitter = 40f, Depth = 220f,
            MinHeight = 20f, MaxHeight = 250f, BaseY = -260f, Sharpness = 1.5f,
            Low = new Color(0.4f, 0.48f, 0.7f), High = new Color(0.52f, 0.56f, 0.76f),
            Snow = new Color(0.97f, 0.98f, 1f), SnowLine = 185f,
        }, rng.Next());
        BuildRing("MountainsMid", new BackdropMeshes.RingSpec
        {
            Segments = 96, Radius = 860f, RadiusJitter = 45f, Depth = 180f,
            MinHeight = -40f, MaxHeight = 170f, BaseY = -220f, Sharpness = 1.8f,
            Low = new Color(0.22f, 0.4f, 0.5f), High = new Color(0.34f, 0.5f, 0.56f),
            Snow = new Color(0.93f, 0.95f, 1f), SnowLine = 150f,
        }, rng.Next());
        BuildRing("MountainsNear", new BackdropMeshes.RingSpec
        {
            Segments = 80, Radius = 560f, RadiusJitter = 35f, Depth = 140f,
            MinHeight = -80f, MaxHeight = 120f, BaseY = -200f, Sharpness = 2.4f,
            Low = new Color(0.18f, 0.36f, 0.3f), High = new Color(0.32f, 0.56f, 0.3f),
            Snow = new Color(0.9f, 0.94f, 0.92f), SnowLine = 1e6f,
        }, rng.Next());

        BuildIslands("IslandsA", BackdropMeshes.BuildIsland(rng.Next(), 9, 1.9f, 4, false), rng.Next(), 0f);
        BuildIslands("IslandsB", BackdropMeshes.BuildIsland(rng.Next(), 7, 2.6f, 2, true), rng.Next(), IslandPeriodZ * 0.5f / Math.Max(1, IslandsPerVariant));
        BuildClouds("CloudSea", rng.Next());

        _motes = GetNodeOrNull<Node3D>("Motes");
        if (_motes is GeometryInstance3D gi) gi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        if (_motes != null) _motes.TopLevel = true;
    }

    public override void _Process(double delta)
    {
        Camera3D? cam = GetViewport()?.GetCamera3D();
        if (cam == null || !IsInstanceValid(cam)) return;
        if (!_sunApplied && _sunTries < 120) { _sunTries++; TryApplySun(); }

        Vector3 cp = cam.GlobalPosition;
        float dt = (float)delta;
        if (!_hasY) { _y = cp.Y; _hasY = true; }
        else
        {
            _y = Mathf.Lerp(_y, cp.Y, 1f - Mathf.Exp(-VerticalFollowRate * dt));
            _y = Mathf.Clamp(_y, cp.Y - MaxVerticalLag, cp.Y + MaxVerticalLag);
        }
        var anchor = new Vector3(cp.X, _y, cp.Z);
        GlobalPosition = anchor;
        if (_motes != null) _motes.GlobalPosition = cp + new Vector3(0f, 0.5f, -6f);
        UpdateScroll(cp, anchor);
    }

    /// <summary>Absolute camera Z that is continuous across Floating-Origin shifts.</summary>
    public static float AbsoluteZ(float localZ) => localZ - (GameManager.Instance?.OriginOffsetZ ?? 0f);

    private void UpdateScroll(Vector3 cp, Vector3 anchor)
    {
        double absX = cp.X;
        double absZ = cp.Z - (GameManager.Instance?.OriginOffsetZPrecise ?? 0.0);
        LastScroll = new Vector2((float)absX, (float)absZ);
        for (int i = 0; i < _materialCount; i++)
        {
            ShaderMaterial? m = _materials[i];
            if (m == null) continue;
            Vector2 p = _periods[i];
            // Reduce modulo the period in double precision -> no float precision loss on very long runs.
            float sx = p.X > 0f ? (float)PosMod(absX, p.X) : 0f;
            float sz = p.Y > 0f ? (float)PosMod(absZ, p.Y) : 0f;
            m.SetShaderParameter(ScrollParam, new Vector2(sx, sz));
            m.SetShaderParameter(AnchorParam, anchor);
        }
    }

    private static double PosMod(double x, double p)
    {
        double r = x % p;
        return r < 0 ? r + p : r;
    }

    /// <summary>
    /// CPU replica of the shader wrap: offset of a wrapped instance relative to the anchor (for tests/tools).
    /// </summary>
    public static Vector2 WrapOffset(Vector2 baseOffset, Vector2 scroll, Vector2 period)
    {
        Vector2 o = baseOffset;
        if (period.X > 0f) o.X = Wrap1(baseOffset.X - (float)PosMod(scroll.X, period.X), period.X);
        if (period.Y > 0f) o.Y = Wrap1(baseOffset.Y - (float)PosMod(scroll.Y, period.Y), period.Y);
        return o;
    }

    private static float Wrap1(float x, float p) => x - p * Mathf.Floor(x / p + 0.5f);

    private void TryApplySun()
    {
        DirectionalLight3D? sun = GetNodeOrNull<DirectionalLight3D>("../Lighting/Sun");
        if (sun == null || !sun.IsInsideTree()) return;
        Vector3 toSun = sun.GlobalTransform.Basis.Z.Normalized();
        Color c = sun.LightColor;
        for (int i = 0; i < _materialCount; i++)
        {
            _materials[i]?.SetShaderParameter(SunDirParam, toSun);
            _materials[i]?.SetShaderParameter(SunColorParam, new Vector3(c.R, c.G, c.B));
        }
        _sunApplied = true;
    }

    private ShaderMaterial? Register(GeometryInstance3D gi)
    {
        gi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        gi.GIMode = GeometryInstance3D.GIModeEnum.Disabled;
        if (gi.MaterialOverride is not ShaderMaterial shared) return null;
        // Own copy per Backdrop instance (uniforms are written per frame).
        var mat = (ShaderMaterial)shared.Duplicate();
        gi.MaterialOverride = mat;
        if (_materialCount < MaxMaterials)
        {
            _materials[_materialCount] = mat;
            Variant pv = mat.GetShaderParameter(PeriodParam);
            _periods[_materialCount] = pv.VariantType == Variant.Type.Vector2 ? pv.AsVector2() : Vector2.Zero;
            _materialCount++;
        }
        return mat;
    }

    private void BuildRing(string node, BackdropMeshes.RingSpec spec, int seed)
    {
        var mi = GetNodeOrNull<MeshInstance3D>(node);
        if (mi == null) return;
        var mesh = BackdropMeshes.BuildMountainRing(spec, seed);
        mi.Mesh = mesh;
        Register(mi);
        // The ring always surrounds the camera: give it a generous AABB so it is never culled.
        mi.CustomAabb = new Aabb(new Vector3(-1500f, -400f, -1500f), new Vector3(3000f, 900f, 3000f));
        TotalTriangles += BackdropMeshes.TriangleCount(mesh);
    }

    private void BuildIslands(string node, ArrayMesh mesh, int seed, float zPhase)
    {
        var mmi = GetNodeOrNull<MultiMeshInstance3D>(node);
        if (mmi == null) return;
        var r = new Random(seed);
        int count = Math.Max(0, IslandsPerVariant);
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = count };
        float slot = IslandPeriodZ / Math.Max(1, count);
        for (int i = 0; i < count; i++)
        {
            float side = (i % 2 == 0) ? -1f : 1f;
            if (r.NextDouble() < 0.25) side = -side;
            float x = side * (IslandMinSide + (float)r.NextDouble() * (IslandMaxSide - IslandMinSide));
            float z = -IslandPeriodZ * 0.5f + zPhase + slot * (i + 0.2f + 0.6f * (float)r.NextDouble());
            float y = -30f + (float)r.NextDouble() * 90f;
            float s = 45f + (float)r.NextDouble() * 70f;
            var basis = new Basis(Vector3.Up, (float)r.NextDouble() * Mathf.Tau)
                .Scaled(new Vector3(s, s * (0.75f + 0.5f * (float)r.NextDouble()), s * (0.8f + 0.4f * (float)r.NextDouble())));
            mm.SetInstanceTransform(i, new Transform3D(basis, new Vector3(x, y, z)));
        }
        mmi.Multimesh = mm;
        Register(mmi);
        mmi.CustomAabb = new Aabb(new Vector3(-IslandMaxSide - 300f, -300f, -IslandPeriodZ), new Vector3(2f * IslandMaxSide + 600f, 600f, 2f * IslandPeriodZ));
        TotalTriangles += BackdropMeshes.TriangleCount(mesh) * count;
    }

    private void BuildClouds(string node, int seed)
    {
        var mmi = GetNodeOrNull<MultiMeshInstance3D>(node);
        if (mmi == null) return;
        var r = new Random(seed);
        ArrayMesh mesh = BackdropMeshes.BuildCloudPuff(r.Next());
        int count = Math.Max(0, CloudPuffs);
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = count };
        // Jittered grid -> even coverage with gaps.
        int grid = Math.Max(1, (int)Mathf.Ceil(Mathf.Sqrt(count)));
        float cell = CloudPeriod / grid;
        for (int i = 0; i < count; i++)
        {
            int gx = i % grid, gz = i / grid;
            float x = -CloudPeriod * 0.5f + (gx + (float)r.NextDouble()) * cell;
            float z = -CloudPeriod * 0.5f + (gz + (float)r.NextDouble()) * cell;
            float y = CloudLevel + (float)r.NextDouble() * 10f - 7f;
            float s = 26f + (float)r.NextDouble() * 34f;
            var basis = new Basis(Vector3.Up, (float)r.NextDouble() * Mathf.Tau)
                .Scaled(new Vector3(s, 9f + 9f * (float)r.NextDouble(), s * (0.8f + 0.4f * (float)r.NextDouble())));
            mm.SetInstanceTransform(i, new Transform3D(basis, new Vector3(x, y, z)));
        }
        mmi.Multimesh = mm;
        Register(mmi);
        mmi.CustomAabb = new Aabb(new Vector3(-CloudPeriod, -200f, -CloudPeriod), new Vector3(2f * CloudPeriod, 300f, 2f * CloudPeriod));
        TotalTriangles += BackdropMeshes.TriangleCount(mesh) * count;
    }
}
