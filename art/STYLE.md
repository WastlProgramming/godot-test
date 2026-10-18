# JumpRun – Art Style Guide

Stylized low-poly look: bright sky, saturated flat colours, platform edges that read clearly. Everything is procedural, with no texture files. Fonts come from `SystemFont` (Nunito/Rubik/Ubuntu/Noto Sans/DejaVu fallback).

## Palette

| Role | Material | Top / main | Side | Edge / accent | Notes |
|---|---|---|---|---|---|
| Start | `platform_start` | `#E8DCC0` sand | `#BFA67C` | `#FFF8E8` | calm, neutral |
| GapJump | `platform_gap` | `#4EA8DE` blue | `#2F6FA8` | `#E6F6FF` | |
| Stairs | `platform_stairs` | `#8E7DDB` lavender | `#5E4DAE` | `#EEE8FF` | |
| ZigZag | `platform_zigzag` | `#2BBFA8` teal | `#1A8475` | `#E0FFF9` | |
| MovingPlatforms | `platform_moving` | `#FFD23F` yellow | `#D69A12` | `#3A2A00` dark, 3 px | pulsing amber emission (3 rad/s), so motion is easy to see |
| NarrowBeams | `platform_beam` | `#FF7AA2` coral pink | `#C94873` | `#FFFFFF` 1.5 px | |
| HeightJump | `platform_height` | `#6CC24A` green | `#468A2E` | `#F0FFE6` | |
| BigGap (item required) | `platform_biggap` | `#FF7A1A` danger orange | `#C2500A` + dark hazard stripes | `#FFE14D` glowing | danger sides |
| TallWall (item required) | `platform_wall` | `#E0393E` red | `#B0242A` + hazard stripes | `#FFE14D` glowing | |
| Supply platform | `platform_supply` | `#DFF6FF` ice | `#9CC7DB` | `#3FE0FF` glowing | |
| Checkpoint pad | `platform_checkpoint` | `#F4F1FF` | `#B9B3D6` | `#7CFFB2` glowing | tiled top |

Item signature colours (used for the pickup, the placed object and the HUD icon):
- Bridge: cyan `#3FE0FF` (`bridge` is 55 % transparent, emissive and double-sided)
- Trampoline: lime `#A6FF2E` pad on a slate `#34405E` base
- Grapple hook: gold `#FFB000`, with an emission of 1.5 that rises to 4.5 on `grapple_anchor_active`. The rope `#FFE7A8` is unshaded, so it reads against any background.

Other objects:
- Crate: procedural wood shader with planks, frame and diagonal brace (`#CC8C4D` / `#8C5729` / `#663D1F`)
- Player (v1, replaced by the characters below): royal blue `#3D5AFE` body with rim light, and a warm white emissive visor `#FFF3D6`
- Checkpoint flag: grey `#9AA3B5` when inactive, emissive green `#2EE66B` when active

UI: dark navy panels `#10182B` (60–88 % alpha), white text with navy outline, gold accent `#FFD84A` for focus, the selected slot, hints and the title, and blue buttons `#3D5AFE`.

v2 additions:

| Role | Material | Top / main | Side | Edge / accent | Notes |
|---|---|---|---|---|---|
| Pillars | `platform_pillars(_shape)` | `#8C9BB0` slate stone | `#5B6A82` | `#EEF3FA` | stronger tiles/noise, reads as stone columns |
| SpinningDiscs | `platform_spin(_shape)` | `#3B6CF0` cobalt | `#2445A8` | `#FFE98A` glowing, 3 px | subtle cobalt pulse (2 rad/s) = "this moves" |
| ShapeMix | `platform_shapes(_shape)` | `#A7B4FF` periwinkle | `#6F7FD6` | `#FFFFFF` | |

Every pattern that can contain non-box shapes has a `*_shape` twin (`gap, stairs, zigzag, moving, height, pillars, spin, shapes`) with the same colours on `platform_shape.gdshader`.

