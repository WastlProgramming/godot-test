using System;
using System.Collections.Generic;
using Godot;
using JumpRun.Game;
using JumpRun.World;

namespace JumpRun.Tests;

/// <summary>
/// VISUALS-Test: lädt Environment, Lighting und Backdrop, prüft Material-Pfade (§7.2), Nebel/Sky-Regeln (§7.1/7.3),
/// Backdrop-Budget (Draw Calls, Dreiecke, keine Schatten) und die Stetigkeit über einen Floating-Origin-Shift.
/// Optional (nicht headless): <c>-- --shot=/pfad.png [--view=0..3]</c> rendert eine Vorschau-Szene mit Beispiel-Formen
/// und speichert einen Screenshot (prüft damit auch, dass alle Shader kompilieren).
/// </summary>
public partial class VisualsTest : Node
{
    private static readonly string[] RequiredMaterials =
    {
        "platform_pillars", "platform_spin", "platform_shapes",
        "platform_gap_shape", "platform_stairs_shape", "platform_zigzag_shape", "platform_moving_shape",
        "platform_height_shape", "platform_pillars_shape", "platform_spin_shape", "platform_shapes_shape",
        "pickup_helicopter", "helicopter_rotor", "helicopter_hub",
        "backdrop_mountains_far", "backdrop_mountains_mid", "backdrop_mountains_near",
        "backdrop_islands", "backdrop_clouds", "backdrop_motes",
    };

    private readonly List<string> _failures = new();
    private Node3D? _main;
    private Camera3D? _cam;
    private Backdrop? _backdrop;
    private int _frame;
    private string? _shotPath;
    private int _view;
    private Vector3[]? _before;

    public override void _Ready()
    {
        if (GameManager.Instance != null) GameManager.Instance.PersistenceEnabled = false;
        foreach (string a in OS.GetCmdlineUserArgs())
        {
            if (a.StartsWith("--shot=")) _shotPath = a.Substring(7);
            if (a.StartsWith("--view=")) int.TryParse(a.Substring(7), out _view);
        }

        CheckMaterials();
        CheckEnvironment();

        _main = new Node3D { Name = "Main" };
        AddChild(_main);
        var env = GD.Load<Godot.Environment>("res://art/env/world_environment.tres");
        _main.AddChild(new WorldEnvironment { Name = "WorldEnvironment", Environment = env });
        var lighting = GD.Load<PackedScene>("res://art/env/Lighting.tscn").Instantiate<Node3D>();
        lighting.Name = "Lighting";
        _main.AddChild(lighting);
        CheckLighting(lighting);

        _cam = new Camera3D { Name = "Camera3D", Far = 1500f, Fov = 70f };
        _main.AddChild(_cam);
        _cam.Current = true;
        PlaceCamera(new Vector3(3f, 6f, -990f));

        var scene = GD.Load<PackedScene>("res://scenes/Backdrop.tscn");
        _backdrop = scene?.Instantiate<Backdrop>();
        if (_backdrop == null) { Fail("Backdrop.tscn lädt nicht"); Finish(); return; }
        _main.AddChild(_backdrop);
        CheckBudget(_backdrop);

        if (_shotPath != null) BuildPreview();
    }

    private void PlaceCamera(Vector3 p)
    {
        if (_cam == null) return;
        _cam.GlobalPosition = p;
        float yaw = _view switch { 1 => Mathf.Pi, 2 => Mathf.Pi * 0.5f, _ => 0f };
        float pitch = _view == 3 ? -0.55f : -0.18f;
        _cam.Rotation = new Vector3(pitch, yaw, 0f);
    }

    public override void _Process(double delta)
    {
        _frame++;
        if (_shotPath != null)
        {
            if (_frame == 40)
            {
                Image img = GetViewport().GetTexture().GetImage();
                Error e = img.SavePng(_shotPath);
                GD.Print($"[VisualsTest] screenshot {_shotPath}: {e}");
                Finish();
            }
            return;
        }
        if (_backdrop == null || _cam == null) return;
        GameManager? gm = GameManager.Instance;
        if (_frame == 3)
        {
            _before = RelativePositions();
            // Floating-Origin-Shift wie WorldGenerator.ShiftOrigin: alles um +1000 m in Z, Offset nachführen.
            _cam.GlobalPosition += new Vector3(0f, 0f, 1000f);
            gm?.AddOriginOffset(1000f);
        }
        else if (_frame == 5)
        {
            Vector3[] after = RelativePositions();
            float worst = 0f;
            for (int i = 0; i < after.Length && _before != null && i < _before.Length; i++)
                worst = Math.Max(worst, (after[i] - _before[i]).Length());
            if (gm == null) GD.Print("[VisualsTest] Hinweis: kein GameManager-Autoload – Shift ohne Offset geprüft");
            else if (worst >= 0.5f) Fail($"Origin-Shift: Backdrop-Elemente springen um {worst:F2} m");
            gm?.AddOriginOffset(-1000f);
            Finish();
        }
    }

