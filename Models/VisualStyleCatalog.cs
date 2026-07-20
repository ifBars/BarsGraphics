using System;
using UnityEngine;

namespace BarsGraphics.Models
{
    internal sealed class VisualStyleDefinition
    {
        public VisualStyleDefinition(
            string id,
            string label,
            string description,
            float postExposure,
            float contrast,
            float hueShift,
            float saturation,
            float temperature,
            float tint,
            Color colorFilter)
        {
            Id = id;
            Label = label;
            Description = description;
            PostExposure = postExposure;
            Contrast = contrast;
            HueShift = hueShift;
            Saturation = saturation;
            Temperature = temperature;
            Tint = tint;
            ColorFilter = colorFilter;
        }

        public string Id { get; }
        public string Label { get; }
        public string Description { get; }
        public float PostExposure { get; }
        public float Contrast { get; }
        public float HueShift { get; }
        public float Saturation { get; }
        public float Temperature { get; }
        public float Tint { get; }
        public Color ColorFilter { get; }
    }

    internal static class VisualStyleCatalog
    {
        public static readonly string[] RuntimeStyleIds =
        {
            "Off",
            "Natural",
            "Vibrant",
            "Warm Film",
            "Cool Film"
        };

        public static readonly VisualStyleDefinition[] Styles =
        {
            new VisualStyleDefinition(
                "Off", "Off",
                "Leaves the game's color grading unchanged.",
                0f, 0f, 0f, 0f, 0f, 0f, Color.white),

            new VisualStyleDefinition(
                "Natural", "Natural",
                "Gently improves clarity and color without strongly changing the game's art direction.",
                0.04f, 5f, 0f, 4f, 2f, 0f, Color.white),

            new VisualStyleDefinition(
                "Vibrant", "Vibrant",
                "Adds punchier color and contrast while keeping exposure close to the original image.",
                0.03f, 8f, 0f, 12f, 3f, 1f, Color.white),

            new VisualStyleDefinition(
                "Warm Film", "Warm Film",
                "Uses warmer highlights, restrained saturation, and deeper contrast for a film-like look.",
                -0.04f, 10f, -1f, -4f, 14f, 3f, new Color(1f, 0.985f, 0.96f, 1f)),

            new VisualStyleDefinition(
                "Cool Film", "Cool Film",
                "Uses cooler color balance and restrained saturation for a moodier image.",
                -0.06f, 11f, 1f, -7f, -16f, -2f, new Color(0.965f, 0.985f, 1f, 1f))
        };

        public static string Normalize(string style)
        {
            if (string.IsNullOrWhiteSpace(style))
            {
                return "Off";
            }

            string trimmed = style.Trim();
            foreach (string knownStyle in RuntimeStyleIds)
            {
                if (string.Equals(trimmed, knownStyle, StringComparison.OrdinalIgnoreCase))
                {
                    return knownStyle;
                }
            }

            return "Off";
        }

        public static VisualStyleDefinition Get(string style)
        {
            string normalized = Normalize(style);
            foreach (VisualStyleDefinition definition in Styles)
            {
                if (string.Equals(definition.Id, normalized, StringComparison.Ordinal))
                {
                    return definition;
                }
            }

            return Styles[0];
        }
    }
}