Helicopter signature colour: violet `#9B6BFF` – `pickup_helicopter` (emissive like the other pickups), `helicopter_rotor` (light violet blades `#B99BFF`, slight emission), `helicopter_hub` (dark metallic `#2E3247` with a faint violet glow). Violet is reserved for the helicopter.

Red and orange mean "an item is needed here". No platform type uses these colours unless it needs an item. Magenta `#FF00FF` is not used in the palette, because it is the `MaterialLibrary` fallback.

## Shaders (`art/shaders/`)
- `platform.gdshader`: all `platform_*` materials use it. Top and side colours come from the world normal. It adds 1 m world-space checker tiles on top faces, value noise, optional diagonal hazard stripes on the sides, and an emission pulse. The edge outline is computed from the BoxMesh 3×2 UV atlas and has a constant pixel width, so it works on any box size or scale. Outlines fade out between 27 m and 90 m so they don't dominate distant boxes. **Requires BoxMesh UVs.** On other meshes (CSG, Cylinder) the colours still work, but the outlines will be wrong.
- `platform_shape.gdshader` (v2): same uniforms/look as `platform.gdshader`, for all non-box shapes from `ShapeMeshFactory`. Outline from the UV2 contract (face borders at UV2 = 0/1, cap fans have UV2.x = 0.5 → only the outer rim is an edge, round side walls get no vertical seams), constant pixel width via `fwidth`. Tiles (1 m), noise and hazard stripes are computed in object space × node scale, so they rotate with spinning platforms instead of swimming. Top = world normal·up > 0.5 (ramps included). Side faces darken towards the bottom (`side_bottom_shade`) and get a light lip under the rim. Meshes without UV2 fall back to the BoxMesh atlas outline.
- `crate.gdshader`: procedural crate, also relies on the BoxMesh UV layout.
- `sky.gdshader` (v2, `shader_type sky`): zenith `#2F6FD6` → sky `#6FA6E8` → horizon `#BFDDF5` (= fog colour) → void below `#8FB4E0`; sun disc (2.2°) + halo/glare from `LIGHT0`; static 4-octave fbm clouds that fade out towards the horizon. No `TIME`/`POSITION` → the radiance map (64 px, `process_mode = Quality`) is built once and may stay the ambient/reflection source.
- `backdrop.gdshader` (v2): unshaded + `fog_disabled`, albedo from vertex colours, flat half-lambert from a `sun_dir` uniform (copied from `Lighting/Sun`), own distance haze + "low haze" towards `horizon_color`. Optional GPU wrap of MultiMesh instances around the camera's absolute XZ (`wrap_period`, `scroll`, `anchor`).

## Environment (`art/env/world_environment.tres`)
- Sky: ShaderMaterial with `sky.gdshader` (see above), `radiance_size = 64`, `process_mode = Quality`.
- Depth fog (`fog_mode = 1`): starts at 80 m, ends at 200 m (must stay ≤ 220 m), curve 1.6, colour `#BFDDF5` = sky `horizon_color` = backdrop `horizon_color` (keep all three identical), `fog_sky_affect = 0`, `fog_sun_scatter = 0` (keeps the horizon seamless). Chunks spawn ~240 m ahead (6 × 40 m), so new chunks fade in from fully fogged. Height fog is turned off (`fog_height_density = 0`) because it washed out the near platforms.
- Tonemap AgX (`tonemap_mode = 4`), exposure 1.0, white 6. Glow uses the screen blend, threshold 1.0, bloom 0.02 (was 0.04 – less wash-out in Forward+), so mostly emissive items, anchors and danger edges bloom. SSAO is on. Saturation is set to 1.12 and contrast to 1.05.
- Ambient light: sky, at 75 % contribution.

## Lighting (`art/env/Lighting.tscn`)
`Lighting` (Node3D) contains `Sun` (DirectionalLight3D):
- Pitch −52°, yaw 28°. The light shines forward and down along the run direction (−Z), so the faces the camera sees are lit.
- Colour `#FFECCC` (warmer in v2), energy 1.2
- Shadows: PSSM 4 splits (0.08 / 0.22 / 0.5), blended, `directional_shadow_max_distance = 120`, bias 0.04, normal bias 1.2, blur 1.0
- The sun is drawn by `sky.gdshader` via `LIGHT0` (`sky_mode = 0`, light and sky).
- v2: `Fill` (DirectionalLight3D), cool `#A9C8FF`, energy 0.28, no shadows, no specular, `sky_mode = 1` (light only, so `LIGHT0` in the sky stays the sun). Comes from the left/front and lifts the shadow sides. No further shadow-casting lights.

