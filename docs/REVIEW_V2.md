# Review V2 – independent code review

Scope: `scripts/**`, `scenes/**`, `art/**`, `tools/**`, `project.godot`, `website/` (VitePress). Checked against the 8 user requirements and against Godot 4.6 / C# best practice. `dotnet build` passes with 0 warnings and 0 errors.

## Requirement coverage

| # | Requirement | Status | Evidence |
|---|---|---|---|
| 1 | VitePress docs | **Partial** | `website/` has `package.json` (vitepress 1.6.3), `config.mts` and `.gdignore`. Only 3 of 10 linked pages exist (see V2-01). |
| 2 | Arms and legs animated | OK | `CharacterRig.cs`: blended Run/Jump/Fall/Fly/Grapple poses. Stride phase follows distance travelled, so feet don't slide. The grapple arm points at the anchor. Squash/stretch on jump and land. All 4 models have ArmL/ArmR/LegL/LegR pivots. |
| 3 | Unlimited items | OK | `Inventory.cs`: the only cap is the overflow guard `InventoryHardCap = 999 999`. The HUD prints any number. `MaxPlaceables = 3` limits only how many objects are placed at the same time, not how many you carry. |
| 4 | 4 characters | OK | `CharacterCatalog.cs` (Flasche, Mülleimer, Superheld, Ritter). Each has its own model: trash-can lid, cape, sword and shield. The choice is saved, and the select screen has a 3D turntable. All 4 play the same (cosmetic only). |
| 5 | Helicopter, short flight | OK | 3.5 s flight, at most 14 m climb, slow sink, fuel bar, rotor mesh, start sound and loop sound. Can be chained (V2-06). |
| 6 | Music and SFX | OK | Autoload `AudioManager`: music cross-fade, 12+8 voice pool, 17 SFX, bus layout with a limiter, volume settings that are saved. Loop keeps playing during pause (V2-02). |
| 7 | Better background | OK | Sky shader (gradient, sun, fbm clouds), 3 mountain rings, floating islands and a cloud sea (MultiMesh, GPU wrap), motes, depth fog, tonemap and glow. |
| 8 | Varied platform shapes | OK | 11 shapes: Box, Square, Diamond, Cylinder, Disc, Triangle, Pentagon, Hexagon, Octagon, Ramp, Cross. Collision matches each shape: Box, rotated Box, CylinderShape3D, Convex hull, 2-box cross. Spinning platforms are added. Gaps are measured on the walkable inner circle. |

## Findings

