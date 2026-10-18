// JumpRun – stylised sky (v2).
// Gradient (zenith -> sky -> horizon -> void below), sun disc + halo from LIGHT0, soft static fbm clouds.
// Deliberately does NOT use TIME/POSITION: the radiance map is computed once (Sky.process_mode = Quality),
// so ambient light/reflections cost nothing per frame. The visible background pass still runs per pixel every
// frame (4-octave fbm + sun); this is a few ALU ops per pixel and cheap at 720p. Half/quarter-res passes are not
// used because the Compatibility renderer does not support them. Works in Compatibility + Forward+.
shader_type sky;

uniform vec3 zenith_color : source_color = vec3(0.184, 0.435, 0.839);
uniform vec3 sky_color : source_color = vec3(0.435, 0.651, 0.910);
uniform vec3 horizon_color : source_color = vec3(0.749, 0.867, 0.961); // == fog_light_color == backdrop horizon
uniform vec3 ground_color : source_color = vec3(0.561, 0.706, 0.878);  // void below the clouds
uniform float horizon_band : hint_range(0.01, 0.6) = 0.16;  // elevation (sin) where horizon -> sky is done
uniform float sky_curve : hint_range(0.1, 3.0) = 0.8;
uniform float ground_band : hint_range(0.05, 1.0) = 0.5;   // sin(depression) where the void reaches ground_color

uniform float sun_disc_radius_deg : hint_range(0.1, 8.0) = 2.2;
uniform float sun_disc_energy = 9.0;
uniform float halo_strength : hint_range(0.0, 2.0) = 0.32;
uniform float halo_power = 7.0;
uniform float glare_strength : hint_range(0.0, 2.0) = 0.55;

uniform vec3 cloud_color : source_color = vec3(1.0, 0.988, 0.965);
uniform vec3 cloud_shade_color : source_color = vec3(0.655, 0.737, 0.875);
uniform float cloud_coverage : hint_range(0.0, 1.0) = 0.54;
uniform float cloud_softness : hint_range(0.01, 0.5) = 0.2;
uniform float cloud_opacity : hint_range(0.0, 1.0) = 0.88;
uniform float cloud_scale = 1.35;
uniform vec2 cloud_offset = vec2(3.7, 11.2);

float hash12(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * 0.1031);
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.x + p3.y) * p3.z);
}

float vnoise(vec2 x) {
	vec2 i = floor(x);
	vec2 f = fract(x);
	f = f * f * (3.0 - 2.0 * f);
	float a = hash12(i);
	float b = hash12(i + vec2(1.0, 0.0));
	float c = hash12(i + vec2(0.0, 1.0));
	float d = hash12(i + vec2(1.0, 1.0));
	return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

float fbm(vec2 p) {
	float v = 0.0;
	float a = 0.5;
	mat2 rot = mat2(vec2(0.8, 0.6), vec2(-0.6, 0.8));
	for (int i = 0; i < 4; i++) {
		v += a * vnoise(p);
		p = rot * p * 2.03 + vec2(1.7, 9.2);
		a *= 0.5;
	}
	return v;
}

void sky() {
	vec3 d = normalize(EYEDIR);
	float e = d.y;

	vec3 col;
	if (e >= 0.0) {
		float t1 = clamp(e / horizon_band, 0.0, 1.0);
		t1 = t1 * t1 * (3.0 - 2.0 * t1);
		float t2 = pow(clamp((e - horizon_band) / (1.0 - horizon_band), 0.0, 1.0), sky_curve);
		col = mix(horizon_color, sky_color, t1);
		col = mix(col, zenith_color, t2);
	} else {
		float g = clamp(-e / ground_band, 0.0, 1.0);
		g = g * g * (3.0 - 2.0 * g);
		col = mix(horizon_color, ground_color, g);
	}

	vec3 sun_add = vec3(0.0);
	float disc = 0.0;
	vec3 sun_col = vec3(1.0);
	float sun_facing = 0.0;
	if (LIGHT0_ENABLED) {
		vec3 L = normalize(LIGHT0_DIRECTION);
		float c = dot(d, L);
		sun_facing = max(c, 0.0);
		sun_col = LIGHT0_COLOR * LIGHT0_ENERGY;
		float ang = acos(clamp(c, -1.0, 1.0));
		float r = max(radians(sun_disc_radius_deg), LIGHT0_SIZE);
		disc = 1.0 - smoothstep(r * 0.8, r, ang);
		disc *= smoothstep(-0.02, 0.01, e); // sun never shows below the horizon
		sun_add = sun_col * (pow(sun_facing, halo_power) * halo_strength + pow(sun_facing, 90.0) * glare_strength);
		// keep the horizon band itself unchanged so distant fog matches the sky exactly
		sun_add *= smoothstep(0.0, horizon_band, e) * 0.85 + 0.15;
	}
	col += sun_add;
	col = mix(col, sun_col * sun_disc_energy, disc);

	if (e > 0.0) {
		vec2 uv = d.xz / (e + 0.14) * cloud_scale + cloud_offset;
		float n = fbm(uv);
		float cov = smoothstep(cloud_coverage, cloud_coverage + cloud_softness, n);
		float fade = smoothstep(0.015, 0.22, e);
		float a = cov * fade * cloud_opacity;
		// thicker parts are shaded, thin rims catch the sun
		float thick = smoothstep(cloud_coverage + 0.05, cloud_coverage + 0.38, n);
		vec3 cc = mix(cloud_color, cloud_shade_color, thick * 0.75);
		cc += sun_col * (pow(sun_facing, 6.0) * 0.35 * (1.0 - thick));
		// distant clouds dissolve into the horizon haze
		cc = mix(horizon_color, cc, smoothstep(0.0, 0.3, e));
		col = mix(col, cc, a);
	}

	COLOR = col;
}