    /// <summary>Weltpositionen (relativ zur Kamera) aller gewrappten Instanzen + Anker, wie sie der Shader berechnet.</summary>
    private Vector3[] RelativePositions()
    {
        var list = new List<Vector3>();
        Vector3 cam = _cam!.GlobalPosition;
        Vector3 anchor = _backdrop!.GlobalPosition;
        list.Add(anchor - cam);
        foreach (string n in new[] { "IslandsA", "IslandsB", "CloudSea" })
        {
            var mmi = _backdrop.GetNodeOrNull<MultiMeshInstance3D>(n);
            if (mmi?.Multimesh == null) { Fail($"{n} fehlt"); continue; }
            Vector2 period = ((ShaderMaterial)mmi.MaterialOverride).GetShaderParameter("wrap_period").AsVector2();
            MultiMesh mm = mmi.Multimesh;
            for (int i = 0; i < mm.InstanceCount; i++)
            {
                Vector3 o = mm.GetInstanceTransform(i).Origin;
                Vector2 w = Backdrop.WrapOffset(new Vector2(o.X, o.Z), _backdrop.LastScroll, period);
                list.Add(anchor + new Vector3(w.X, o.Y, w.Y) - cam);
            }
        }
        return list.ToArray();
    }

    private void CheckMaterials()
    {
        foreach (string m in RequiredMaterials)
        {
            string path = $"res://art/materials/{m}.tres";
            if (!ResourceLoader.Exists(path)) { Fail($"{path} fehlt"); continue; }
            var mat = GD.Load<Material>(path);
            if (mat == null) { Fail($"{path} lädt nicht"); continue; }
            if (m.EndsWith("_shape") && (mat as ShaderMaterial)?.Shader?.ResourcePath != "res://art/shaders/platform_shape.gdshader")
                Fail($"{m} nutzt nicht platform_shape.gdshader");
        }
    }

    private void CheckEnvironment()
    {
        var env = GD.Load<Godot.Environment>("res://art/env/world_environment.tres");
        if (env == null) { Fail("world_environment.tres lädt nicht"); return; }
        if (env.BackgroundMode != Godot.Environment.BGMode.Sky || env.Sky == null) { Fail("Kein Sky-Hintergrund"); return; }
        if (env.Sky.SkyMaterial is not ShaderMaterial sm || sm.Shader?.ResourcePath != "res://art/shaders/sky.gdshader")
        { Fail("Sky nutzt nicht sky.gdshader"); return; }
        if ((int)env.Sky.RadianceSize > (int)Sky.RadianceSizeEnum.Size64) Fail("radiance_size > 64");
        if (System.Text.RegularExpressions.Regex.IsMatch(StripComments(sm.Shader.Code), @"\bTIME\b") && env.AmbientLightSource == Godot.Environment.AmbientSource.Sky)
            Fail("Sky nutzt TIME, Ambient kommt aber aus dem Sky");
        if (!env.FogEnabled || env.FogDepthEnd > 220f) Fail($"Nebel-Ende {env.FogDepthEnd} > 220");
        Color horizon = sm.GetShaderParameter("horizon_color").AsColor();
        Color fog = env.FogLightColor;
        if (Math.Abs(horizon.R - fog.R) + Math.Abs(horizon.G - fog.G) + Math.Abs(horizon.B - fog.B) > 0.03f)
            Fail($"Nebelfarbe {fog} ≠ Horizont {horizon}");
        foreach (string m in new[] { "backdrop_mountains_far", "backdrop_islands", "backdrop_clouds" })
        {
            var bm = GD.Load<ShaderMaterial>($"res://art/materials/{m}.tres");
            Variant hv = bm?.GetShaderParameter("horizon_color") ?? default;
            Color h = hv.VariantType == Variant.Type.Color ? hv.AsColor()
                : hv.VariantType == Variant.Type.Vector3 ? new Color(hv.AsVector3().X, hv.AsVector3().Y, hv.AsVector3().Z)
                : new Color(0.749f, 0.867f, 0.961f); // Shader-Default
            if (Math.Abs(h.R - fog.R) + Math.Abs(h.G - fog.G) + Math.Abs(h.B - fog.B) > 0.03f) Fail($"{m}: Horizontfarbe ≠ Nebel");
        }
        foreach (string s in new[] { "sky", "backdrop", "platform_shape", "platform", "crate" })
        {
            string code = StripComments(GD.Load<Shader>($"res://art/shaders/{s}.gdshader")?.Code ?? "");
            if (code.Length == 0) { Fail($"{s}.gdshader fehlt"); continue; }
            foreach (string bad in new[] { "hint_screen_texture", "hint_depth_texture", "hint_normal_roughness_texture", "SCREEN_TEXTURE", "DEPTH_TEXTURE" })
                if (code.Contains(bad)) Fail($"{s}.gdshader nutzt {bad}");
        }
    }

