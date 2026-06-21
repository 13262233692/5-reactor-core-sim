using System;
using Godot;

namespace ReactorCoreSim.Scripts.Rendering
{
    public static class ThermalColorMapper
    {
        public static Color JetColormap(float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            float r, g, b;

            if (t < 0.125f)
            {
                r = 0f;
                g = 0f;
                b = 0.5f + 4f * t;
            }
            else if (t < 0.375f)
            {
                r = 0f;
                g = 4f * (t - 0.125f);
                b = 1f;
            }
            else if (t < 0.625f)
            {
                r = 4f * (t - 0.375f);
                g = 1f;
                b = 1f - 4f * (t - 0.375f);
            }
            else if (t < 0.875f)
            {
                r = 1f;
                g = 1f - 4f * (t - 0.625f);
                b = 0f;
            }
            else
            {
                r = 1f - 4f * (t - 0.875f);
                g = 0f;
                b = 0f;
            }

            return new Color(r, g, b, 1f);
        }

        public static Color ThermalColormap(float t)
        {
            t = Math.Clamp(t, 0f, 1f);

            float r = Mathf.SmoothStep(0.2f, 0.8f, t);
            float g = Mathf.SmoothStep(0f, 0.5f, t) * (1f - Mathf.SmoothStep(0.7f, 1f, t));
            float b = (1f - Mathf.SmoothStep(0f, 0.5f, t)) * (0.3f + 0.7f * (1f - t));

            return new Color(r, g, b, 1f);
        }

        public static Color InfernoColormap(float t)
        {
            t = Math.Clamp(t, 0f, 1f);

            float r = 0.0015f + 3.8f * t + 1.5f * t * t - 2.0f * t * t * t;
            float g = 0.0f + 2.5f * t * t + 1.5f * t * t * t;
            float b = 0.0f + 3.0f * t * t * t;

            r = Math.Clamp(r, 0f, 1f);
            g = Math.Clamp(g, 0f, 1f);
            b = Math.Clamp(b, 0f, 1f);

            return new Color(r, g, b, 1f);
        }

        public static Color RainbowColormap(float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            float h = t * 5f;
            float r, g, b;

            if (h < 1f)
            {
                r = 1f; g = h; b = 0f;
            }
            else if (h < 2f)
            {
                r = 2f - h; g = 1f; b = 0f;
            }
            else if (h < 3f)
            {
                r = 0f; g = 1f; b = h - 2f;
            }
            else if (h < 4f)
            {
                r = 0f; g = 4f - h; b = 1f;
            }
            else
            {
                r = h - 4f; g = 0f; b = 1f;
            }

            return new Color(r, g, b, 1f);
        }

        public static Color MapTemperature(float temperature, float minTemp, float maxTemp, int mode = 0)
        {
            float normalized = (temperature - minTemp) / (maxTemp - minTemp);
            normalized = Math.Clamp(normalized, 0f, 1f);

            return mode switch
            {
                0 => JetColormap(normalized),
                1 => ThermalColormap(normalized),
                2 => InfernoColormap(normalized),
                _ => RainbowColormap(normalized)
            };
        }

        public static Color MapDnbr(float dnbr, float minDnbr = 0.5f, float maxDnbr = 3.0f)
        {
            float normalized = (dnbr - minDnbr) / (maxDnbr - minDnbr);
            normalized = Math.Clamp(normalized, 0f, 1f);

            Color color;
            if (normalized < 0.33f)
            {
                float t = normalized / 0.33f;
                color = new Color(1f, 0.2f + 0.3f * t, 0.2f, 1f);
            }
            else if (normalized < 0.66f)
            {
                float t = (normalized - 0.33f) / 0.33f;
                color = new Color(1f - 0.3f * t, 0.8f + 0.2f * t, 0.2f, 1f);
            }
            else
            {
                float t = (normalized - 0.66f) / 0.34f;
                color = new Color(0.2f + 0.4f * t, 1f - 0.2f * t, 0.4f + 0.3f * t, 1f);
            }

            return color;
        }
    }
}
