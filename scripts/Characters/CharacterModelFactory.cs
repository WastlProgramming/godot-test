using System.Collections.Generic;
using Godot;

namespace JumpRun.Characters;

/// <summary>
/// Baut die prozeduralen Charaktermodelle (CONTRACT_V2 §5.4/§5.5) als Kinder eines <see cref="CharacterRig"/>.
/// Hierarchie: Rig/Hips/{Body/{Head,ArmL,ArmR,…},LegL,LegR}. Füße bei y = 0, Blick nach −Z.
/// Limits je Charakter: ≤ 24 MeshInstance3D, ≤ 4000 Dreiecke, ≤ 6 Materialien.
/// </summary>
public static class CharacterModelFactory
{
    private const int CapsuleSegs = 10;
    private const int CapsuleRings = 2;
    private const int CylSegs = 14;
    private const int SphereSegs = 12;
    private const int SphereRings = 6;

    /// <summary>Zwischenspeicher pro Bau (Materialien einmal je Schlüssel).</summary>
    private sealed class Ctx
    {
        public readonly Dictionary<string, Material> Mats = new();
        public Material M(string key) => CharacterMaterials.Get(key, Mats);
    }

    public static void Build(CharacterDefinition def, CharacterRig rig)
    {
        var ctx = new Ctx();
        var hips = Pivot(rig, "Hips", new Vector3(0, def.HipHeight, 0));
        var body = Pivot(hips, "Body", Vector3.Zero);
        var parts = new CharacterRig.Parts { Hips = hips, Body = body, HipHeight = def.HipHeight, Style = def.Style };
        switch (def.Style)
        {
            case CharacterStyle.Bottle: BuildBottle(ctx, def, body, hips, ref parts); break;
            case CharacterStyle.Trashcan: BuildTrashcan(ctx, def, body, hips, ref parts); break;
            case CharacterStyle.Knight: BuildKnight(ctx, def, body, hips, ref parts); break;
            default: BuildSuperhero(ctx, def, body, hips, ref parts); break;
        }
        rig.SetParts(parts);
    }

    // ------------------------------------------------------------------ Flasche
    private static void BuildBottle(Ctx c, CharacterDefinition def, Node3D body, Node3D hips, ref CharacterRig.Parts p)
    {
        Material glass = c.M(CharacterMaterials.BottleGlass), label = c.M(CharacterMaterials.BottleLabel);
        Material gold = c.M(CharacterMaterials.Gold), limb = c.M(CharacterMaterials.Limb);
        Material white = c.M(CharacterMaterials.EyeWhite), dark = c.M(CharacterMaterials.EyeDark);

        AddMesh(body, "Base", Cyl(0.30f, 0.25f, 0.08f), glass, new Vector3(0, -0.06f, 0));
        AddMesh(body, "Torso", Cyl(0.30f, 0.30f, 0.72f), glass, new Vector3(0, 0.34f, 0));
        AddMesh(body, "Shoulder", Cyl(0.11f, 0.30f, 0.24f), glass, new Vector3(0, 0.82f, 0));
        AddMesh(body, "Label", Cyl(0.312f, 0.312f, 0.28f), label, new Vector3(0, 0.28f, 0));
        AddEyes(body, white, dark, 0.11f, 0.56f, -0.255f, 0.08f, 0.042f);

        var head = Pivot(body, "Head", new Vector3(0, 0.94f, 0));
        AddMesh(head, "Neck", Cyl(0.11f, 0.11f, 0.12f), glass, new Vector3(0, 0.06f, 0));
        AddMesh(head, "NeckRing", Cyl(0.135f, 0.135f, 0.03f), gold, new Vector3(0, 0.03f, 0), shadow: false);
        AddMesh(head, "Cap", Cyl(0.13f, 0.13f, 0.13f), gold, new Vector3(0, 0.185f, 0));

        p.Head = head;
        p.ArmL = AddArm(body, "ArmL", new Vector3(-0.31f, 0.52f, 0), 0.045f, 0.48f, limb, 0.075f, white);
        p.ArmR = AddArm(body, "ArmR", new Vector3(0.31f, 0.52f, 0), 0.045f, 0.48f, limb, 0.075f, white);
        p.LegL = AddLeg(hips, "LegL", -0.13f, def.HipHeight, 0.05f, limb, new Vector3(0.14f, 0.08f, 0.22f), gold);
        p.LegR = AddLeg(hips, "LegR", 0.13f, def.HipHeight, 0.05f, limb, new Vector3(0.14f, 0.08f, 0.22f), gold);
        p.ArmLength = 0.46f;
    }