    private static string StripComments(string code)
    {
        var sb = new System.Text.StringBuilder();
        foreach (string line in code.Split('\n'))
        {
            int i = line.IndexOf("//", StringComparison.Ordinal);
            sb.AppendLine(i >= 0 ? line.Substring(0, i) : line);
        }
        return sb.ToString();
    }

    private void CheckLighting(Node3D lighting)
    {
        var sun = lighting.GetNodeOrNull<DirectionalLight3D>("Sun");
        if (sun == null) { Fail("Lighting/Sun fehlt"); return; }
        if (!sun.ShadowEnabled) Fail("Sun ohne Schatten");
        if (sun.DirectionalShadowMaxDistance > 150f) Fail("directional_shadow_max_distance > 150");
        foreach (Node n in lighting.GetChildren())
            if (n is Light3D l && l != sun && l.ShadowEnabled) Fail($"{n.Name} wirft Schatten");
    }

    private void CheckBudget(Backdrop b)
    {
        int draws = 0;
        int tris = b.TotalTriangles;
        var stack = new Stack<Node>();
        stack.Push(b);
        while (stack.Count > 0)
        {
            Node n = stack.Pop();
            foreach (Node c in n.GetChildren()) stack.Push(c);
            if (n is not GeometryInstance3D gi || !gi.Visible) continue;
            if (gi.CastShadow != GeometryInstance3D.ShadowCastingSetting.Off) Fail($"{gi.Name} wirft Schatten");
            switch (gi)
            {
                case MeshInstance3D mi when mi.Mesh != null: draws += mi.Mesh.GetSurfaceCount(); break;
                case MultiMeshInstance3D mm when mm.Multimesh?.Mesh != null: draws += mm.Multimesh.Mesh.GetSurfaceCount(); break;
                case CpuParticles3D p: draws += 1; tris += p.Amount * 2; break;
                case GpuParticles3D: Fail("GPUParticles im Backdrop"); break;
            }
            if (gi.MaterialOverride is BaseMaterial3D bm3 && bm3.AlbedoTexture != null) Fail($"{gi.Name} nutzt Textur");
        }
        GD.Print($"[VisualsTest] Backdrop: {draws} Draw Calls, {tris} Dreiecke");
        if (draws > 16) Fail($"Backdrop: {draws} Draw Calls > 16");
        if (draws < 5) Fail($"Backdrop: nur {draws} Draw Calls – Inhalte fehlen?");
        if (tris > 60000) Fail($"Backdrop: {tris} Dreiecke > 60000");
    }

    // ---------- Vorschau (nur mit --shot) ----------