| id | sev | file:line | issue | fix |
|---|---|---|---|---|
| V2-01 | **High** | `website/.vitepress/config.mts:19-50` | Nav and sidebar link to `spiel/welt`, `spiel/plattformen`, `spiel/items`, `spiel/charaktere`, `technik/architektur`, `technik/audio`, `technik/grafik`, `technik/tests` and `entwicklung/mitwirken`. None of these files exist (only `index.md`, `guide/installation.md` and `guide/steuerung.md` do). `vitepress build` fails on dead links by default, so requirement 1 is not met yet. | Write the missing pages, or remove their links. Then run `npm ci && npm run docs:build` to confirm. |
| V2-02 | Medium | `scripts/Audio/AudioManager.cs:122,279` | The `AudioManager` autoload is `ProcessMode.Always`, and the loop players inherit that. So `helicopter_loop` keeps buzzing while the pause menu is open: the flight is frozen but the sound is not. | Create the loop players with `ProcessMode = Pausable`, or pause or mute loops when `GetTree().Paused` changes. One-shot UI SFX can stay Always. |
| V2-03 | Medium | `scripts/Items/TrampolineItem.cs:210` | Only `MovingPlatform` is refused. A trampoline placed on a `RotatingPlatform` (spinning triangle or hexagon) goes under `Placeables` in world space. The platform then turns away underneath it, so it floats or sits inside the mesh. | Refuse `RotatingPlatform` too, or parent the placeable to the platform body. |
| V2-04 | Medium (perf) | `art/env/world_environment.tres` (ssao_enabled), `art/env/Lighting.tscn` (directional_shadow_mode=2, blend_splits), `project.godot` (no renderer setting) | Forward+ only, with 4-split PSSM, blended splits, SSAO, glow and a SubViewport preview. There is no quality option, which is heavy on an integrated GPU. The shaders do support Compatibility, but the project never uses it. | Add a graphics-quality setting (low = 2 splits, SSAO off, shadow distance 60). Optionally set `rendering/renderer/rendering_method="gl_compatibility"` or provide a launch flag. Note that SSAO is ignored in Compatibility. |
| V2-05 | Low-Med | `project.godot` | No `physics/3d/physics_engine` is set, and the file is hand-written, so the engine default applies. With GodotPhysics3D, a CharacterBody3D on CylinderShape3D or ConvexPolygonShape3D (disc, pillar and polygon platforms) is known to jitter or snap at the edges. | Set `physics/3d/physics_engine="Jolt Physics"` explicitly, then re-run the shape collision tests. |
| V2-06 | Low | `scripts/Player/PlayerController.cs:291,385-387` | The flight ceiling is reset from the current Y on every flight. Pressing 4 to stop and 4 to start again chains flights. Because the inventory is unlimited, N charges give 14·N m of climb, and the player can skip chunks or rise above `MaxWorldY`. The flight is short per charge only. | Add a cooldown, base the ceiling on the last ground Y, or clamp it to `MaxWorldY`. |
| V2-07 | Low | `scripts/Player/PlayerController.cs:191` | `AnimMode` stays `Fly` while the player stands on the ground during a flight. Arms stay up and legs dangle while walking. | Use the ground pose (Run/Idle) when `_flying && IsOnFloor()`, or end the flight after a short time on the ground. |
| V2-08 | Low | `scripts/Player/PlayerController.cs:295` | The "Nachtanken" (refuel) branch in `StartFlight` can never run, because `HelicopterItem.TryUse` returns false while flying. | Remove it, or document refuelling as intended. |
| V2-09 | Low | `scripts/Characters/CharacterRig.cs:111` | `_time` wraps at 1000 s. The sine waves (breathing, cape flutter, fall paddling) don't have periods that divide 1000, so there is a visible pop every ~16 min. | Keep `_time` as a `double`, or wrap each oscillator's phase with `Tau`. |
| V2-10 | Low (perf) | `art/shaders/sky.gdshader:1-4` | The comment claims "zero per-frame cost". Only the radiance map is cached. The background sky pass runs every frame for every pixel (4-octave fbm, acos, pow). | Render the clouds in `AT_HALF_RES_PASS` or `AT_QUARTER_RES_PASS`, and correct the comment. |
| V2-11 | Low | `scripts/Game/GameManager.cs:31`, `scripts/World/Backdrop.cs:119` | `OriginOffsetZ` is a `float`. Backdrop's "double precision" modulo starts from a value that has already lost precision, and score distance has the same issue. This only matters on very long runs (more than about 100 km). | Store `OriginOffsetZ` as a `double`. |
| V2-12 | Low (alloc) | `scripts/Items/Pickup.cs:61`, `scripts/Player/PlayerController.cs:141-143` | Every pickup calls `GetOverlappingBodies()` every 0.25 s, which allocates a Godot Array each time. `IsOnMovingPlatform()` allocates a `KinematicCollision3D` per slide collision, and runs every physics frame while \|Z\| > 1000 and the player is on a moving platform. | Poll pickups only for about 0.5 s after they spawn. In the platform check, use `GetPlatformVelocity()` or `GetLastSlideCollision()` once, or throttle it. |
| V2-13 | Low | `scripts/World/ChunkBuilder.cs:783` | When even `zGap = 0` gives a walkable gap ≥ `gap`, the platform is accepted as it is. The real gap can then exceed `MaxSafeGap`, and the large value is only recorded in `NormalGaps`. | Move X toward the previous platform or enlarge the shape until `WalkableGap ≤ MaxSafeGap`. |
| V2-14 | Info | `scenes/Player.tscn`, `CharacterCatalog.cs` | The characters are purely cosmetic. The collision capsule does not match `ModelHeight` (1.72–1.96 m), so the rotor at `ModelTopHeight` can clip through low ceilings. | Optional: small per-character perks, and size the capsule to the model. |
| V2-15 | Info | repo root | The git repo exists on `main` but has **0 commits** and no remote, so it cannot be pushed yet. | `git add -A && git commit -m "JumpRun v2"`, then `git remote add origin … && git push -u origin main`. |
| V2-16 | Info | `art/audio/music_main.wav` | A single ~36 s loop is used for both the menu and the game, so it gets repetitive. | Optional: a second track for the menu (cross-fading already exists via `PlayMusic`). |

