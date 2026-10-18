using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using JumpRun.Game;
using JumpRun.Items;
using JumpRun.UI;
using JumpRun.World;

namespace JumpRun.Tests;

/// <summary>Reine Logiktests (ohne Szenenbaum). Liefert eine Liste von Fehlermeldungen.</summary>
public static class LogicTests
{
    public static List<string> RunAll()
    {
        var f = new List<string>();
        SaveSystem.QuietWarnings = true;
        try { Run(f, "SaveSystem", TestSaveSystem); }
        finally { SaveSystem.QuietWarnings = false; }
        Run(f, "ChoosePattern", TestChoosePattern);
        Run(f, "Solvability", TestSolvability);
        Run(f, "RollDrop", TestRollDrop);
        Run(f, "SeedParsing", TestSeedParsing);
        Run(f, "Frustum", TestFrustum);
        Run(f, "TrampolineTrigger", TestTrampolineTrigger);
        Run(f, "Inventory", TestInventory);
        Run(f, "RequiredItems", TestRequiredItems);
        Run(f, "ShapeGeometry", TestShapeGeometry);
        Run(f, "ShapeMeshes", TestShapeMeshes);
        Run(f, "ShapeCoverage", TestShapeCoverage);
        return f;
    }

    private static void Run(List<string> failures, string name, Action<List<string>> test)
    {
        int before = failures.Count;
        try { test(failures); }
        catch (Exception e) { failures.Add($"{name}: Exception {e.GetType().Name}: {e.Message}"); }
        GD.Print($"[LogicTests] {name}: {(failures.Count == before ? "ok" : $"{failures.Count - before} Fehler")}");
    }

    private static void Check(List<string> f, bool cond, string msg)
    {
        if (!cond) f.Add(msg);
    }

    // ------------------------------------------------------------ Trampolin (F-02)

    private static void TestTrampolineTrigger(List<string> f)
    {
        Vector3 t = new(0f, 0f, 0f);
        Check(f, Trampoline.ShouldBounce(t, new Vector3(0f, 0.3f, 0f), new Vector3(0f, -5f, 0f)), "Landung mittig löst nicht aus");
        Check(f, Trampoline.ShouldBounce(t, new Vector3(0.6f, 0f, 0.3f), Vector3.Zero), "Darauflaufen löst nicht aus");
        Check(f, !Trampoline.ShouldBounce(t, new Vector3(0f, 0f, 1.5f), Vector3.Zero), "Spieler 1,5 m daneben (Platzierung) löst aus");
        Check(f, !Trampoline.ShouldBounce(t, new Vector3(1.2f, 0f, 0f), Vector3.Zero), "Seitliches Streifen löst aus");
        Check(f, !Trampoline.ShouldBounce(t, new Vector3(0f, 0.2f, 0f), new Vector3(0f, 8f, 0f)), "Aufstieg löst erneut aus");
        Check(f, !Trampoline.ShouldBounce(t, new Vector3(0f, 2f, 0f), new Vector3(0f, -2f, 0f)), "2 m über dem Pad löst aus");
    }

    // ------------------------------------------------------------ SaveSystem