    // ------------------------------------------------------------------ Mülleimer
    private static void BuildTrashcan(Ctx c, CharacterDefinition def, Node3D body, Node3D hips, ref CharacterRig.Parts p)
    {
        Material can = c.M(CharacterMaterials.TrashCan), lidMat = c.M(CharacterMaterials.TrashLid);
        Material gold = c.M(CharacterMaterials.Gold), limb = c.M(CharacterMaterials.Limb);
        Material white = c.M(CharacterMaterials.EyeWhite), dark = c.M(CharacterMaterials.EyeDark);

        AddMesh(body, "Can", Cyl(0.42f, 0.35f, 1.0f), can, new Vector3(0, 0.48f, 0));
        AddMesh(body, "Groove1", Cyl(0.385f, 0.385f, 0.05f), lidMat, new Vector3(0, 0.25f, 0), shadow: false);
        AddMesh(body, "Groove2", Cyl(0.41f, 0.41f, 0.05f), lidMat, new Vector3(0, 0.60f, 0), shadow: false);
        AddMesh(body, "Rim", Cyl(0.435f, 0.435f, 0.06f), lidMat, new Vector3(0, 0.95f, 0));
        AddMesh(body, "Gap", Cyl(0.40f, 0.40f, 0.02f), dark, new Vector3(0, 0.985f, 0), shadow: false);
        AddEyes(body, white, dark, 0.12f, 1.0f, -0.30f, 0.085f, 0.045f);
        AddMesh(body, "Banana", Capsule(0.04f, 0.30f), gold, new Vector3(0.27f, 1.02f, 0.06f),
            new Vector3(10, 0, -55), shadow: false);

        // Deckel = Kopf: Pivot am hinteren Scharnier, klappt um X auf.
        var head = Pivot(body, "Head", new Vector3(0, 1.0f, 0.42f));
        var lid = Pivot(head, "Lid", Vector3.Zero);
        AddMesh(lid, "LidPlate", Cyl(0.40f, 0.45f, 0.08f), lidMat, new Vector3(0, 0.04f, -0.42f));
        AddMesh(lid, "Handle", Box(new Vector3(0.22f, 0.06f, 0.07f)), lidMat, new Vector3(0, 0.11f, -0.42f), shadow: false);

        p.Head = head;
        p.Lid = lid;
        p.ArmL = AddArm(body, "ArmL", new Vector3(-0.41f, 0.66f, 0), 0.055f, 0.50f, limb, 0.085f, white);
        p.ArmR = AddArm(body, "ArmR", new Vector3(0.41f, 0.66f, 0), 0.055f, 0.50f, limb, 0.085f, white);
        p.LegL = AddLeg(hips, "LegL", -0.16f, def.HipHeight, 0.065f, limb, new Vector3(0.17f, 0.09f, 0.26f), lidMat);
        p.LegR = AddLeg(hips, "LegR", 0.16f, def.HipHeight, 0.065f, limb, new Vector3(0.17f, 0.09f, 0.26f), lidMat);
        p.ArmLength = 0.48f;
    }

