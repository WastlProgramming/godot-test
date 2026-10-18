// JumpRun – procedural wooden crate (planks + frame) for BoxMesh.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx;

uniform vec4 wood_light : source_color = vec4(0.80, 0.55, 0.30, 1.0);
uniform vec4 wood_dark : source_color = vec4(0.55, 0.34, 0.17, 1.0);
uniform vec4 frame_color : source_color = vec4(0.40, 0.24, 0.12, 1.0);
uniform vec4 gap_color : source_color = vec4(0.18, 0.10, 0.05, 1.0);
uniform float plank_count = 4.0;
uniform float frame_width : hint_range(0.0, 0.4) = 0.14;
uniform float grain_scale = 22.0;
uniform vec4 emission_color : source_color = vec4(0.0, 0.0, 0.0, 1.0);
uniform float emission_energy = 0.0;

float hash1(float n) { return fract(sin(n) * 43758.5453); }

void fragment() {
	vec2 fuv = UV * vec2(3.0, 2.0);
	vec2 face = floor(fuv);
	vec2 f = fract(fuv);
	float face_id = face.x + face.y * 3.0;

	// Planks run horizontally; stagger tint per plank.
	float plank = floor(f.y * plank_count);
	float pf = fract(f.y * plank_count);
	float tint = hash1(plank * 7.13 + face_id * 3.7);
	vec3 wood = mix(wood_dark.rgb, wood_light.rgb, 0.45 + 0.4 * tint);

	// Wood grain: wavy stripes along the plank.
	float grain = sin((f.x * 1.3 + pf * 0.25 + sin(f.x * 9.0 + plank) * 0.04) * grain_scale + tint * 6.28);
	wood *= 0.92 + 0.08 * grain;

	// Dark gaps between planks.
	float gap = 1.0 - smoothstep(0.0, 0.06, min(pf, 1.0 - pf));
	wood = mix(wood, gap_color.rgb, gap * 0.8);

	// Frame around each face + diagonal brace.
	float border = min(min(f.x, 1.0 - f.x), min(f.y, 1.0 - f.y));
	float frame = 1.0 - step(frame_width, border);
	float diag = 1.0 - step(frame_width * 0.5, abs(f.x - f.y) * 0.7071);
	float brace = max(frame, diag);
	vec3 col = mix(wood, frame_color.rgb, brace);
	// Thin dark outline at the very edge.
	col *= 1.0 - (1.0 - smoothstep(0.0, 0.02, border)) * 0.5;

	ALBEDO = col;
	ROUGHNESS = 0.9;
	SPECULAR = 0.2;
	EMISSION = emission_color.rgb * emission_energy;
}
