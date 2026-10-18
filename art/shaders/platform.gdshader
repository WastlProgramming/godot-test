// JumpRun – stylized low-poly platform shader.
// Works on arbitrarily sized BoxMeshes: colours/noise in world space,
// edge outline from the BoxMesh 3x2 UV atlas with constant pixel width.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx;

uniform vec4 top_color : source_color = vec4(0.31, 0.66, 0.87, 1.0);
uniform vec4 side_color : source_color = vec4(0.23, 0.47, 0.71, 1.0);
uniform vec4 edge_color : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float edge_width_px : hint_range(0.0, 8.0) = 2.0;
uniform float edge_fade_distance = 90.0;
uniform float tile_contrast : hint_range(0.0, 0.3) = 0.05;
uniform float noise_strength : hint_range(0.0, 0.3) = 0.06;
// Diagonal hazard stripes on side faces (item-required sections).
uniform float stripe_strength : hint_range(0.0, 1.0) = 0.0;
uniform vec4 stripe_color : source_color = vec4(0.12, 0.08, 0.06, 1.0);
uniform float stripe_scale = 1.2;
uniform vec4 emission_color : source_color = vec4(0.0, 0.0, 0.0, 1.0);
uniform float emission_energy = 0.0;
uniform float edge_emission = 0.0;
uniform float pulse_speed = 0.0;
uniform float roughness_value : hint_range(0.0, 1.0) = 0.85;

varying vec3 world_pos;
varying vec3 world_normal;

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
	world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

void fragment() {
	vec3 n = normalize(world_normal);
	float up = smoothstep(0.55, 0.9, n.y);
	vec3 base = mix(side_color.rgb, top_color.rgb, up);

	// 1 m world-space tiles on top faces for depth/speed perception.
	vec2 tile = floor(world_pos.xz);
	float checker = mod(tile.x + tile.y, 2.0);
	base *= 1.0 - checker * tile_contrast * up;

	// Low-frequency colour variation.
	float nz = value_noise(world_pos * 0.9) * 0.6 + value_noise(world_pos * 3.1) * 0.4;
	base *= 1.0 + (nz - 0.5) * 2.0 * noise_strength;

	// Hazard stripes on the sides.
	float stripe = step(0.5, fract((world_pos.x + world_pos.y + world_pos.z) / max(stripe_scale, 0.01)));
	base = mix(base, stripe_color.rgb, stripe * stripe_strength * (1.0 - up));

	// Edge outline: BoxMesh lays faces out in a 3 x 2 UV grid.
	vec2 fuv = UV * vec2(3.0, 2.0);
	vec2 f = fract(fuv);
	vec2 d = min(f, vec2(1.0) - f);
	vec2 fw = max(fwidth(fuv), vec2(1e-5));
	vec2 px = d / fw;
	float edge = 1.0 - smoothstep(edge_width_px, edge_width_px + 1.0, min(px.x, px.y));
	float dist = length(world_pos - CAMERA_POSITION_WORLD);
	edge *= 1.0 - smoothstep(edge_fade_distance * 0.3, edge_fade_distance, dist) * 0.7;
	edge *= edge_color.a;

	float pulse = pulse_speed > 0.0 ? 0.65 + 0.35 * sin(TIME * pulse_speed) : 1.0;

	ALBEDO = mix(base, edge_color.rgb, edge);
	ROUGHNESS = roughness_value;
	SPECULAR = 0.3;
	EMISSION = emission_color.rgb * emission_energy * pulse + edge_color.rgb * edge * edge_emission * pulse;
}