    // ------------------------------------------------------------------ Superheld
    private static void BuildSuperhero(Ctx c, CharacterDefinition def, Node3D body, Node3D hips, ref CharacterRig.Parts p)
    {
        Material suit = c.M(CharacterMaterials.HeroSuit), gold = c.M(CharacterMaterials.Gold);
        Material skin = c.M(CharacterMaterials.HeroSkin), dark = c.M(CharacterMaterials.EyeDark);
        Material white = c.M(CharacterMaterials.EyeWhite);

        AddMesh(body, "Torso", Capsule(0.25f, 0.80f), suit, new Vector3(0, 0.33f, 0), scale: new Vector3(1.16f, 1f, 0.84f));
        AddMesh(body, "Belt", Cyl(0.262f, 0.262f, 0.08f), gold, new Vector3(0, 0.04f, 0), scale: new Vector3(1.14f, 1f, 0.86f), shadow: false);
        AddMesh(body, "Emblem", new PrismMesh { Size = new Vector3(0.22f, 0.19f, 0.04f) }, gold,
            new Vector3(0, 0.44f, -0.205f), new Vector3(0, 0, 180), shadow: false);

        var head = Pivot(body, "Head", new Vector3(0, 0.74f, 0));
        AddMesh(head, "Face", Sphere(0.16f), skin, new Vector3(0, 0.09f, 0));
        AddMesh(head, "Hair", Sphere(0.168f), dark, new Vector3(0, 0.165f, 0.035f), scale: new Vector3(1f, 0.6f, 1f), shadow: false);
        AddMesh(head, "Mask", Cyl(0.166f, 0.166f, 0.07f), dark, new Vector3(0, 0.095f, 0), shadow: false);
        AddMesh(head, "EyeL", Sphere(0.042f), white, new Vector3(-0.062f, 0.098f, -0.148f), scale: new Vector3(1.2f, 0.8f, 0.6f), shadow: false);
        AddMesh(head, "EyeR", Sphere(0.042f), white, new Vector3(0.062f, 0.098f, -0.148f), scale: new Vector3(1.2f, 0.8f, 0.6f), shadow: false);

        var cape = Pivot(body, "Cape", new Vector3(0, 0.66f, 0.17f));
        AddMesh(cape, "CapeCloth", Box(new Vector3(0.50f, 0.98f, 0.025f)), gold, new Vector3(0, -0.48f, 0.03f));
        AddMesh(cape, "Collar", Box(new Vector3(0.56f, 0.07f, 0.06f)), gold, new Vector3(0, 0.0f, -0.01f), shadow: false);

        p.Head = head;
        p.Cape = cape;
        p.ArmL = AddArm(body, "ArmL", new Vector3(-0.34f, 0.60f, 0), 0.08f, 0.62f, suit, 0.09f, gold);
        p.ArmR = AddArm(body, "ArmR", new Vector3(0.34f, 0.60f, 0), 0.08f, 0.62f, suit, 0.09f, gold);
        p.LegL = AddLeg(hips, "LegL", -0.13f, def.HipHeight, 0.10f, suit, new Vector3(0.19f, 0.2f, 0.3f), gold);
        p.LegR = AddLeg(hips, "LegR", 0.13f, def.HipHeight, 0.10f, suit, new Vector3(0.19f, 0.2f, 0.3f), gold);
        p.ArmLength = 0.56f;
    }

