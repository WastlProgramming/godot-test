# Integration-Notes VISUALS

## 1. `scenes/Main.tscn` (INTEGRATOR) – Backdrop einhängen (Pflicht, §10.2)
- `[ext_resource type="PackedScene" path="res://scenes/Backdrop.tscn" id="8_backdrop"]`
- Knoten **direkt unter `Main`**, nach `Lighting` (nicht unter `WorldRoot`):
  `[node name="Backdrop" parent="." instance=ExtResource("8_backdrop")]`
- Begründung: Backdrop liest `../Lighting/Sun` (Sonnenrichtung/-farbe für die Shader) und folgt der Kamera selbst;
  unter `WorldRoot` würde es vom Floating Origin verschoben. Kein Code in `Main.cs` nötig.
- Optional: dieselbe Szene im Hauptmenü/Charakterauswahl-Hintergrund verwenden (funktioniert mit jeder aktiven Camera3D;
  ohne `../Lighting/Sun` bleibt die Default-Sonnenrichtung aus `backdrop.gdshader`).

## 2. `scenes/Player.tscn` (CHARACTERS) – `Camera3D.far = 1500`
Ist bereits gesetzt (geprüft). Muss ≥ 1300 bleiben, sonst werden die Bergringe (bis ~1220 m) abgeschnitten.

## 3. `art/env/*` geändert – keine Pfadänderung
- `world_environment.tres`: Sky = ShaderMaterial (`art/shaders/sky.gdshader`), `radiance_size` 64, `process_mode` Quality,
  Nebel 80–200 m, Nebelfarbe = Sky-Horizont `#BFDDF5`, `fog_sun_scatter = 0`, `glow_bloom` 0.04 → 0.02.
  Sky-Shader nutzt kein `TIME` → Ambient/Reflections dürfen aus dem Sky kommen (wie v1).
- `Lighting.tscn`: `Sun` wärmer (`#FFECCC`, 1.2), neues `Lighting/Fill` (ohne Schatten, `sky_mode` = Light only).
  `FlowTest` prüft nur `Lighting/Sun` → unverändert grün.
- Wer Farben ändert: `horizon_color` in sky-Material, `fog_light_color` und `horizon_color` in `backdrop.gdshader`/
  `backdrop_*.tres` müssen identisch bleiben (`tests/VisualsTest` prüft das).

## 4. Neue Materialien (§7.2) – alle vorhanden
`platform_pillars|spin|shapes.tres` (platform.gdshader), `platform_{gap,stairs,zigzag,moving,height,pillars,spin,shapes}_shape.tres`
(platform_shape.gdshader), `pickup_helicopter.tres`, `helicopter_rotor.tres`, `helicopter_hub.tres`,
Backdrop: `backdrop_mountains_far|mid|near.tres`, `backdrop_islands.tres`, `backdrop_clouds.tres`, `backdrop_motes.tres`.
`platform_shape.gdshader` ist auf `ShapeMeshFactory` abgestimmt (Fächer-Deckel UV2.x = 0.5 → nur Außenrand;
runde Mäntel UV2.x = 0.5 → keine senkrechten Nähte). Meshes ohne UV2 (z. B. BoxMesh) fallen auf die BoxMesh-Atlas-Kante zurück.

## 5. Docs (DOCS) – Inhalte für `technik/grafik.md`
Siehe `art/STYLE.md` (Abschnitte Shaders, Environment, Lighting, Backdrop). Renderer-Regeln: alle Shader ohne
Screen-/Depth-Texture, Backdrop unshaded + fog_disabled, CPUParticles statt GPUParticles, 1 schattenwerfendes Licht.

## 6. Verifikation (VISUALS)
- `dotnet build`: 0 Fehler. Alle `tests/*.tscn` Exit 0 (inkl. neuem `tests/VisualsTest.tscn` → `VISUALS_TEST_PASS`:
  Material-Pfade, Sky/Nebel-Regeln, Licht-Regeln, Backdrop-Budget 7 Draw Calls / ~43k Dreiecke, Origin-Shift-Stetigkeit 0 m).
- Gerendert unter Compatibility (`opengl3`, Mesa d3d12) und Forward+ (Vulkan/llvmpipe) mit
  `-- --shot=<png> --view=0..3` (VisualsTest-Vorschaumodus): keine SHADER ERRORs.
  `./run-linux.sh --quit-after 600` und `./run-linux.sh res://scenes/Main.tscn --quit-after 400`: keine Fehler.
