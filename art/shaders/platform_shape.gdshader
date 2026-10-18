// JumpRun – platform shader for the non-box shapes (Square/Diamond/Cylinder/Disc/Triangle/Pentagon/Hexagon/
// Octagon/Ramp/Cross) built by ShapeMeshFactory. Same look and uniforms as platform.gdshader, but:
//  * Edge outline from the UV2 contract (CONTRACT_V2 §4.3): face borders lie on UV2.x ∈ {0,1} or UV2.y ∈ {0,1}.
//    Cap fans have a constant UV2.x (= 0.5) -> only their outer rim (UV2.y = 0) is an edge, never the fan centre.
//    Round side walls (constant UV2.x) and very narrow side quads get no vertical seams, only top/bottom edges.
//    Width is constant in pixels for every size/scale (fwidth).
//  * Tiles/noise/stripes in object space scaled by the node scale -> patterns rotate with spinning platforms and do
//    not swim; 1 m tiles on caps regardless of platform size.
//  * Top/side split by the world normal (normal·up > 0.5 = top, also for ramps).
// Works in Compatibility (GL3) and Forward+.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx;

uniform vec4 top_color : source_color = vec4(0.31, 0.66, 0.87, 1.0);
uniform vec4 side_color : source_color = vec4(0.23, 0.47, 0.71, 1.0);
uniform vec4 edge_color : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float edge_width_px : hint_range(0.0, 8.0) = 2.0;
uniform float edge_fade_distance = 90.0;
uniform float tile_contrast : hint_range(0.0, 0.3) = 0.05;
uniform float tile_size = 1.0;
uniform float noise_strength : hint_range(0.0, 0.3) = 0.06;
uniform float stripe_strength : hint_range(0.0, 1.0) = 0.0;
uniform vec4 stripe_color : source_color = vec4(0.12, 0.08, 0.06, 1.0);
uniform float stripe_scale = 1.2;
uniform vec4 emission_color : source_color = vec4(0.0, 0.0, 0.0, 1.0);
uniform float emission_energy = 0.0;
uniform float edge_emission = 0.0;
uniform float pulse_speed = 0.0;
uniform float roughness_value : hint_range(0.0, 1.0) = 0.85;
// Side quads narrower than this (in unit-mesh units, i.e. fraction of the platform diameter) get no vertical seam.
uniform float seam_min_width = 0.2;
// Side faces darken towards the bottom (fake ambient occlusion / depth).
uniform float side_bottom_shade : hint_range(0.0, 0.6) = 0.22;
// Lighter lip just below the top rim on side faces.
uniform float side_lip_strength : hint_range(0.0, 0.5) = 0.12;

varying vec3 world_pos;
varying vec3 world_normal;
varying vec3 obj_pos;
varying float obj_h;

float hash3(vec3 p) {
	p = fract(p * 0.3183099 + vec3(0.1, 0.2, 0.3));
	p *= 17.0;
	return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float value_noise(vec3 x) {
	vec3 i = floor(x);
	vec3 f = fract(x);
	f = f * f * (3.0 - 2.0 * f);
	return mix(
		mix(mix(hash3(i + vec3(0.0, 0.0, 0.0)), hash3(i + vec3(1.0, 0.0, 0.0)), f.x),
			mix(hash3(i + vec3(0.0, 1.0, 0.0)), hash3(i + vec3(1.0, 1.0, 0.0)), f.x), f.y),
		mix(mix(hash3(i + vec3(0.0, 0.0, 1.0)), hash3(i + vec3(1.0, 0.0, 1.0)), f.x),
			mix(hash3(i + vec3(0.0, 1.0, 1.0)), hash3(i + vec3(1.0, 1.0, 1.0)), f.x), f.y),
		f.z);
}

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	world_normal = normalize(MODEL_NORMAL_MATRIX * NORMAL);
	vec3 sc = vec3(length(MODEL_MATRIX[0].xyz), length(MODEL_MATRIX[1].xyz), length(MODEL_MATRIX[2].xyz));
	obj_pos = VERTEX * sc;
	obj_h = clamp(VERTEX.y + 0.5, 0.0, 1.0);
}

void fragment() {
	vec3 n = normalize(world_normal);
	float up = smoothstep(0.45, 0.6, n.y);
	vec3 base = mix(side_color.rgb, top_color.rgb, up);

	// Tiles on top faces (object space, rotate with the platform).
	vec2 tile = floor(obj_pos.xz / max(tile_size, 0.05));
	float checker = mod(tile.x + tile.y, 2.0);
	base *= 1.0 - checker * tile_contrast * up;

	// Low-frequency colour variation.
	float nz = value_noise(obj_pos * 0.9) * 0.6 + value_noise(obj_pos * 3.1) * 0.4;
	base *= 1.0 + (nz - 0.5) * 2.0 * noise_strength;

	// Side faces: darker towards the bottom, light lip below the top rim, optional hazard stripes.
	float side = 1.0 - up;
	base *= 1.0 - side * side_bottom_shade * (1.0 - obj_h);
	base *= 1.0 + side * side_lip_strength * smoothstep(0.82, 1.0, obj_h);
	float stripe = step(0.5, fract((obj_pos.x + obj_pos.y + obj_pos.z) / max(stripe_scale, 0.01)));
	base = mix(base, stripe_color.rgb, stripe * stripe_strength * side);

	// Edge outline (UV2 contract). All derivatives are taken outside of any branch.
	vec2 uv2 = UV2;
	vec2 fw_raw = fwidth(uv2);
	vec2 fw = max(fw_raw, vec2(1e-6));
	vec2 dpx = min(uv2, vec2(1.0) - uv2) / fw;          // pixel distance to the 4 borders
	float face_w = fwidth(UV.x) / fw.x;                  // quad width in mesh units (UV is planar in mesh units)
	float const_x = step(fw_raw.x, 1e-6);                // UV2.x constant: cap fan or smooth round side
	float is_fan = const_x * step(0.5, abs(n.y));        // cap fans: only the rim (UV2.y = 0) counts
	float seam_ok = step(seam_min_width, face_w) * (1.0 - const_x);
	float px_x = mix(1e6, dpx.x, seam_ok);
	float px_y = mix(dpx.y, uv2.y / fw.y, is_fan);
	float px = min(px_x, px_y);
	// Fallback for meshes without UV2 (e.g. a plain BoxMesh): BoxMesh 3x2 UV atlas like platform.gdshader.
	vec2 fuv = UV * vec2(3.0, 2.0);
	vec2 ff = fract(fuv);
	vec2 bpx = min(ff, vec2(1.0) - ff) / max(fwidth(fuv), vec2(1e-5));
	float no_uv2 = 1.0 - step(1e-6, fw_raw.x + fw_raw.y + uv2.x + uv2.y);
	px = mix(px, min(bpx.x, bpx.y), no_uv2);
	float edge = 1.0 - smoothstep(edge_width_px, edge_width_px + 1.0, px);
	float dist = length(world_pos - CAMERA_POSITION_WORLD);
	edge *= 1.0 - smoothstep(edge_fade_distance * 0.3, edge_fade_distance, dist) * 0.7;
	edge *= edge_color.a;

	float pulse = pulse_speed > 0.0 ? 0.65 + 0.35 * sin(TIME * pulse_speed) : 1.0;

	ALBEDO = mix(base, edge_color.rgb, edge);
	ROUGHNESS = roughness_value;
	SPECULAR = 0.3;
	EMISSION = emission_color.rgb * emission_energy * pulse + edge_color.rgb * edge * edge_emission * pulse;
}