    // ------------------------------------------------------------------ Ritter
    private static void BuildKnight(Ctx c, CharacterDefinition def, Node3D body, Node3D hips, ref CharacterRig.Parts p)
    {
        Material steel = c.M(CharacterMaterials.KnightSteel), tabard = c.M(CharacterMaterials.KnightTabard);
        Material gold = c.M(CharacterMaterials.Gold), dark = c.M(CharacterMaterials.EyeDark);
        Material glow = c.M(CharacterMaterials.KnightEyeGlow);

        AddMesh(body, "Torso", Cyl(0.30f, 0.25f, 0.66f), steel, new Vector3(0, 0.33f, 0), scale: new Vector3(1f, 1f, 0.85f));
        AddMesh(body, "Skirt", Cyl(0.26f, 0.31f, 0.20f), steel, new Vector3(0, -0.04f, 0), scale: new Vector3(1f, 1f, 0.85f));
        AddMesh(body, "Tabard", Box(new Vector3(0.36f, 0.66f, 0.03f)), tabard, new Vector3(0, 0.22f, -0.262f), shadow: false);
        AddMesh(body, "Emblem", Box(new Vector3(0.12f, 0.12f, 0.02f)), gold, new Vector3(0, 0.36f, -0.282f), new Vector3(0, 0, 45), shadow: false);
        AddMesh(body, "PauldronL", Sphere(0.14f), steel, new Vector3(-0.30f, 0.60f, 0), scale: new Vector3(1f, 0.8f, 1f));
        AddMesh(body, "PauldronR", Sphere(0.14f), steel, new Vector3(0.30f, 0.60f, 0), scale: new Vector3(1f, 0.8f, 1f));

        var head = Pivot(body, "Head", new Vector3(0, 0.66f, 0));
        AddMesh(head, "Helm", Cyl(0.18f, 0.18f, 0.29f), steel, new Vector3(0, 0.145f, 0));
        AddMesh(head, "Visor", Box(new Vector3(0.27f, 0.045f, 0.06f)), dark, new Vector3(0, 0.16f, -0.162f), shadow: false);
        AddMesh(head, "EyeL", Sphere(0.024f), glow, new Vector3(-0.065f, 0.16f, -0.186f), shadow: false);
        AddMesh(head, "EyeR", Sphere(0.024f), glow, new Vector3(0.065f, 0.16f, -0.186f), shadow: false);
        AddMesh(head, "Plume", Capsule(0.075f, 0.44f), gold, new Vector3(0, 0.30f, 0.10f), new Vector3(66, 0, 0), shadow: false);

        p.Head = head;
        p.ArmL = AddArm(body, "ArmL", new Vector3(-0.35f, 0.56f, 0), 0.085f, 0.60f, steel, 0.095f, dark);
        p.ArmR = AddArm(body, "ArmR", new Vector3(0.35f, 0.56f, 0), 0.085f, 0.60f, steel, 0.095f, dark);

        // Schwert in der rechten Hand, Klinge nach vorn (−Z) und leicht nach oben.
        var sword = Pivot(p.ArmR, "Sword", new Vector3(0, -0.54f, 0));
        sword.Rotation = new Vector3(0.35f, 0, 0);
        AddMesh(sword, "Grip", Box(new Vector3(0.045f, 0.045f, 0.2f)), dark, Vector3.Zero, shadow: false);
        AddMesh(sword, "Guard", Box(new Vector3(0.06f, 0.26f, 0.06f)), gold, new Vector3(0, 0, -0.11f), shadow: false);
        AddMesh(sword, "Blade", Box(new Vector3(0.025f, 0.10f, 0.66f)), steel, new Vector3(0, 0, -0.46f), shadow: false);

        // Rundschild links außen am Unterarm.
        var shield = Pivot(p.ArmL, "Shield", new Vector3(-0.11f, -0.36f, -0.03f));
        AddMesh(shield, "ShieldFace", Cyl(0.27f, 0.27f, 0.05f), tabard, Vector3.Zero, new Vector3(0, 0, 90));
        AddMesh(shield, "ShieldBoss", Sphere(0.075f), gold, new Vector3(-0.03f, 0, 0), shadow: false);

        p.Sword = sword;
        p.Shield = shield;
        p.LegL = AddLeg(hips, "LegL", -0.14f, def.HipHeight, 0.11f, steel, new Vector3(0.2f, 0.15f, 0.3f), steel);
        p.LegR = AddLeg(hips, "LegR", 0.14f, def.HipHeight, 0.11f, steel, new Vector3(0.2f, 0.15f, 0.3f), steel);
        p.ArmLength = 0.54f;
    }