    private static void TestSaveSystem(List<string> f)
    {
        HighscoreData ok = SaveSystem.ParseHighscore("{\"version\":1,\"highscore\":1234,\"best_distance\":1100,\"extra\":[1,2]}");
        Check(f, ok.Highscore == 1234 && ok.BestDistance == 1100, "gültiges JSON falsch geparst");

        string[] bad =
        {
            "", "   ", "{", "null", "[]", "\"x\"", "{\"version\":2,\"highscore\":5}",
            "{\"version\":\"1\",\"highscore\":5}", "{\"highscore\":5}", "{\"version\":1,\"highscore\":NaN}",
            "{\"version\":1,\"highscore\":5,}", "\u0000\u0001ÿ binär",
            new string('[', 200) + new string(']', 200),
            "{\"version\":1,\"highscore\":\"" + new string('9', 5000) + "\"}",
        };
        foreach (string b in bad)
        {
            HighscoreData d = SaveSystem.ParseHighscore(b);
            Check(f, d.Highscore == 0 && d.BestDistance == 0, $"kaputtes JSON nicht auf Default: {Trim(b)}");
        }

        HighscoreData neg = SaveSystem.ParseHighscore("{\"version\":1,\"highscore\":-50,\"best_distance\":99999999999999}");
        Check(f, neg.Highscore == 0 && neg.BestDistance == GameConstants.MaxScoreValue, "Clamping fehlerhaft");

        HighscoreData types = SaveSystem.ParseHighscore("{\"version\":1,\"highscore\":\"abc\",\"best_distance\":true}");
        Check(f, types.Highscore == 0 && types.BestDistance == 0, "falsche Typen nicht ignoriert");

        HighscoreData frac = SaveSystem.ParseHighscore("{\"version\":1,\"highscore\":12.7,\"best_distance\":1e300}");
        Check(f, frac.Highscore == 12 && frac.BestDistance == GameConstants.MaxScoreValue, "Gleitkommawerte nicht begrenzt");

        var big = new StringBuilder("{\"version\":1,\"highscore\":1,\"pad\":\"");
        big.Append('a', GameConstants.MaxSaveFileBytes);
        big.Append("\"}");
        Check(f, SaveSystem.ParseHighscore(big.ToString()).Highscore == 0, "zu große Datei nicht abgelehnt");

        SettingsData s1 = SaveSystem.ParseSettings("{\"version\":1,\"mouse_sensitivity\":99,\"invert_y\":true}");
        Check(f, Mathf.IsEqualApprox(s1.MouseSensitivity, GameConstants.MouseSensitivityMax) && s1.InvertY, "Settings-Clamp oben");
        SettingsData s2 = SaveSystem.ParseSettings("{\"version\":1,\"mouse_sensitivity\":-3,\"invert_y\":\"yes\"}");
        Check(f, Mathf.IsEqualApprox(s2.MouseSensitivity, GameConstants.MouseSensitivityMin) && !s2.InvertY, "Settings-Clamp unten/Typ");
        // Roundtrip (atomar, Überschreiben) nur, wenn noch kein echter Spielstand existiert.
        if (!Godot.FileAccess.FileExists(GameConstants.HighscoreFile))
        {
            bool w1 = SaveSystem.SaveHighscore(new HighscoreData { Highscore = 10, BestDistance = 5 });
            bool w2 = SaveSystem.SaveHighscore(new HighscoreData { Highscore = 77, BestDistance = 66 });
            HighscoreData rt = SaveSystem.LoadHighscore();
            Check(f, w1 && w2 && rt.Highscore == 77 && rt.BestDistance == 66, "Highscore-Roundtrip fehlgeschlagen");
            Check(f, !Godot.FileAccess.FileExists(GameConstants.HighscoreFile + ".tmp"), ".tmp-Datei bleibt liegen");
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(GameConstants.HighscoreFile));
        }

