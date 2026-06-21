shader_type canvas_item;

uniform float min_temp : hint_range(20.0, 400.0) = 200.0;
uniform float max_temp : hint_range(20.0, 1000.0) = 600.0;
uniform float brightness : hint_range(0.0, 2.0) = 1.0;
uniform float gamma : hint_range(0.1, 3.0) = 1.0;

uniform int color_mode : hint_range(0, 3) = 0;

varying vec2 uv;

vec3 jet_colormap(float t) {
    t = clamp(t, 0.0, 1.0);
    vec3 c = vec3(0.0);
    
    if (t < 0.125) {
        c = vec3(0.0, 0.0, 0.5 + 4.0 * t);
    } else if (t < 0.375) {
        c = vec3(0.0, 4.0 * (t - 0.125), 1.0);
    } else if (t < 0.625) {
        c = vec3(4.0 * (t - 0.375), 1.0, 1.0 - 4.0 * (t - 0.375));
    } else if (t < 0.875) {
        c = vec3(1.0, 1.0 - 4.0 * (t - 0.625), 0.0);
    } else {
        c = vec3(1.0 - 4.0 * (t - 0.875), 0.0, 0.0);
    }
    
    return c;
}

vec3 thermal_colormap(float t) {
    t = clamp(t, 0.0, 1.0);
    vec3 c;
    
    c.r = smoothstep(0.2, 0.8, t);
    c.g = smoothstep(0.0, 0.5, t) * (1.0 - smoothstep(0.7, 1.0, t));
    c.b = 1.0 - smoothstep(0.0, 0.5, t);
    c.b *= 0.3 + 0.7 * (1.0 - t);
    
    return c;
}

vec3 inferno_colormap(float t) {
    t = clamp(t, 0.0, 1.0);
    vec3 c;
    
    c.r = 0.0015 + 3.8 * t + 1.5 * t * t - 2.0 * t * t * t;
    c.g = 0.0 + 2.5 * t * t + 1.5 * t * t * t;
    c.b = 0.0 + 3.0 * t * t * t;
    
    c.r = clamp(c.r, 0.0, 1.0);
    c.g = clamp(c.g, 0.0, 1.0);
    c.b = clamp(c.b, 0.0, 1.0);
    
    return c;
}

vec3 rainbow_colormap(float t) {
    t = clamp(t, 0.0, 1.0);
    float h = t * 5.0;
    vec3 c;
    
    if (h < 1.0) {
        c = vec3(1.0, h, 0.0);
    } else if (h < 2.0) {
        c = vec3(2.0 - h, 1.0, 0.0);
    } else if (h < 3.0) {
        c = vec3(0.0, 1.0, h - 2.0);
    } else if (h < 4.0) {
        c = vec3(0.0, 4.0 - h, 1.0);
    } else {
        c = vec3(h - 4.0, 0.0, 1.0);
    }
    
    return c;
}

void fragment() {
    vec4 tex_color = texture(TEXTURE, UV);
    float temp_value = tex_color.r;
    
    float normalized = (temp_value - min_temp) / (max_temp - min_temp);
    normalized = clamp(normalized, 0.0, 1.0);
    
    normalized = pow(normalized, gamma);
    
    vec3 color;
    if (color_mode == 0) {
        color = jet_colormap(normalized);
    } else if (color_mode == 1) {
        color = thermal_colormap(normalized);
    } else if (color_mode == 2) {
        color = inferno_colormap(normalized);
    } else {
        color = rainbow_colormap(normalized);
    }
    
    color *= brightness;
    
    float alpha = tex_color.a;
    if (tex_color.r < min_temp * 0.1 && tex_color.g < 0.01) {
        alpha = 0.0;
    }
    
    COLOR = vec4(color, alpha);
}