## Positive notes
- Low-allocation hot paths: reused ray and shape queries, `stackalloc` in pattern choice, StringName caches, HUD text set only on change, pooled audio voices.
- Shared unit meshes and hull caches per shape/material/size, and the caches are cleared on exit.
- Lifecycle: signals are connected and disconnected in pairs (`_EnterTree`/`_ExitTree`, HUD, AudioEventBridge). Deferred spawns check that the parent is still valid. The origin shift is held back while the player is on moving or rotating platforms.
- The backdrop uses 7 draw calls, is unshaded, casts no shadows, and wraps on the GPU, so it works in both Compatibility and Forward+.

## Fix-Status (fix round v2)

Re-verified: `dotnet build` 0 warnings / 0 errors, headless `--import` clean, all 14 `tests/*.tscn` exit 0
(`TesterV2Test` `pass=34 known=0 fail=0`, also with `-- --strict`), `npm run docs:build` passes.

| id | status | resolution / reason |
|---|---|---|
| V2-01 | fixed (before this round) | All 10 pages linked in `config.mts` exist. `npm run docs:build` passes with the dead-link check enabled. |
| V2-02 | fixed | Loop players are created with `ProcessMode = Pausable`. `IsLoopActive` reports paused loops as inactive. Music and UI SFX keep playing (`AudioManager` stays `Always`). |
| V2-03 | fixed | `TrampolineItem.TryUse` refuses `RotatingPlatform` as well. |
| V2-04 | fixed | New graphics-quality setting: pause-menu toggle "Grafik: niedrig (schneller)", saved as `graphics_low` in `settings.json`, CLI `-- --quality=low\|high` (not persisted). Low = SSAO off, PSSM 2 splits, no split blending, shadow distance 60 m (`Main.ApplyGraphicsQuality`). Without a saved value it defaults to low on `gl_compatibility`/`mobile` and high on Forward+. The renderer is **not** switched project-wide: Windows keeps Forward+ (per project requirement) and `run-linux.sh` already forces OpenGL. |
| V2-05 | fixed | `physics/3d/physics_engine="Jolt Physics"` set explicitly. All collision, solvability, long-run and helicopter tests pass under Jolt. |
| V2-06 | fixed | The flight ceiling uses `min(startY, lastGroundY + 4 m) + 14 m`, so chained air starts are bounded (re-test: 18 m instead of 562 m). Flying over pads also pulls the checkpoint along, which keeps alive chunks ≤ 12. |
| V2-07 | fixed | `AnimMode` falls back to Run/Idle once the player has been on the ground for more than 0.3 s during a flight. The first 0.3 s keep the Fly pose for lift-off. |
| V2-08 | fixed | The dead refuel branch was removed. `StartFlight` returns early while already flying. |
| V2-09 | fixed | `_time` wraps at 20π s. All oscillator frequencies are multiples of 0.1 rad/s, so the wrap is seamless. |
| V2-10 | partially fixed (comment) | The comment now states the real per-pixel cost. Half/quarter-res passes were declined because the Compatibility renderer does not support them, and the shader is cheap at 720p. |
| V2-11 | fixed | `GameManager` keeps the offset as a `double` (`OriginOffsetZPrecise`). Distance and backdrop scroll use it. The `float OriginOffsetZ` getter stays for API compatibility. Distance is clamped before the `long` cast. |
| V2-12 | partially fixed | Pickups poll overlaps only for the first 1 s after spawning, then disable `_PhysicsProcess`. The `IsOnMovingPlatform` loop was declined: it only runs while \|Z\| > 1000 m **and** the player is standing on a moving platform, which is a few frames per shift. Replacing it with `GetPlatformVelocity()` would miss platforms at their turning point, which are exactly what the guard is for. |
| V2-13 | fixed | `ChunkBuilder.Ctx.Add`: if the walkable gap is still > `MaxSafeGap` at `zGap = 0`, X is bisected toward the previous platform until the gap is safe again. The 18 000-chunk geometry test is still issue-free. |
| V2-14 | declined | Characters are cosmetic by design (CONTRACT_V2 §5). One shared capsule keeps all four characters' gameplay and test results identical. The rotor is a visual only and has no collision. |
| V2-15 | open (by design) | The repo is on `main` with `.gitignore`. Per the user request the user pushes the repo themselves; the initial commit is left to the final integration step. README → "Git" has the commands. |
| V2-16 | declined | Optional content. One ~36 s synthesized loop is within scope. Cross-fading already supports a second track if one is added to `tools/gen_audio.py` later. |