    private void BuildPreview()
    {
        if (_main == null || _cam == null) return;
        PlaceCamera(new Vector3(0f, 6f, 14f));
        var root = new Node3D { Name = "Preview" };
        _main.AddChild(root);
        void Add(Mesh mesh, string mat, Vector3 pos, Vector3 size, float rotY = 0f)
        {
            var mi = new MeshInstance3D { Mesh = mesh, MaterialOverride = GD.Load<Material>($"res://art/materials/{mat}.tres") };
            root.AddChild(mi);
            mi.Position = pos;
            mi.Rotation = new Vector3(0f, rotY, 0f);
            mi.Scale = size;
        }
        var box = new BoxMesh { Size = Vector3.One };
        Add(box, "platform_start", new Vector3(0f, -0.5f, 6f), new Vector3(10f, 1f, 8f));
        Add(Prism(32), "platform_pillars_shape", new Vector3(-6f, -2f, -4f), new Vector3(3.2f, 6f, 3.2f));
        Add(Prism(6), "platform_spin_shape", new Vector3(0f, -0.3f, -6f), new Vector3(5f, 0.6f, 5f), 0.3f);
        Add(Prism(3), "platform_shapes_shape", new Vector3(6f, -0.4f, -4f), new Vector3(4f, 0.8f, 4f));
        Add(Prism(8), "platform_gap_shape", new Vector3(-4f, 0.2f, -14f), new Vector3(4f, 1f, 4f));
        Add(Prism(5), "platform_height_shape", new Vector3(4f, 1.0f, -15f), new Vector3(4f, 1f, 4f));
        Add(Prism(32), "platform_moving_shape", new Vector3(0f, 0.6f, -22f), new Vector3(4f, 0.5f, 4f));
        Add(Prism(4), "platform_zigzag_shape", new Vector3(-8f, 0.5f, -24f), new Vector3(4f, 1f, 4f), Mathf.Pi / 4f);
        Add(box, "platform_shapes", new Vector3(8f, 0f, -26f), new Vector3(4f, 1f, 4f));
        var heli = new SphereMesh { Radius = 0.5f, Height = 1f };
        Add(heli, "pickup_helicopter", new Vector3(2f, 1f, 4f), Vector3.One);
        Add(new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.06f, Height = 0.2f }, "helicopter_hub", new Vector3(-2f, 1.2f, 4f), Vector3.One);
        Add(new BoxMesh { Size = new Vector3(2.4f, 0.04f, 0.18f) }, "helicopter_rotor", new Vector3(-2f, 1.32f, 4f), Vector3.One, 0.6f);
    }

    /// <summary>Unit n-prism nach dem UV/UV2-Vertrag (§4.3), nur für die Vorschau.</summary>
    private static ArrayMesh Prism(int n)
    {
        var v = new List<Vector3>(); var nr = new List<Vector3>(); var uv = new List<Vector2>(); var uv2 = new List<Vector2>();
        void P(Vector3 p, Vector3 normal, Vector2 a, Vector2 b) { v.Add(p); nr.Add(normal); uv.Add(a); uv2.Add(b); }
        Vector3 Corner(int i, float y) { float a = i / (float)n * Mathf.Tau; return new Vector3(-Mathf.Sin(a) * 0.5f, y, -Mathf.Cos(a) * 0.5f); }
        float per = 0f;
        for (int i = 0; i < n; i++)
        {
            Vector3 a0 = Corner(i, 0.5f), a1 = Corner(i + 1, 0.5f), b0 = Corner(i, -0.5f), b1 = Corner(i + 1, -0.5f);
            float w = (a1 - a0).Length();
            Vector3 normal = (((a0 + a1) * 0.5f) with { Y = 0f }).Normalized();
            // top fan (clockwise seen from above)
            P(new Vector3(0, 0.5f, 0), Vector3.Up, Vector2.Zero, new Vector2(0.5f, 1f));
            P(a1, Vector3.Up, new Vector2(a1.X, a1.Z), new Vector2(0.5f, 0f));
            P(a0, Vector3.Up, new Vector2(a0.X, a0.Z), new Vector2(0.5f, 0f));
            // side quad (round shapes: UV2.x constant 0.5 like ShapeMeshFactory -> no vertical seams)
            float x0 = n >= 16 ? 0.5f : 0f, x1 = n >= 16 ? 0.5f : 1f;
            P(a0, normal, new Vector2(per, 1f), new Vector2(x0, 1f));
            P(a1, normal, new Vector2(per + w, 1f), new Vector2(x1, 1f));
            P(b1, normal, new Vector2(per + w, 0f), new Vector2(x1, 0f));
            P(a0, normal, new Vector2(per, 1f), new Vector2(x0, 1f));
            P(b1, normal, new Vector2(per + w, 0f), new Vector2(x1, 0f));
            P(b0, normal, new Vector2(per, 0f), new Vector2(x0, 0f));
            per += w;
        }
        var arr = new Godot.Collections.Array();
        arr.Resize((int)Mesh.ArrayType.Max);
        arr[(int)Mesh.ArrayType.Vertex] = v.ToArray();
        arr[(int)Mesh.ArrayType.Normal] = nr.ToArray();
        arr[(int)Mesh.ArrayType.TexUV] = uv.ToArray();
        arr[(int)Mesh.ArrayType.TexUV2] = uv2.ToArray();
        var m = new ArrayMesh();
        m.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);
        return m;
    }

    private void Fail(string msg) => _failures.Add(msg);

    private bool _finished;
    private void Finish()
    {
        if (_finished) return;
        _finished = true;
        foreach (string f in _failures) GD.PrintErr($"[VisualsTest] {f}");
        if (_failures.Count == 0) { GD.Print("VISUALS_TEST_PASS"); GetTree().Quit(0); }
        else { GD.Print($"VISUALS_TEST_FAIL: {_failures[0]}"); GetTree().Quit(1); }
    }
}
