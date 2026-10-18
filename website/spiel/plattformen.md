# Plattformtypen & Sprungmuster

## Plattformformen

Plattformen sind nicht mehr nur Quader. Es gibt elf Formen (`enum PlatformShape`, Reihenfolge verbindlich):

| Nr. | Form | Beschreibung | Kollision |
|---|---|---|---|
| 0 | `Box` | Klassischer Quader (v1) | `BoxShape3D` |
| 1 | `Square` | Quader mit quadratischer Grundfläche | `BoxShape3D` |
| 2 | `Diamond` | Quadrat, um 45° gedreht (Raute) | gedrehte `BoxShape3D` |
| 3 | `Cylinder` | Runde, hohe Säule (Höhe ≥ 2 m) | `CylinderShape3D` |
| 4 | `Disc` | Runde, flache Scheibe (Höhe ≤ 0,6 m) | `CylinderShape3D` |
| 5 | `Triangle` | Aufrechtes Dreiecksprisma | `ConvexPolygonShape3D` |
| 6 | `Pentagon` | Fünfeck | `ConvexPolygonShape3D` |
| 7 | `Hexagon` | Sechseck | `ConvexPolygonShape3D` |
| 8 | `Octagon` | Achteck | `ConvexPolygonShape3D` |
| 9 | `Ramp` | Keil, steigt in Laufrichtung (−Z), höchstens 22° steil | `ConvexPolygonShape3D` |
| 10 | `Cross` | Plus-Form aus zwei Balken | zwei `BoxShape3D` |

Die Kollision entspricht der Darstellung auf ±1 cm genau; konkave Trimesh-Shapes werden nicht verwendet.

### Regeln für begehbare Formen

- Runde und polygonale Formen haben einen **Innenkreisradius von mindestens 1,2 m**
  (`MinWalkableInnerRadius`), damit du sicher landen kannst.
- Rampen sind höchstens 22° steil (Grenze laut Vertrag: `MaxRampSlopeDeg` = 25°).
- Die Abstände zwischen Plattformen werden mit der echten Formgeometrie geprüft (`ChunkLayout.WalkableGap`):
  Rechtecke für Box/Square/Ramp/Cross, ein Kreis mit Innenradius für runde, polygonale und drehende Plattformen.
  Jede Lücke in einem Nicht-Item-Muster ist kleiner als die erreichbare Sprungweite minus 0,8 m, und jede Stufe ist
  höchstens **2 m** hoch (`MaxSafeStep`).
- Kisten stehen nur auf statischen, nicht drehenden Plattformen und nie auf Rampen.

### Bewegte Plattformen

- **`MovingPlatform`** (`AnimatableBody3D`) pendelt entlang eines Versatzes hin und her.
- **`RotatingPlatform`** (`AnimatableBody3D`, `AngularSpeed`) dreht sich um die eigene Hochachse, höchstens mit
  0,9 rad/s (`RotatingMaxAngularSpeed`). Du wirst mitgedreht.

Beide synchronisieren sich mit der Physik (`sync_to_physics`). Solange du auf ihnen stehst, findet kein Floating-Origin-
Versatz statt.

## Sprungmuster

Jeder Chunk folgt genau einem Muster (`enum PatternType`):

| Muster | Inhalt | Formen |
|---|---|---|
| `Start` | Große Startfläche 30 × 12 m mit drei Tutorial-Kisten (Brücke, Trampolin, Helikopter) | Box |
| `GapJump` | Plattformen mit Lücken von 2,5 bis 5,5 m (max. 6 m) | Box, Square, Disc, Hexagon, Octagon, Triangle |
| `Stairs` | Treppe hinauf oder hinunter, Stufen teils als Rampe | Box, Square, Cylinder, Ramp |
| `ZigZag` | Plattformen versetzt links/rechts | Disc, Hexagon, Diamond, Square, Box |
| `MovingPlatforms` | Pendelnde Plattformen über einer Lücke | Box, Disc, Hexagon |
| `NarrowBeams` | Schmale Balken mit kleinen Lücken | Box |
| `HeightJump` | Erst hinunter, dann Plattformen hinaufklettern | Cylinder, Hexagon, Octagon, Box |
| `BigGap` | **Item-Chunk:** Riesenlücke 10–13,5 m mit Enterhaken-Anker | Box |
| `TallWall` | **Item-Chunk:** Wand 5,5–7 m hoch | Box |
| `Pillars` | 5–8 Säulen, 3–8 m hoch, Höhenversatz ≤ 2 m | Cylinder, Hexagon, Octagon |
| `SpinningDiscs` | 3–5 drehende Plattformen (0,4 → 0,9 rad/s, Richtung zufällig), erst ab Chunk 4 | Disc, Hexagon, Triangle |
| `ShapeMix` | 4–7 gemischte Formen aller Typen inkl. Rampe und Kreuz | alle |

Jeder Chunk beginnt mit dem Checkpoint-Pad; Nicht-Item-Muster enden mit einer Landeplattform.

### Musterwahl

Für normale Muster gibt es Gewichte, die von der Schwierigkeit `d` (0 → 1) abhängen:

| Muster | Gewicht |
|---|---|
| `GapJump`, `Stairs` | 3 − 2·d |
| `HeightJump` | 2 |
| `ZigZag`, `MovingPlatforms`, `NarrowBeams` | 1 + 2·d |
| `Pillars` | 1 + d |
| `SpinningDiscs` | 0,5 + 1,5·d (0 vor Chunk 4) |
| `ShapeMix` | 1,5 |

Am Anfang überwiegen also einfache Lücken und Treppen, später Zickzack, bewegliche Plattformen, Balken und drehende
Scheiben. Wie oft Item-Chunks kommen, steht unter [Spielprinzip & endlose Welt](./welt#item-chunks).

## Farben

Jedes Muster hat ein eigenes Material (`platform_gap`, `platform_stairs`, `platform_zigzag`, …). Nicht-Box-Formen nutzen
die Variante `<name>_shape.tres` mit dem Shader `platform_shape.gdshader`. Rot und Orange sind für Item-Hindernisse
reserviert, Magenta ist die Fallback-Farbe für fehlende Materialien. Mehr dazu unter [Grafik](../technik/grafik).
