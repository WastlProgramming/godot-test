// JumpRun – distant scenery (mountain rings, floating islands, cloud sea).
// Unshaded + fog_disabled: the objects sit far behind the depth fog, so they compute their own lighting
// (flat lambert from `sun_dir`) and their own aerial haze towards `horizon_color` (== sky horizon == fog colour).
// Albedo comes from the vertex COLOR of the procedural meshes (Backdrop*.cs).
// Optional GPU wrapping for MultiMesh instances (`wrap_period` > 0): each instance origin is repeated on a grid
// relative to the camera's *absolute* XZ (`scroll`, already reduced modulo the period on the CPU), so the elements
// are world-stable, scroll past with parallax and survive Floating-Origin shifts without a jump.
// Works in Compatibility (GL3) and Forward+; no textures, no screen/depth reads.
shader_type spatial;
render_mode unshaded, fog_disabled, cull_back, depth_draw_opaque, world_vertex_coords, shadows_disabled;

uniform vec3 tint : source_color = vec3(1.0);
uniform vec3 horizon_color : source_color = vec3(0.749, 0.867, 0.961);
uniform vec3 sun_dir = vec3(0.289, 0.788, 0.544);   // towards the sun (Backdrop.cs copies it from Lighting/Sun)
uniform vec3 sun_color : source_color = vec3(1.0, 0.925, 0.8);
uniform vec3 shade_color : source_color = vec3(0.52, 0.6, 0.78);
uniform float sun_strength : hint_range(0.0, 2.0) = 0.62;
uniform float wrap_light : hint_range(0.0, 1.0) = 0.25;    // soft "half lambert"
uniform float haze_begin = 150.0;
uniform float haze_end = 1100.0;
uniform float haze_max : hint_range(0.0, 1.0) = 0.6;
uniform float haze_curve : hint_range(0.2, 4.0) = 1.0;   // >1 = haze builds up late (clear mid-distance)
// Extra haze for low parts (relative to the backdrop anchor height) -> silhouettes dissolve at their base.
uniform float low_haze_top = 0.0;
uniform float low_haze_bottom = -120.0;
uniform float low_haze_strength : hint_range(0.0, 1.0) = 0.0;

uniform vec2 wrap_period = vec2(0.0);
uniform vec2 scroll = vec2(0.0);
uniform vec3 anchor = vec3(0.0);

varying vec3 v_world;
varying vec3 v_normal;
varying vec3 v_color;

float wrap1(float x, float period) {
	return x - period * floor(x / period + 0.5);
}

void vertex() {
	// world_vertex_coords: VERTEX/NORMAL are already in world space here.
	if (wrap_period.x > 0.0 || wrap_period.y > 0.0) {
		vec2 rel = MODEL_MATRIX[3].xz - anchor.xz;
		vec2 want = rel;
		if (wrap_period.x > 0.0) want.x = wrap1(rel.x - scroll.x, wrap_period.x);
		if (wrap_period.y > 0.0) want.y = wrap1(rel.y - scroll.y, wrap_period.y);
		VERTEX.xz += want - rel;
	}
	v_world = VERTEX;
	v_normal = NORMAL;
	v_color = COLOR.rgb;
}

void fragment() {
	vec3 n = normalize(v_normal);
	float ndl = dot(n, normalize(sun_dir));
	ndl = clamp((ndl + wrap_light) / (1.0 + wrap_light), 0.0, 1.0);
	vec3 base = v_color * tint;
	vec3 lit = base * (shade_color + sun_color * ndl * sun_strength);

	float dist = length(v_world - CAMERA_POSITION_WORLD);
	float h = clamp((dist - haze_begin) / max(haze_end - haze_begin, 1.0), 0.0, 1.0);
	h = pow(h, haze_curve);
	float low = 1.0 - smoothstep(low_haze_bottom, low_haze_top, v_world.y - anchor.y);
	float haze = clamp(max(h * haze_max, low * low_haze_strength), 0.0, 1.0);
	// beyond haze_end everything is fully hazed (used to hide wrap-around pops)
	haze = max(haze, step(haze_end, dist) * step(0.999, haze_max));

	ALBEDO = mix(lit, horizon_color, haze);
}