        SettingsData s3 = SaveSystem.ParseSettings("kaputt");
        Check(f, Mathf.IsEqualApprox(s3.MouseSensitivity, GameConstants.MouseSensitivityDefault), "Settings-Default");
    }

    private static string Trim(string s) => s.Length > 30 ? s[..30] + "…" : s;

    // ------------------------------------------------------------ Musterwahl

    private static void TestChoosePattern(List<string> f)
    {
        foreach (int seed in new[] { 1, 42, 12345, 987654321 })
        {
            PatternType? prev = null;
            int streak = 0;
            for (int i = 0; i < 1000; i++)
            {
                bool prevItem = prev.HasValue && prev.Value.IsItemRequired();
                PatternType p = ChunkBuilder.ChoosePattern(ChunkBuilder.CreateRng(seed, i), i,
                    ChunkBuilder.DifficultyForIndex(i), prev, streak, prevItem);
                if (i == 0) Check(f, p == PatternType.Start, $"seed {seed}: Chunk 0 ist nicht Start");
                else Check(f, p != PatternType.Start, $"seed {seed}: Start bei Index {i}");
                if (i <= 2) Check(f, !p.IsItemRequired(), $"seed {seed}: Item-Chunk bei Index {i}");
                if (prevItem) Check(f, !p.IsItemRequired(), $"seed {seed}: zwei Item-Chunks in Folge bei {i}");
                int newStreak = prev.HasValue && prev.Value == p ? streak + 1 : 1;
                Check(f, newStreak <= 2, $"seed {seed}: Muster {p} 3× in Folge bei {i}");
                streak = newStreak;
                prev = p;
            }
        }
    }

    // ------------------------------------------------------------ Lösbarkeit

    private static void TestSolvability(List<string> f)
    {
        int itemChunks = 0;
        foreach (int seed in new[] { 7, 12345, 55555 })
        {
            var builder = new ChunkBuilder(seed);
            Vector3 entry = Vector3.Zero;
            PatternType? prev = null;
            int streak = 0;
            ChunkLayout? prevLayout = null;
            for (int i = 0; i < 400; i++)
            {
                bool prevItem = prev.HasValue && prev.Value.IsItemRequired();
                ChunkLayout l = builder.Plan(i, entry, prev, streak, prevItem, ChunkBuilder.DifficultyForIndex(i));
                if (prevLayout != null)
                    Check(f, prevLayout.NextPattern == l.Pattern, $"seed {seed}: Vorhersage falsch bei {i}");

                foreach (float g in l.NormalGaps)
                    Check(f, g <= GameConstants.MaxSafeGap + 0.001f, $"seed {seed}: Lücke {g:F2} > MaxSafeGap bei {i} ({l.Pattern})");
                foreach (float s in l.NormalSteps)
                    Check(f, s <= GameConstants.MaxSafeStep + 0.001f, $"seed {seed}: Stufe {s:F2} > MaxSafeStep bei {i} ({l.Pattern})");

                Vector3 exit = l.ExitPoint;
                Check(f, Mathf.Abs(exit.X) <= GameConstants.MaxLateralOffset, $"seed {seed}: X {exit.X:F1} außerhalb bei {i}");
                Check(f, exit.Y >= GameConstants.MinWorldY && exit.Y <= GameConstants.MaxWorldY, $"seed {seed}: Y {exit.Y:F1} außerhalb bei {i}");
                Check(f, exit.Z < entry.Z, $"seed {seed}: Chunk {i} läuft nicht nach -Z");

                if (l.Pattern.IsItemRequired())
                {
                    itemChunks++;
                    ItemType[] need = l.Pattern.RequiredItems();
                    int supply = 0;
                    foreach (CrateSpec c in l.Crates)
                        if (c.IsSupply && c.ForcedDrop.HasValue && Array.IndexOf(need, c.ForcedDrop.Value) >= 0) supply++;
                    Check(f, supply >= 2, $"seed {seed}: Item-Chunk {i} hat nur {supply} Versorgungskisten");
                    bool prevHas = false;
                    if (prevLayout != null)
                        foreach (CrateSpec c in prevLayout.Crates)
                            if (c.ForcedDrop.HasValue && Array.IndexOf(need, c.ForcedDrop.Value) >= 0) prevHas = true;
                    Check(f, prevHas, $"seed {seed}: Vorgänger von Item-Chunk {i} ohne garantierte Kiste");
                    if (l.Pattern == PatternType.BigGap)
                    {
                        Check(f, l.Anchors.Count >= 1, $"seed {seed}: BigGap {i} ohne Anker");
                        Check(f, l.ItemObstacleSize <= GameConstants.BigGapMax, $"seed {seed}: BigGap {i} ohne Brücken-Reserve ({l.ItemObstacleSize:F2} m)");
                        Check(f, l.ItemObstacleSize > GameConstants.MaxSafeGap + 2f, $"seed {seed}: BigGap {i} springbar ({l.ItemObstacleSize:F2} m)");
                    }
                }

                streak = prev.HasValue && prev.Value == l.Pattern ? streak + 1 : 1;
                prev = l.Pattern;
                entry = exit;
                prevLayout = l;
            }
        }
        Check(f, itemChunks > 20, $"zu wenige Item-Chunks generiert ({itemChunks})");
    }

    // ------------------------------------------------------------ Drops

    private static void TestRollDrop(List<string> f)
    {
        var rng = new Random(99);
        var counts = new int[5];
        const int n = 100_000;
        for (int i = 0; i < n; i++) counts[(int)Crate.RollDrop(rng)]++;
        void Near(ItemType t, double expected)
        {
            double share = counts[(int)t] / (double)n;
            Check(f, Math.Abs(share - expected) < 0.015, $"Drop {t}: {share:P1} statt {expected:P0}");
        }
        Near(ItemType.Bridge, 0.27);
        Near(ItemType.Trampoline, 0.27);
        Near(ItemType.GrappleHook, 0.22);
        Near(ItemType.Helicopter, 0.12);
        Near(ItemType.None, 0.12);
    }

    // ------------------------------------------------------------ Inventar (CONTRACT_V2 §3.2)

    private static void TestInventory(List<string> f)
    {
        var inv = new Inventory();
        try
        {
            int added = 0, lastType = -1, lastCount = -1, changed = 0, lastHeli = -1;
            inv.ItemAdded += (t, n) => { added++; lastType = t; lastCount = n; };
            inv.InventoryChanged += (b, t, g, h) => { changed++; lastHeli = h; };
            bool all = true;
            for (int i = 0; i < 50; i++) all &= inv.Add(ItemType.Bridge);
            Check(f, all && inv.GetCount(ItemType.Bridge) == 50, $"50× Add(Bridge) → {inv.GetCount(ItemType.Bridge)}");
            Check(f, added == 50 && lastType == (int)ItemType.Bridge && lastCount == 50, $"ItemAdded falsch ({added}, {lastType}, {lastCount})");
            Check(f, inv.Add(ItemType.Helicopter, 3) && inv.GetCount(ItemType.Helicopter) == 3 && lastHeli == 3, "Helikopter-Slot / 4. Signal-Argument");
            Check(f, !inv.Add(ItemType.None), "Add(None) erlaubt");
            Check(f, !inv.Add(ItemType.Bridge, 0) && !inv.Add(ItemType.Bridge, -4), "Add(x, 0/-4) erlaubt");
            Check(f, !inv.Add((ItemType)5) && !inv.Add((ItemType)(-1)), "ungültiger Typ erlaubt");
            Check(f, !inv.Consume(ItemType.None), "Consume(None) erlaubt");
            Check(f, inv.Consume(ItemType.Helicopter) && inv.GetCount(ItemType.Helicopter) == 2, "Consume(Helicopter)");
            Check(f, inv.Add(ItemType.Trampoline, int.MaxValue) && inv.Add(ItemType.Trampoline, int.MaxValue)
                && inv.GetCount(ItemType.Trampoline) == GameConstants.InventoryHardCap, "Sättigung bei InventoryHardCap");
            inv.Select(ItemType.Bridge);
            var seen = new List<ItemType>();
            for (int i = 0; i < 4; i++) { inv.SelectNext(1); seen.Add(inv.Selected); }
            Check(f, seen[0] == ItemType.Trampoline && seen[1] == ItemType.GrappleHook && seen[2] == ItemType.Helicopter && seen[3] == ItemType.Bridge,
                $"SelectNext zyklisch über 4 Slots: {string.Join(",", seen)}");
            inv.SelectNext(-1);
            Check(f, inv.Selected == ItemType.Helicopter, "SelectNext(-1) von Bridge → Helicopter");
            inv.Clear();
            Check(f, inv.GetCount(ItemType.Bridge) == 0 && inv.GetCount(ItemType.Helicopter) == 0 && changed > 0, "Clear");
            Check(f, HUD.FormatCount(12) == "x12" && HUD.FormatCount(1234) == "x1234", "HUD-Zählerformat");
        }
        finally { inv.Free(); }
    }

    private static void TestRequiredItems(List<string> f)
    {
        ItemType[] big = PatternType.BigGap.RequiredItems();
        ItemType[] wall = PatternType.TallWall.RequiredItems();
        Check(f, Array.IndexOf(big, ItemType.Bridge) >= 0 && Array.IndexOf(big, ItemType.GrappleHook) >= 0 && Array.IndexOf(big, ItemType.Helicopter) >= 0, "BigGap-Items");
        Check(f, Array.IndexOf(wall, ItemType.Trampoline) >= 0 && Array.IndexOf(wall, ItemType.Helicopter) >= 0, "TallWall-Items");
        foreach (PatternType p in new[] { PatternType.Pillars, PatternType.SpinningDiscs, PatternType.ShapeMix })
        {
            Check(f, !p.IsItemRequired(), $"{p} darf kein Item-Muster sein");
            Check(f, MaterialNames.ForPattern(p) != MaterialNames.PlatformGap, $"{p} ohne eigenes Material");
        }
        Check(f, (int)PatternType.ShapeMix == 11 && (int)PlatformShape.Cross == 10 && (int)ItemType.Helicopter == 4, "Enum-Reihenfolge");
    }

    // ------------------------------------------------------------ Formgeometrie (CONTRACT_V2 §4.2)

    private static PlatformSpec Spec(PlatformShape s, Vector3 c, Vector3 size, float spin = 0f) =>
        new() { Shape = s, Center = c, Size = size, SpinSpeed = spin };

    private static void TestShapeGeometry(List<string> f)
    {
        var box = Spec(PlatformShape.Box, Vector3.Zero, new Vector3(4, 1, 4));
        var box2 = Spec(PlatformShape.Box, new Vector3(0, 0, -7), new Vector3(4, 1, 4));
        Check(f, Mathf.IsEqualApprox(ChunkLayout.WalkableGap(box, box2), 3f), "Box-Box-Lücke");
        var disc = Spec(PlatformShape.Disc, new Vector3(0, 0, -7), new Vector3(4, 0.6f, 4));
        Check(f, Mathf.IsEqualApprox(ChunkLayout.WalkableGap(box, disc), 3f), "Box-Disc-Lücke (ausgerichtet)");
        var hex = Spec(PlatformShape.Hexagon, new Vector3(0, 0, -7), new Vector3(4, 1, 4));
        float inner = 2f * Mathf.Cos(Mathf.Pi / 6f);
        Check(f, Mathf.IsEqualApprox(hex.InnerRadius, inner), $"Hexagon-Innenradius {hex.InnerRadius}");
        Check(f, Mathf.IsEqualApprox(ChunkLayout.WalkableGap(box, hex), 7f - 2f - inner), "Box-Hexagon-Lücke");
        var tri = Spec(PlatformShape.Triangle, Vector3.Zero, new Vector3(6, 1, 6));
        Check(f, Mathf.IsEqualApprox(tri.InnerRadius, 1.5f), "Dreieck-Innenradius");
        var dia = Spec(PlatformShape.Diamond, Vector3.Zero, new Vector3(4, 1, 4));
        Check(f, Mathf.IsEqualApprox(dia.InnerRadius, 2f * Mathf.Cos(Mathf.Pi / 4f)), "Diamond-Innenradius");
        // Drehende Box/Square → Innenkreis (drehinvariant)
        var spinSq = Spec(PlatformShape.Square, new Vector3(0, 0, -7), new Vector3(4, 1, 4), 0.5f);
        Check(f, Mathf.IsEqualApprox(ChunkLayout.WalkableGap(box, spinSq), 3f), "drehendes Quadrat");
        // Konservativ: begehbare Lücke nie kleiner als Bounding-Box-Lücke
        var rng = new Random(5);
        for (int i = 0; i < 500; i++)
        {
            var a = Spec((PlatformShape)rng.Next(PlatformShapeExtensions.Count), Vector3.Zero, new Vector3(3, 1, 3));
            float d = 3f + (float)rng.NextDouble() * 4f;
            var b = Spec((PlatformShape)rng.Next(PlatformShapeExtensions.Count), new Vector3((float)rng.NextDouble() * 6f - 3f, 0, -d), new Vector3(3, 1, 3));
            float aabb = Mathf.Max(0f, d - 3f);
            Check(f, ChunkLayout.WalkableGap(a, b) >= aabb - 0.001f, $"WalkableGap < AABB-Lücke ({a.Shape}/{b.Shape})");
            Check(f, Mathf.IsEqualApprox(ChunkLayout.WalkableGap(a, b), ChunkLayout.WalkableGap(b, a)), "WalkableGap nicht symmetrisch");
        }
        // SupportsPoint
        Check(f, ChunkLayout.SupportsPoint(box, new Vector3(1.6f, 0, 0), 0.3f) && !ChunkLayout.SupportsPoint(box, new Vector3(1.8f, 0, 0), 0.3f), "SupportsPoint Box");
        var disc0 = Spec(PlatformShape.Disc, Vector3.Zero, new Vector3(4, 0.6f, 4));
        Check(f, ChunkLayout.SupportsPoint(disc0, new Vector3(1.2f, 0, 1.2f), 0.3f) && !ChunkLayout.SupportsPoint(disc0, new Vector3(1.5f, 0, 1.5f), 0.3f), "SupportsPoint Disc");
        var cross = Spec(PlatformShape.Cross, Vector3.Zero, new Vector3(5, 1, 5));
        Check(f, ChunkLayout.SupportsPoint(cross, new Vector3(2f, 0, 0), 0.3f) && !ChunkLayout.SupportsPoint(cross, new Vector3(2f, 0, 2f), 0.3f), "SupportsPoint Cross");
        // Ramp
        var ramp = new PlatformSpec { Shape = PlatformShape.Ramp, Center = new Vector3(0, 0.5f, -3), Size = new Vector3(4, 1.6f, 6), Rise = 1f };
        Check(f, Mathf.IsEqualApprox(ramp.TopY, 1.3f) && Mathf.IsEqualApprox(ramp.EntryTopY, 0.3f), $"Ramp TopY/EntryTopY {ramp.TopY}/{ramp.EntryTopY}");
        Check(f, Mathf.IsEqualApprox(ramp.SurfaceYAt(-3f), 0.8f), "Ramp SurfaceYAt Mitte");
        // v1-Defaults
        var v1 = new PlatformSpec { Center = new Vector3(0, -0.5f, -2), Size = new Vector3(4, 1, 4) };
        Check(f, v1.Shape == PlatformShape.Box && !v1.IsRotating && v1.TopY == v1.EntryTopY && !v1.IsRoundOrPolygon, "PlatformSpec-Defaults");
    }

    private static void TestShapeMeshes(List<string> f)
    {
        // Referenz-Wicklung aus Godots BoxMesh: Vorzeichen von (b-a)×(c-a)·n für Vorderseiten.
        var bm = new BoxMesh { Size = Vector3.One };
        Godot.Collections.Array ba = bm.GetMeshArrays();
        Vector3[] bv = ba[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        Vector3[] bn = ba[(int)Mesh.ArrayType.Normal].AsVector3Array();
        int[] bi = ba[(int)Mesh.ArrayType.Index].AsInt32Array();
        float refSign = Mathf.Sign((bv[bi[1]] - bv[bi[0]]).Cross(bv[bi[2]] - bv[bi[0]]).Dot(bn[bi[0]]));
        for (int s = 1; s < PlatformShapeExtensions.Count; s++)
        {
            var shape = (PlatformShape)s;
            ArrayMesh m = ShapeMeshFactory.Build(shape, 0.4f);
            Godot.Collections.Array a = m.SurfaceGetArrays(0);
            Vector3[] v = a[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            Vector3[] n = a[(int)Mesh.ArrayType.Normal].AsVector3Array();
            Vector2[] uv2 = a[(int)Mesh.ArrayType.TexUV2].AsVector2Array();
            int[] idx = a[(int)Mesh.ArrayType.Index].AsInt32Array();
            bool bounds = true, uvOk = true, wind = true;
            foreach (Vector3 p in v) bounds &= Mathf.Abs(p.X) <= 0.5001f && Mathf.Abs(p.Y) <= 0.5001f && Mathf.Abs(p.Z) <= 0.5001f;
            foreach (Vector2 q in uv2) uvOk &= q.X >= 0f && q.X <= 1f && q.Y >= 0f && q.Y <= 1f;
            for (int t = 0; t < idx.Length; t += 3)
            {
                Vector3 cr = (v[idx[t + 1]] - v[idx[t]]).Cross(v[idx[t + 2]] - v[idx[t]]);
                Vector3 nn = n[idx[t]] + n[idx[t + 1]] + n[idx[t + 2]];
                if (cr.LengthSquared() < 1e-10f) continue;
                wind &= Mathf.Sign(cr.Dot(nn)) == refSign;
            }
            Check(f, bounds, $"{shape}: Mesh außerhalb [-0.5, 0.5]");
            Check(f, uvOk, $"{shape}: UV2 außerhalb [0, 1]");
            Check(f, wind, $"{shape}: falsche Dreieckswicklung");
            Check(f, idx.Length / 3 <= 300, $"{shape}: zu viele Dreiecke ({idx.Length / 3})");
            if (shape.PolygonSides() > 0 || shape == PlatformShape.Diamond)
            {
                // erste Ecke zeigt nach −Z
                Vector2[] o = ShapeMeshFactory.Outline(shape)!;
                Check(f, Mathf.Abs(o[0].X) < 1e-5f && o[0].Y < -0.49f, $"{shape}: erste Ecke nicht bei −Z");
            }
        }
        bm.Dispose();
    }

    /// <summary>50 Seeds × 100 Chunks: jede Form kommt vor; in den ersten 20 Chunks ≥ 4 Nicht-Box-Formen (Mittel).</summary>
    private static void TestShapeCoverage(List<string> f)
    {
        var seen = new HashSet<PlatformShape>();
        int seedsWithFour = 0;
        for (int seed = 1; seed <= 50; seed++)
        {
            var b = new ChunkBuilder(seed * 31337);
            Vector3 entry = Vector3.Zero;
            PatternType? prev = null;
            int streak = 0;
            var first20 = new HashSet<PlatformShape>();
            for (int i = 0; i < 100; i++)
            {
                bool prevItem = prev.HasValue && prev.Value.IsItemRequired();
                ChunkLayout l = b.Plan(i, entry, prev, streak, prevItem, ChunkBuilder.DifficultyForIndex(i));
                foreach (PlatformSpec p in l.Platforms)
                {
                    seen.Add(p.Shape);
                    if (i < 20 && p.Shape != PlatformShape.Box) first20.Add(p.Shape);
                }
                streak = prev.HasValue && prev.Value == l.Pattern ? streak + 1 : 1;
                prev = l.Pattern;
                entry = l.ExitPoint;
            }
            if (first20.Count >= 4) seedsWithFour++;
        }
        for (int s = 0; s < PlatformShapeExtensions.Count; s++)
            Check(f, seen.Contains((PlatformShape)s), $"Form {(PlatformShape)s} kommt nie vor");
        Check(f, seedsWithFour >= 45, $"nur {seedsWithFour}/50 Seeds mit ≥ 4 Nicht-Box-Formen in 20 Chunks");
    }

    private static void TestSeedParsing(List<string> f)
    {
        Check(f, MainMenu.ParseSeed("123") == 123, "Seed 123");
        Check(f, MainMenu.ParseSeed("999999999") == 999999999, "Seed max");
        Check(f, MainMenu.ParseSeed("1234567890") == null, "Seed zu lang");
        Check(f, MainMenu.ParseSeed("12a") == null, "Seed Buchstaben");
        Check(f, MainMenu.ParseSeed("-5") == null, "Seed negativ");
        Check(f, MainMenu.ParseSeed("١٢٣") == null, "Seed Unicode-Ziffern");
        Check(f, MainMenu.ParseSeed("") == null, "Seed leer");
    }

    private static void TestFrustum(List<string> f)
    {
        // Würfel-Frustum |x|,|y|,|z| <= 10 mit nach außen zeigenden Normalen
        var planes = new Godot.Collections.Array<Plane>
        {
            new(Vector3.Right, 10), new(Vector3.Left, 10), new(Vector3.Up, 10),
            new(Vector3.Down, 10), new(Vector3.Back, 10), new(Vector3.Forward, 10),
        };
        Check(f, FrustumUtil.AabbIntersectsFrustum(new Aabb(new Vector3(-1, -1, -1), Vector3.One * 2), planes), "Frustum innen");
        Check(f, FrustumUtil.AabbIntersectsFrustum(new Aabb(new Vector3(9, 0, 0), Vector3.One * 5), planes), "Frustum überlappend");
        Check(f, !FrustumUtil.AabbIntersectsFrustum(new Aabb(new Vector3(0, 0, 20), Vector3.One * 5), planes), "Frustum außen");
    }
}
