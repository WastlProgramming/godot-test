using System;
using System.Collections.Generic;
using Godot;

namespace JumpRun.World;

/// <summary>
/// Baut Einheits-Meshes für Nicht-Box-Plattformformen (CONTRACT_V2 §4.3):
/// Mittelpunkt im Ursprung, X/Z ∈ [−0.5, 0.5], Y ∈ [−0.5, 0.5] (Node-Scale = Size), flache Normalen pro Fläche
/// (Mantel von Zylinder/Scheibe weich), erste Ecke eines n-Ecks zeigt nach −Z.
/// Attribut-Vertrag für VISUALS: UV planar in Mesh-Einheiten (Deckel XZ, Seiten (Umfang, Höhe));
/// UV2: Deckel/Böden als Fächer (Rand (0.5, 0), Mitte (0.5, 1)), Seitenflächen als Quad (0,0)…(1,1);
/// runde Mäntel UV2.x = 0.5 (nur Ober-/Unterkante sind Kanten). COLOR wird nicht benutzt.
/// </summary>
public static class ShapeMeshFactory
{
    public const int RoundSegments = 32;

    /// <summary>Umriss der Grundfläche (XZ, Einheitsmaß) für prismatische Formen; null für Box/Ramp.</summary>
    public static Vector2[]? Outline(PlatformShape shape)
    {
        switch (shape)
        {
            case PlatformShape.Square:
                return new[] { new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f), new Vector2(0.5f, 0.5f), new Vector2(-0.5f, 0.5f) };
            case PlatformShape.Diamond: return Regular(4);
            case PlatformShape.Cylinder:
            case PlatformShape.Disc: return Regular(RoundSegments);
            case PlatformShape.Triangle: return Regular(3);
            case PlatformShape.Pentagon: return Regular(5);
            case PlatformShape.Hexagon: return Regular(6);
            case PlatformShape.Octagon: return Regular(8);
            case PlatformShape.Cross:
            {
                float a = PlatformShapeExtensions.CrossArmRatio * 0.5f;
                return new[]
                {
                    new Vector2(-0.5f, -a), new Vector2(-a, -a), new Vector2(-a, -0.5f), new Vector2(a, -0.5f),
                    new Vector2(a, -a), new Vector2(0.5f, -a), new Vector2(0.5f, a), new Vector2(a, a),
                    new Vector2(a, 0.5f), new Vector2(-a, 0.5f), new Vector2(-a, a), new Vector2(-0.5f, a),
                };
            }
            default: return null;
        }
    }

    /// <summary>Reguläres n-Eck mit Umkreisradius 0.5, Ecke 0 bei −Z.</summary>
    public static Vector2[] Regular(int n)
    {
        var pts = new Vector2[n];
        for (int k = 0; k < n; k++)
        {
            float th = Mathf.Tau * k / n;
            pts[k] = new Vector2(0.5f * Mathf.Sin(th), -0.5f * Mathf.Cos(th));
        }
        return pts;
    }

    /// <summary>Höhe der Ramp-Oberfläche am +Z-Ende (Einheitsmaß), aus dem Dickenanteil t = Dicke / Size.Y.</summary>
    public static float RampEntryY(float entryFraction) => -0.5f + Mathf.Clamp(entryFraction, 0.02f, 1f);

    /// <summary>Konvexe Hülle (Einheitsmaß) für ConvexPolygonShape3D (Polygone, Ramp).</summary>
    public static Vector3[] UnitHullPoints(PlatformShape shape, float rampEntryFraction = 1f)
    {
        if (shape == PlatformShape.Ramp)
        {
            float yb = RampEntryY(rampEntryFraction);
            return new[]
            {
                new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, -0.5f),
                new Vector3(0.5f, -0.5f, 0.5f), new Vector3(-0.5f, -0.5f, 0.5f),
                new Vector3(-0.5f, 0.5f, -0.5f), new Vector3(0.5f, 0.5f, -0.5f),
                new Vector3(0.5f, yb, 0.5f), new Vector3(-0.5f, yb, 0.5f),
            };
        }
        Vector2[] o = Outline(shape) ?? Outline(PlatformShape.Square)!;
        var pts = new Vector3[o.Length * 2];
        for (int i = 0; i < o.Length; i++)
        {
            pts[i] = new Vector3(o[i].X, 0.5f, o[i].Y);
            pts[o.Length + i] = new Vector3(o[i].X, -0.5f, o[i].Y);
        }
        return pts;
    }

    /// <summary>Einheits-Mesh der Form (ohne Material). Ramp: <paramref name="rampEntryFraction"/> = Dicke / Size.Y.</summary>
    public static ArrayMesh Build(PlatformShape shape, float rampEntryFraction = 1f)
    {
        var b = new Builder();
        if (shape == PlatformShape.Ramp) BuildRamp(b, rampEntryFraction);
        else BuildPrism(b, Outline(shape) ?? Outline(PlatformShape.Square)!, shape.IsRound());
        return b.ToMesh();
    }

    private static void BuildPrism(Builder b, Vector2[] o, bool smoothSides)
    {
        int n = o.Length;
        var top = new Vector3[n];
        var bottom = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            top[i] = new Vector3(o[i].X, 0.5f, o[i].Y);
            bottom[i] = new Vector3(o[i].X, -0.5f, o[i].Y);
        }
        b.Fan(top, new Vector3(0f, 0.5f, 0f), Vector3.Up);
        b.Fan(bottom, new Vector3(0f, -0.5f, 0f), Vector3.Down);

        float area = 0f;
        for (int i = 0; i < n; i++)
        {
            Vector2 p = o[i], q = o[(i + 1) % n];
            area += p.X * q.Y - q.X * p.Y;
        }
        float u = 0f;
        for (int i = 0; i < n; i++)
        {
            Vector2 p = o[i], q = o[(i + 1) % n];
            Vector2 d = q - p;
            Vector2 nn = area > 0f ? new Vector2(d.Y, -d.X) : new Vector2(-d.Y, d.X);
            var normal = new Vector3(nn.X, 0f, nn.Y).Normalized();
            float len = d.Length();
            if (smoothSides)
            {
                var np = new Vector3(p.X, 0f, p.Y).Normalized();
                var nq = new Vector3(q.X, 0f, q.Y).Normalized();
                b.SmoothSide(bottom[i], bottom[(i + 1) % n], top[(i + 1) % n], top[i], np, nq, normal, u, u + len);
            }
            else
            {
                b.Quad(bottom[i], bottom[(i + 1) % n], top[(i + 1) % n], top[i], normal,
                    new Vector2(u, -0.5f), new Vector2(u + len, -0.5f), new Vector2(u + len, 0.5f), new Vector2(u, 0.5f));
            }
            u += len;
        }
    }

    private static void BuildRamp(Builder b, float entryFraction)
    {
        float yb = RampEntryY(entryFraction);
        var b0 = new Vector3(-0.5f, -0.5f, -0.5f); var b1 = new Vector3(0.5f, -0.5f, -0.5f);
        var b2 = new Vector3(0.5f, -0.5f, 0.5f); var b3 = new Vector3(-0.5f, -0.5f, 0.5f);
        var t0 = new Vector3(-0.5f, 0.5f, -0.5f); var t1 = new Vector3(0.5f, 0.5f, -0.5f);
        var t2 = new Vector3(0.5f, yb, 0.5f); var t3 = new Vector3(-0.5f, yb, 0.5f);

        Vector3 topN = new Vector3(0f, 1f, (0.5f - yb)).Normalized(); // steigt nach −Z → Normale kippt nach +Z
        b.Fan(new[] { t0, t1, t2, t3 }, (t0 + t1 + t2 + t3) * 0.25f, topN);
        b.Fan(new[] { b0, b1, b2, b3 }, new Vector3(0f, -0.5f, 0f), Vector3.Down);
        // Vorderseite (−Z, volle Höhe) und Rückseite (+Z, Einstiegskante)
        b.Quad(b1, b0, t0, t1, Vector3.Forward, new Vector2(0f, -0.5f), new Vector2(1f, -0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 0.5f));
        b.Quad(b3, b2, t2, t3, Vector3.Back, new Vector2(0f, -0.5f), new Vector2(1f, -0.5f), new Vector2(1f, yb), new Vector2(0f, yb));
        // Seiten (Trapeze)
        b.Quad(b2, b1, t1, t2, Vector3.Right, new Vector2(0f, -0.5f), new Vector2(1f, -0.5f), new Vector2(1f, 0.5f), new Vector2(0f, yb));
        b.Quad(b0, b3, t3, t0, Vector3.Left, new Vector2(0f, -0.5f), new Vector2(1f, -0.5f), new Vector2(1f, yb), new Vector2(0f, 0.5f));
    }

    private sealed class Builder
    {
        private readonly List<Vector3> _v = new();
        private readonly List<Vector3> _n = new();
        private readonly List<Vector2> _uv = new();
        private readonly List<Vector2> _uv2 = new();
        private readonly List<int> _idx = new();

        private int Vert(Vector3 p, Vector3 n, Vector2 uv, Vector2 uv2)
        {
            _v.Add(p); _n.Add(n); _uv.Add(uv); _uv2.Add(uv2);
            return _v.Count - 1;
        }

        /// <summary>Dreieck so orientiert, dass die Vorderseite (Godot: im Uhrzeigersinn) zu <paramref name="outward"/> zeigt.</summary>
        private void Tri(int a, int b, int c, Vector3 outward)
        {
            Vector3 raw = (_v[b] - _v[a]).Cross(_v[c] - _v[a]);
            if (raw.Dot(outward) > 0f) (b, c) = (c, b);
            _idx.Add(a); _idx.Add(b); _idx.Add(c);
        }

        public void Fan(Vector3[] rim, Vector3 center, Vector3 normal)
        {
            int c = Vert(center, normal, new Vector2(center.X, center.Z), new Vector2(0.5f, 1f));
            int first = _v.Count;
            foreach (Vector3 p in rim) Vert(p, normal, new Vector2(p.X, p.Z), new Vector2(0.5f, 0f));
            for (int i = 0; i < rim.Length; i++)
                Tri(c, first + i, first + (i + 1) % rim.Length, normal);
        }

        public void Quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 normal,
            Vector2 uv0, Vector2 uv1, Vector2 uv2, Vector2 uv3)
        {
            int a = Vert(p0, normal, uv0, new Vector2(0f, 0f));
            int b = Vert(p1, normal, uv1, new Vector2(1f, 0f));
            int c = Vert(p2, normal, uv2, new Vector2(1f, 1f));
            int d = Vert(p3, normal, uv3, new Vector2(0f, 1f));
            Tri(a, b, c, normal);
            Tri(a, c, d, normal);
        }

        public void SmoothSide(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 n0, Vector3 n1, Vector3 faceN, float u0, float u1)
        {
            int a = Vert(p0, n0, new Vector2(u0, -0.5f), new Vector2(0.5f, 0f));
            int b = Vert(p1, n1, new Vector2(u1, -0.5f), new Vector2(0.5f, 0f));
            int c = Vert(p2, n1, new Vector2(u1, 0.5f), new Vector2(0.5f, 1f));
            int d = Vert(p3, n0, new Vector2(u0, 0.5f), new Vector2(0.5f, 1f));
            Tri(a, b, c, faceN);
            Tri(a, c, d, faceN);
        }

        public ArrayMesh ToMesh()
        {
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = _v.ToArray();
            arrays[(int)Mesh.ArrayType.Normal] = _n.ToArray();
            arrays[(int)Mesh.ArrayType.TexUV] = _uv.ToArray();
            arrays[(int)Mesh.ArrayType.TexUV2] = _uv2.ToArray();
            arrays[(int)Mesh.ArrayType.Index] = _idx.ToArray();
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            return mesh;
        }
    }
}
