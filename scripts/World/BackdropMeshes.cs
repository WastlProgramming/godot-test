using System;
using System.Collections.Generic;
using Godot;

namespace JumpRun.World;

/// <summary>
/// Procedural, flat-shaded low-poly meshes for the backdrop (VISUALS). Built once in <see cref="Backdrop"/>._Ready.
/// All meshes carry per-face vertex colours (albedo) and face normals for backdrop.gdshader.
/// Godot uses clockwise winding for front faces; every triangle is oriented against a "desired" normal.
/// </summary>
public static class BackdropMeshes
{
    private sealed class Builder
    {
        public readonly List<Vector3> V = new();
        public readonly List<Vector3> N = new();
        public readonly List<Color> C = new();

        public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 desired, Color col)
        {
            Vector3 n = (c - a).Cross(b - a);
            if (n.Dot(desired) < 0f) { (b, c) = (c, b); n = -n; }
            float len = n.Length();
            if (len < 1e-9f) return;
            n /= len;
            V.Add(a); V.Add(b); V.Add(c);
            N.Add(n); N.Add(n); N.Add(n);
            C.Add(col); C.Add(col); C.Add(col);
        }

        public ArrayMesh ToMesh()
        {
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = V.ToArray();
            arrays[(int)Mesh.ArrayType.Normal] = N.ToArray();
            arrays[(int)Mesh.ArrayType.Color] = C.ToArray();
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            return mesh;
        }
    }

    private static float Rand(Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);

    private static Color Vary(Color c, Random r, float amount)
    {
        float k = 1f + Rand(r, -amount, amount);
        return new Color(c.R * k, c.G * k, c.B * k, 1f);
    }

    /// <summary>Parameters of one silhouette ring around the camera.</summary>
    public struct RingSpec
    {
        public int Segments;
        public float Radius;
        public float RadiusJitter;
        public float Depth;        // horizontal distance base -> ridge (base is closer to the camera)
        public float MinHeight;    // ridge height range (relative to the anchor)
        public float MaxHeight;
        public float BaseY;        // bottom of the silhouette (hidden in haze / under the cloud sea)
        public float Sharpness;    // >1 = fewer, sharper peaks
        public Color Low;
        public Color High;
        public Color Snow;
        public float SnowLine;     // ridge height above which faces turn to snow (> MaxHeight = never)
    }

    /// <summary>Closed ring of mountains (faces look towards the centre). 4 triangles per segment.</summary>
    public static ArrayMesh BuildMountainRing(RingSpec s, int seed)
    {
        var r = new Random(seed);
        int n = Math.Max(16, s.Segments);
        // Seamless profile: integer harmonics + per-column jitter.
        int[] freqs = { 2, 5, 9, 17, 31 };
        float[] amps = { 0.34f, 0.24f, 0.15f, 0.08f, 0.035f };
        float[] phases = new float[freqs.Length];
        for (int i = 0; i < phases.Length; i++) phases[i] = Rand(r, 0f, Mathf.Tau);

        var ridge = new float[n];
        var rad = new float[n];
        var midR = new float[n];
        var midH = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n * Mathf.Tau;
            float f = 0.5f;
            for (int k = 0; k < freqs.Length; k++) f += amps[k] * Mathf.Sin(freqs[k] * t + phases[k]);
            f += Rand(r, -0.03f, 0.03f);
            f = Mathf.Clamp(f, 0f, 1.2f);
            f = Mathf.Pow(f / 1.2f, Math.Max(0.2f, s.Sharpness)) * 1.2f;
            ridge[i] = Mathf.Lerp(s.MinHeight, s.MaxHeight, Mathf.Clamp(f, 0f, 1.2f));
            rad[i] = s.Radius + Rand(r, -s.RadiusJitter, s.RadiusJitter);
            midR[i] = rad[i] - s.Depth * Rand(r, 0.44f, 0.52f);
            midH[i] = Mathf.Lerp(s.BaseY, ridge[i], Rand(r, 0.56f, 0.64f));
        }

        var b = new Builder();
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            float ti = i / (float)n * Mathf.Tau, tj = j / (float)n * Mathf.Tau;
            var di = new Vector3(Mathf.Sin(ti), 0f, Mathf.Cos(ti));
            var dj = new Vector3(Mathf.Sin(tj), 0f, Mathf.Cos(tj));
            Vector3 bi = di * (rad[i] - s.Depth) + Vector3.Up * s.BaseY;
            Vector3 bj = dj * (rad[j] - s.Depth) + Vector3.Up * s.BaseY;
            Vector3 mi = di * midR[i] + Vector3.Up * midH[i];
            Vector3 mj = dj * midR[j] + Vector3.Up * midH[j];
            Vector3 ti3 = di * rad[i] + Vector3.Up * ridge[i];
            Vector3 tj3 = dj * rad[j] + Vector3.Up * ridge[j];
            Vector3 inward = -(di + dj).Normalized() + Vector3.Up * 0.3f;

            Color lowC = Vary(s.Low, r, 0.05f);
            Color midC = Vary(s.Low.Lerp(s.High, 0.55f), r, 0.06f);
            b.Tri(bi, mi, bj, inward, lowC);
            b.Tri(mi, mj, bj, inward, Vary(lowC, r, 0.04f));
            float top = Math.Max(ridge[i], ridge[j]);
            Color topC = top > s.SnowLine ? Vary(s.Snow, r, 0.04f) : Vary(s.High, r, 0.06f);
            b.Tri(mi, ti3, mj, inward, Vary(midC, r, 0.04f));
            b.Tri(ti3, tj3, mj, inward, topC);
        }
        return b.ToMesh();
    }

    /// <summary>Unit floating island: grassy top (radius ≈ 1 at y = 0), rocky inverted cone below, a few trees.</summary>
    public static ArrayMesh BuildIsland(int seed, int sides, float depth, int trees, bool tall)
    {
        var r = new Random(seed);
        var b = new Builder();
        var grass = new Color(0.42f, 0.74f, 0.36f);
        var grassDark = new Color(0.32f, 0.6f, 0.3f);
        var rockTop = new Color(0.62f, 0.52f, 0.44f);
        var rockLow = new Color(0.4f, 0.36f, 0.42f);
        var leaf = new Color(0.2f, 0.5f, 0.3f);

        int n = Math.Max(5, sides);
        // ring 0 = top rim (grass), rings below = rock, last = tip
        float[] ringY = tall ? new[] { 0f, -0.12f, -0.9f, -1.7f } : new[] { 0f, -0.12f, -0.45f, -0.95f };
        float[] ringR = tall ? new[] { 1f, 0.94f, 0.78f, 0.45f } : new[] { 1f, 0.96f, 0.72f, 0.38f };
        int rings = ringY.Length;
        var pts = new Vector3[rings, n];
        float rot = Rand(r, 0f, Mathf.Tau);
        for (int k = 0; k < rings; k++)
        for (int i = 0; i < n; i++)
        {
            float a = rot + i / (float)n * Mathf.Tau + (k == 0 ? 0f : Rand(r, -0.12f, 0.12f));
            float rr = ringR[k] * Rand(r, 0.85f, 1.08f);
            pts[k, i] = new Vector3(Mathf.Sin(a) * rr, ringY[k] * (k == 0 ? 1f : Rand(r, 0.9f, 1.1f)), Mathf.Cos(a) * rr);
        }
        var center = new Vector3(0f, 0.06f, 0f);
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            b.Tri(center, pts[0, i], pts[0, j], Vector3.Up, Vary(i % 2 == 0 ? grass : grassDark, r, 0.05f));
        }
        for (int k = 0; k < rings - 1; k++)
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            Vector3 outward = (pts[k, i] + pts[k, j]);
            outward.Y = 0f;
            Color c = k == 0 ? Vary(grassDark, r, 0.05f) : Vary(rockTop.Lerp(rockLow, k / (float)(rings - 1)), r, 0.07f);
            b.Tri(pts[k, i], pts[k, j], pts[k + 1, i], outward, c);
            b.Tri(pts[k, j], pts[k + 1, j], pts[k + 1, i], outward, Vary(c, r, 0.05f));
        }
        var tip = new Vector3(Rand(r, -0.1f, 0.1f), -depth, Rand(r, -0.1f, 0.1f));
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            Vector3 outward = pts[rings - 1, i] + pts[rings - 1, j];
            outward.Y = -0.5f;
            b.Tri(pts[rings - 1, i], pts[rings - 1, j], tip, outward, Vary(rockLow, r, 0.08f));
        }
        // Low-poly trees (5-sided cones) on the top.
        for (int t = 0; t < trees; t++)
        {
            float a = Rand(r, 0f, Mathf.Tau);
            float d = Rand(r, 0.15f, 0.65f);
            var basePos = new Vector3(Mathf.Sin(a) * d, 0.03f, Mathf.Cos(a) * d);
            float h = Rand(r, 0.22f, 0.4f);
            float rad = h * 0.32f;
            Vector3 apex = basePos + Vector3.Up * h;
            for (int i = 0; i < 5; i++)
            {
                float a0 = i / 5f * Mathf.Tau, a1 = (i + 1) / 5f * Mathf.Tau;
                Vector3 p0 = basePos + new Vector3(Mathf.Sin(a0) * rad, h * 0.15f, Mathf.Cos(a0) * rad);
                Vector3 p1 = basePos + new Vector3(Mathf.Sin(a1) * rad, h * 0.15f, Mathf.Cos(a1) * rad);
                Vector3 outward = (p0 + p1) * 0.5f - basePos;
                outward.Y = rad * 0.6f;
                b.Tri(p0, p1, apex, outward, Vary(leaf, r, 0.12f));
            }
        }
        return b.ToMesh();
    }

    /// <summary>Unit cloud puff: 3 overlapping low-poly domes (upper hemispheres), ~120 triangles.</summary>
    public static ArrayMesh BuildCloudPuff(int seed)
    {
        var r = new Random(seed);
        var b = new Builder();
        var white = new Color(1f, 0.99f, 0.97f);
        var under = new Color(0.86f, 0.89f, 0.96f);
        (Vector3 c, float rad)[] lobes =
        {
            (new Vector3(0f, 0f, 0f), 1f),
            (new Vector3(0.75f, -0.1f, 0.2f), 0.68f),
            (new Vector3(-0.7f, -0.12f, -0.15f), 0.6f),
        };
        const int seg = 10;
        float[] lat = { -0.3f, 0.2f, 0.6f, 0.88f }; // sin(latitude) of the rings, top = pole
        foreach (var (c, rad) in lobes)
        {
            var ring = new Vector3[lat.Length, seg];
            for (int k = 0; k < lat.Length; k++)
            {
                float y = lat[k];
                float rr = Mathf.Sqrt(Math.Max(0f, 1f - y * y));
                for (int i = 0; i < seg; i++)
                {
                    float a = (i + (k % 2) * 0.5f) / seg * Mathf.Tau;
                    float jit = Rand(r, 0.92f, 1.06f);
                    ring[k, i] = c + new Vector3(Mathf.Sin(a) * rr, y, Mathf.Cos(a) * rr) * rad * jit;
                }
            }
            Vector3 pole = c + Vector3.Up * rad;
            for (int k = 0; k < lat.Length - 1; k++)
            for (int i = 0; i < seg; i++)
            {
                int j = (i + 1) % seg;
                Color col = (k == 0 ? under : white);
                Vector3 o1 = (ring[k, i] + ring[k, j] + ring[k + 1, i]) / 3f - c;
                b.Tri(ring[k, i], ring[k, j], ring[k + 1, i], o1, Vary(col, r, 0.02f));
                Vector3 o2 = (ring[k, j] + ring[k + 1, j] + ring[k + 1, i]) / 3f - c;
                b.Tri(ring[k, j], ring[k + 1, j], ring[k + 1, i], o2, Vary(col, r, 0.02f));
            }
            int last = lat.Length - 1;
            for (int i = 0; i < seg; i++)
            {
                int j = (i + 1) % seg;
                Vector3 o = (ring[last, i] + ring[last, j] + pole) / 3f - c;
                b.Tri(ring[last, i], ring[last, j], pole, o, Vary(white, r, 0.02f));
            }
        }
        return b.ToMesh();
    }

    public static int TriangleCount(Mesh? mesh)
    {
        if (mesh == null) return 0;
        int tris = 0;
        for (int s = 0; s < mesh.GetSurfaceCount(); s++)
        {
            var arr = mesh.SurfaceGetArrays(s);
            var idx = arr[(int)Mesh.ArrayType.Index];
            if (idx.VariantType == Variant.Type.PackedInt32Array && idx.AsInt32Array().Length > 0)
                tris += idx.AsInt32Array().Length / 3;
            else
                tris += arr[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length / 3;
        }
        return tris;
    }
}