    // ------------------------------------------------------------------ Bausteine
    private static Node3D Pivot(Node parent, string name, Vector3 pos)
    {
        var n = new Node3D { Name = name, Position = pos };
        parent.AddChild(n);
        return n;
    }

    private static MeshInstance3D AddMesh(Node3D parent, string name, Mesh mesh, Material mat, Vector3 pos,
        Vector3? rotDeg = null, Vector3? scale = null, bool shadow = true)
    {
        var mi = new MeshInstance3D
        {
            Name = name,
            Mesh = mesh,
            MaterialOverride = mat,
            Position = pos,
            CastShadow = shadow ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
        };
        if (rotDeg.HasValue)
        {
            Vector3 r = rotDeg.Value;
            mi.RotationDegrees = r;
        }
        if (scale.HasValue) mi.Scale = scale.Value;
        parent.AddChild(mi);
        return mi;
    }

    private static void AddEyes(Node3D parent, Material white, Material dark, float x, float y, float z, float r, float pupilR)
    {
        AddMesh(parent, "EyeL", Sphere(r), white, new Vector3(-x, y, z), shadow: false);
        AddMesh(parent, "EyeR", Sphere(r), white, new Vector3(x, y, z), shadow: false);
        float pz = z - r + pupilR * 0.55f;
        AddMesh(parent, "PupilL", Sphere(pupilR), dark, new Vector3(-x * 0.96f, y + r * 0.08f, pz), shadow: false);
        AddMesh(parent, "PupilR", Sphere(pupilR), dark, new Vector3(x * 0.96f, y + r * 0.08f, pz), shadow: false);
    }

    /// <summary>Arm-Pivot an der Schulter; Arm hängt entlang −Y, Hand am Ende.</summary>
    private static Node3D AddArm(Node3D body, string name, Vector3 shoulder, float radius, float length,
        Material armMat, float handR, Material handMat)
    {
        var pivot = Pivot(body, name, shoulder);
        AddMesh(pivot, name + "Mesh", Capsule(radius, length), armMat, new Vector3(0, -length * 0.5f + radius, 0));
        AddMesh(pivot, name == "ArmL" ? "HandL" : "HandR", Sphere(handR), handMat, new Vector3(0, -length + radius, 0), shadow: false);
        return pivot;
    }

    /// <summary>Bein-Pivot an der Hüfte; Fußsohle exakt bei y = −hipHeight (Welt: y = 0).</summary>
    private static Node3D AddLeg(Node3D hips, string name, float x, float hipHeight, float radius, Material legMat,
        Vector3 footSize, Material footMat)
    {
        var pivot = Pivot(hips, name, new Vector3(x, 0, 0));
        float legLen = hipHeight - footSize.Y * 0.5f;
        AddMesh(pivot, name + "Mesh", Capsule(radius, legLen), legMat, new Vector3(0, -legLen * 0.5f, 0));
        AddMesh(pivot, name == "LegL" ? "FootL" : "FootR", Box(footSize), footMat,
            new Vector3(0, -hipHeight + footSize.Y * 0.5f, -footSize.Z * 0.18f));
        return pivot;
    }

    private static CapsuleMesh Capsule(float r, float h) =>
        new() { Radius = r, Height = Mathf.Max(h, 2f * r), RadialSegments = CapsuleSegs, Rings = CapsuleRings };

    private static CylinderMesh Cyl(float top, float bottom, float h) =>
        new() { TopRadius = top, BottomRadius = bottom, Height = h, RadialSegments = CylSegs, Rings = 0 };

    private static SphereMesh Sphere(float r) =>
        new() { Radius = r, Height = 2f * r, RadialSegments = SphereSegs, Rings = SphereRings };

    private static BoxMesh Box(Vector3 size) => new() { Size = size };
}