## Backdrop (`scenes/Backdrop.tscn`, `scripts/World/Backdrop.cs` + `BackdropMeshes.cs`)
Instanced as `Main/Backdrop` (not under `WorldRoot`). Follows the active camera in X/Z, height damped (0.8/s, max 18 m lag), `ProcessPriority = 100` (after the camera). Everything is generated procedurally in `_Ready`, flat-shaded low poly with vertex colours:
- `MountainsFar` / `MountainsMid` / `MountainsNear`: closed silhouette rings at ~1180 / 860 / 560 m around the camera (blue + snow → blue-green → green hills), locked to the camera, bases dissolve in "low haze" below the horizon.
- `IslandsA` / `IslandsB`: 2 × 9 floating islands (grass top, rock cone, low-poly trees) 250–600 m to the sides, world-stable in Z with a 1600 m period, wrapped on the GPU; fully hazed beyond 820 m so the wrap never pops.
- `CloudSea`: 190 low-poly cloud puffs ~78 m below the camera, world-stable on a 1500 m × 1500 m wrap, fully hazed beyond 720 m.
- `Motes`: CPUParticles3D, 48 tiny warm billboards around the camera (global coords → parallax).
Budget (checked by `tests/VisualsTest`): 7 draw calls, ≈ 43k triangles, no shadows, no textures. Per frame only a few uniforms are written (no allocations). Origin shifts are seamless because the wrap uses `absZ = local.Z − GameManager.OriginOffsetZ`. Requires `Camera3D.far ≥ 1300` (Player.tscn: 1500).

## Charaktere (`scripts/Characters/`, `art/characters/*.tres`)

| Charakter | Hauptfarbe | Akzente | Notes |
|---|---|---|---|
| Flasche (`bottle`) | Aqua-PET `#8FE6DE` with rim light | label coral `#FF7AA2`, cap and shoes gold `#E8A317` | |
| Mülleimer (`trashcan`) | green `#45A866` | lid/ribs `#2C7A47`, banana gold | lid lifts on jumps |
| Superheld (`superhero`, default) | suit `#3D5AFE` (the v1 player colour) | cape, belt, emblem and boots gold, mask navy `#161A2B` | |
| Ritter (`knight`) | steel `#C9D1DC` (metallic) | tabard/shield `#2F6FA8`, plume/crossguard gold, glowing eyes `#7FE6FF` | |

Shared materials: `char_eye_white`, `char_eye_dark`, `char_limb`, `char_gold`. Characters never use red, orange or magenta (reserved for item-required sections and the fallback), nor helicopter violet.

## FX (`art/fx/`)
- `crate_break.tscn`: CPUParticles3D, 28 wood chips (BoxMesh), explosive, with gravity 24 so they match the player's physics. `one_shot = true`, `emitting = false`, lifetime 1.2 s.
- `item_pickup.tscn`: CPUParticles3D, 24 emissive billboard sparks, `one_shot = true`, `emitting = false`, lifetime 0.7 s.

## UI (`art/ui/theme.tres`)
Type variations:
- Required by the contract: `ItemSlot`, `ItemSlotSelected` (PanelContainer, gold 4 px border with glow), `HudLabel`, `TitleLabel`
- Extra: `HudBigLabel` (distance), `HintLabel` (gold on a dark pill), `SlotCountLabel`, `SlotKeyLabel`, `SubtitleLabel`

It also styles Button, LineEdit, HSlider and Panel/PanelContainer.

Icons in `art/ui/icons/`: `bridge.svg`, `trampoline.svg`, `grapple.svg`, `crosshair.svg`, plus the extra `grapple_target.svg` (anchor-in-range ring).
