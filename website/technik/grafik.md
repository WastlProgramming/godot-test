# Grafik

Alle Grafik ist prozedural: Meshes entstehen im Code, Materialien sind Text-Ressourcen (`.tres`), Shader liegen unter
`art/shaders/`. Es gibt keine Textur-Dateien.

## Renderer-Regeln

Das Spiel muss in **Forward+** (Windows) und **Compatibility / OpenGL 3** (`run-linux.sh`) gleich funktionieren.
Ziel sind 60 fps auf einer integrierten AMD-GPU bei 1280 × 720.

| Erlaubt | Verboten |
|---|---|
| Eigene Shader ohne Bildschirm-/Tiefentextur | SDFGI, VoxelGI, SSR, SSIL, Volumetric Fog |
| `CPUParticles3D` | `GPUParticles3D`, Compute-Shader |
| SSAO (Compatibility ignoriert es) | `hint_screen_texture`, `hint_depth_texture` |
| Ein schattenwerfendes Licht (Sonne) | weitere schattenwerfende Lichter |

Außerdem gilt: keine Allokationen pro Frame im Backdrop-Code, und alles muss headless ohne Renderer laufen.

## Himmel und Umgebung

`art/env/world_environment.tres`:

- **Himmel** aus `sky.gdshader` (`shader_type sky`): Verlauf von Zenit `#2F6FD6` über `#6FA6E8` zum Horizont
  `#BFDDF5`, Sonnenscheibe mit Halo aus `LIGHT0`, statische Wolken (4 Oktaven fbm). Ohne `TIME` – die Radiance-Map
  (64 px) wird nur einmal berechnet.
- **Tiefennebel** von 80 m bis 200 m in Horizontfarbe. Neue Chunks entstehen ≈ 240 m voraus und blenden so aus dem
  Nebel ein.
- **Tonemapping** AgX, Glow (Schwelle 1,0) vor allem für leuchtende Items, Anker und Gefahrenkanten, SSAO,
  leicht erhöhte Sättigung.

`art/env/Lighting.tscn`: warme Sonne (`#FFECCC`, PSSM-Schatten bis 120 m) und ein kühles, schattenloses Fülllicht
von vorn links.

Der Himmels-Shader läuft für den sichtbaren Hintergrund jedes Frame pro Pixel (fbm + Sonne, wenige ALU-Befehle).
Half-/Quarter-Res-Pässe nutzt er nicht, weil der Compatibility-Renderer sie nicht unterstützt.

## Grafikqualität

Im Pause-Menü schaltet **„Grafik: niedrig (schneller)“** zwischen zwei Stufen um (`GameManager.GraphicsLow`,
angewendet in `Main.ApplyGraphicsQuality`):

| | Hoch | Niedrig |
|---|---|---|
| SSAO | an (nur Forward+) | aus |
| Sonnenschatten | PSSM 4 Splits, überblendet, bis 120 m | PSSM 2 Splits, ohne Überblendung, bis 60 m |

Ohne gespeicherte Wahl gilt automatisch: **niedrig** im Compatibility-Renderer (`run-linux.sh`), **hoch** in
Forward+. Die Wahl landet als `graphics_low` in `user://settings.json`. Für einen Start ohne Speichern gibt es
`-- --quality=low` bzw. `-- --quality=high`.

## Hintergrund (Backdrop)

`scenes/Backdrop.tscn` hängt direkt unter `Main` (nicht unter `WorldRoot`), damit der Floating Origin ihn nicht
verschiebt. Er folgt der Kamera:

| Ebene | Entfernung | Verhalten |
|---|---|---|
| `MountainsFar` / `Mid` / `Near` | ≈ 1180 / 860 / 560 m | Bergringe (Schnee → blaugrün → grüne Hügel), fest an der Kamera |
| `IslandsA` / `IslandsB` | 250–600 m seitlich | 2 × 9 schwebende Inseln mit Bäumen, weltstabil (Periode 1600 m, Wrap im Shader) |
| `CloudSea` | ≈ 78 m unter der Kamera | 190 Wolkenpuffs, weltstabil (1500 × 1500 m Wrap) |
| `Motes` | um die Kamera | 48 kleine Schwebe-Partikel (`CPUParticles3D`) |

Die weltstabilen Ebenen werden über die absolute Z-Position `absZ = local.Z − GameManager.OriginOffsetZ` gewickelt –
so bleibt der Hintergrund auch beim Origin-Shift ruhig. Pro Frame schreibt `Backdrop.cs` nur ein paar
Shader-Uniforms. Budget: 7 Draw Calls, ≈ 35 000 Dreiecke, keine Schatten.

Die Backdrop-Materialien (`backdrop.gdshader`) sind `unshaded` + `fog_disabled` und berechnen ihren eigenen Dunst
zur Horizontfarbe – Himmel, Nebel und Backdrop nutzen exakt dieselbe Farbe `#BFDDF5`, damit der Horizont nahtlos ist.

## Plattform-Shader

- `platform.gdshader` für Box-Plattformen: Ober- und Seitenfarbe aus der Weltnormale, 1-m-Kacheln auf der Oberseite,
  Rauschen, optionale Warnstreifen, Kantenlinie in konstanter Pixelbreite (aus dem BoxMesh-UV-Atlas), die zwischen
  27 und 90 m ausblendet.
- `platform_shape.gdshader` für alle anderen Formen: gleiche Optik, die Kantenlinie kommt aus dem **UV2-Vertrag** von
  `ShapeMeshFactory` (Flächenränder bei UV2 = 0 oder 1, Deckel als Fächer, runde Mäntel ohne Nähte). Muster werden im
  Objektraum berechnet, drehen sich also mit drehenden Plattformen mit.
- `crate.gdshader`: prozedurale Holzkiste mit Brettern, Rahmen und Diagonalstrebe.

## Farbregeln

| Element | Farbe |
|---|---|
| Brücke | Cyan `#3FE0FF`, halbtransparent, leuchtend |
| Trampolin | Lime `#A6FF2E` auf Schiefer `#34405E` |
| Enterhaken-Anker | Gold `#FFB000`, leuchtet stärker, wenn anvisiert |
| Helikopter-Pickup | Violett |
| Checkpoint-Fahne | Grau, aktiv leuchtend Grün `#2EE66B` |
| Item-Hindernisse | Rot/Orange (nur dafür reserviert) |
| Fehlendes Material | Magenta (Fallback mit einmaliger Warnung) |

## Effekte

`art/fx/crate_break.tscn` (28 Holzsplitter) und `art/fx/item_pickup.tscn` (24 Funken), beide `CPUParticles3D`,
`one_shot`.

Mehr Details stehen im Styleguide `art/STYLE.md`.
