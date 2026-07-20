using System;

namespace BarsGraphics.Models
{
    internal sealed class OptimizationProfileDefinition
    {
        public OptimizationProfileDefinition(
            string id,
            string label,
            string performanceEffect,
            string qualityImpact,
            string notes)
        {
            Id = id;
            Label = label;
            PerformanceEffect = performanceEffect;
            QualityImpact = qualityImpact;
            Notes = notes;
        }

        public string Id { get; }
        public string Label { get; }
        public string PerformanceEffect { get; }
        public string QualityImpact { get; }
        public string Notes { get; }
    }

    internal static class OptimizationProfileCatalog
    {
        public static readonly string[] RuntimeProfileIds =
        {
            "Off",
            "Conservative",
            "Balanced",
            "Aggressive",
            "Custom"
        };

        public static readonly OptimizationProfileDefinition[] Profiles =
        {
            new OptimizationProfileDefinition(
                "Off",
                "Off",
                "Baseline",
                "No quality loss",
                "Restores captured render settings when the optimizer is active."),

            new OptimizationProfileDefinition(
                "Conservative",
                "Conservative",
                "Light render-cost reduction",
                "Very low visual impact",
                "Keeps LOD and outlines intact. Uses mild FSR upscaling and reduces shadow work."),

            new OptimizationProfileDefinition(
                "Balanced",
                "Balanced",
                "Moderate render-cost reduction",
                "Low visual impact",
                "Uses FSR upscaling plus measured shadow, LOD, shader LOD, anisotropic filtering, and URP feature cuts while preserving nearby gameplay readability."),

            new OptimizationProfileDefinition(
                "Aggressive",
                "Aggressive",
                "High render-cost reduction",
                "Medium visual impact",
                "Adds stronger FSR scaling and terrain foliage removal to the heavy render stack. Nearby geometry should remain playable, but trees/grass, distant scenes, lighting, and textures can change visibly."),

            new OptimizationProfileDefinition(
                "Custom",
                "Custom",
                "Depends on selected options",
                "Depends on selected options",
                "Manual tuning profile. Sliders and toggles below save immediately and are applied by the optimizer loop.")
        };

        public static string Normalize(string profile)
        {
            if (string.IsNullOrWhiteSpace(profile))
            {
                return "Off";
            }

            string trimmed = profile.Trim();
            foreach (string knownProfile in RuntimeProfileIds)
            {
                if (string.Equals(trimmed, knownProfile, StringComparison.OrdinalIgnoreCase))
                {
                    return knownProfile;
                }
            }

            return "Off";
        }

        public static OptimizationProfileDefinition Get(string profile)
        {
            string normalized = Normalize(profile);
            foreach (OptimizationProfileDefinition definition in Profiles)
            {
                if (string.Equals(definition.Id, normalized, StringComparison.Ordinal))
                {
                    return definition;
                }
            }

            return Profiles[0];
        }
    }
}


