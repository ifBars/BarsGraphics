using System.Collections.Generic;

namespace BarsGraphics.Models
{
    internal enum OptimizationImpactConfidence
    {
        Unknown,
        SingleSample,
        Repeated,
        Rejected
    }

    internal enum OptimizationVisualRisk
    {
        None,
        Low,
        Medium,
        High
    }

    internal sealed class OptimizationImpact
    {
        public OptimizationImpact(
            string id,
            string label,
            string category,
            float? measuredFpsDelta,
            float? measuredTotalFps,
            string measurementContext,
            OptimizationImpactConfidence confidence,
            OptimizationVisualRisk visualRisk,
            bool recommended,
            string qualityNotes,
            string measurementNotes)
        {
            Id = id;
            Label = label;
            Category = category;
            MeasuredFpsDelta = measuredFpsDelta;
            MeasuredTotalFps = measuredTotalFps;
            MeasurementContext = measurementContext;
            Confidence = confidence;
            VisualRisk = visualRisk;
            Recommended = recommended;
            QualityNotes = qualityNotes;
            MeasurementNotes = measurementNotes;
        }

        public string Id { get; }
        public string Label { get; }
        public string Category { get; }
        public float? MeasuredFpsDelta { get; }
        public float? MeasuredTotalFps { get; }
        public string MeasurementContext { get; }
        public OptimizationImpactConfidence Confidence { get; }
        public OptimizationVisualRisk VisualRisk { get; }
        public bool Recommended { get; }
        public string QualityNotes { get; }
        public string MeasurementNotes { get; }
    }

    internal static class OptimizationImpactCatalog
    {
        public const float TownhallBaselineFps = 83.99f;

        public static IReadOnlyList<OptimizationImpact> Items { get; } = new List<OptimizationImpact>
        {
            new OptimizationImpact(
                "profile.conservative",
                "Conservative profile",
                "Profile",
                null,
                null,
                "Estimated from retained low-risk levers; not yet measured as a standalone 30s profile",
                OptimizationImpactConfidence.Unknown,
                OptimizationVisualRisk.Low,
                true,
                "Keeps LOD, interaction outlines, and camera stacks intact. Mostly reduces shadow and URP overhead.",
                "Expected to be safer than Balanced but weaker. Needs a fresh profile sample before claiming a measured FPS delta."),

            new OptimizationImpact(
                "profile.balanced",
                "Balanced profile",
                "Profile",
                24.99f,
                108.98f,
                "Townhall, 04:00 clear, 30s rerun vs 83.99 FPS baseline",
                OptimizationImpactConfidence.SingleSample,
                OptimizationVisualRisk.Low,
                true,
                "Multi-location screenshots preserved nearby buildings and HUD. Distant LOD is reduced but playable.",
                "Also measured Motel 130.98 FPS and Docks 114.48 FPS in same rerun; cross-location baselines differ, so only Townhall delta is comparable."),

            new OptimizationImpact(
                "profile.aggressive",
                "Aggressive profile",
                "Profile",
                51.54f,
                135.53f,
                "Townhall, 04:00 clear, heavy explicit stack vs 83.99 FPS baseline",
                OptimizationImpactConfidence.SingleSample,
                OptimizationVisualRisk.Medium,
                true,
                "Nearby geometry and HUD stayed visible, but broader checks showed sky/large-environment quality degradation. Not a default-safe profile.",
                "This profile intentionally avoids unsafe global layer cull. Treat as opt-in for low-end hardware and profiling."),

            new OptimizationImpact(
                "lod.bias.0_45",
                "LOD bias 0.45",
                "LOD",
                11.3f,
                147.12f,
                "Townhall, 04:00 clear, 30s repeat on top of current heavy stack",
                OptimizationImpactConfidence.Repeated,
                OptimizationVisualRisk.High,
                false,
                "Townhall was playable, but Motel showed obvious sky/large-environment LOD degradation. Keep this aggressive-only.",
                "Measured 147.02 FPS then 147.12 FPS; delta is incremental vs the 135.82 FPS heavy stack, not vs Off."),

            new OptimizationImpact(
                "lod.bias.0_40",
                "LOD bias 0.40",
                "LOD",
                12.75f,
                148.57f,
                "Townhall, 04:00 clear, 30s single sample on top of current heavy stack",
                OptimizationImpactConfidence.SingleSample,
                OptimizationVisualRisk.High,
                false,
                "Townhall screenshot was playable, but aggressive LOD is already too close to unplayable in broader testing.",
                "Only about 1.5 FPS above LOD 0.45 and higher visual risk."),

            new OptimizationImpact(
                "renderer_feature.outline_off",
                "URP outline feature off",
                "Renderer Feature",
                2.08f,
                131.36f,
                "Townhall, 04:00 clear, 60s on top of current heavy stack",
                OptimizationImpactConfidence.SingleSample,
                OptimizationVisualRisk.Low,
                true,
                "No screenshot regression observed, but may reduce interactable/readability affordances. Needs gameplay interaction validation.",
                "Small gain; keep as a low-impact toggle rather than a headline optimization."),

            new OptimizationImpact(
                "renderer_feature.decals_off",
                "Decal renderer feature off",
                "Renderer Feature",
                2.67f,
                146.62f,
                "Townhall, 04:00 clear, 30s repeat on top of heavy aggressive stack",
                OptimizationImpactConfidence.Repeated,
                OptimizationVisualRisk.Low,
                false,
                "Townhall screenshot looked fine. Risk is loss of surface decals/detail, and it did not help the safer Balanced stack.",
                "Measured 147.98 FPS then 146.62 FPS. Treat as aggressive-only until multi-location quality is reviewed."),

            new OptimizationImpact(
                "textures.global_mipmap_limit_2",
                "Texture mipmap limit 2",
                "Textures",
                1.5f,
                104.65f,
                "Townhall, Balanced profile, 20s x3 A/B repeat vs 103.15 FPS at limit 0",
                OptimizationImpactConfidence.SingleSample,
                OptimizationVisualRisk.Medium,
                false,
                "Usually helps VRAM and texture bandwidth more than FPS. Higher values make world, item, and sign textures blurrier.",
                "Retain as advanced/custom and mild Aggressive-only tuning. Do not use in Balanced until close-up readability is validated."),

            new OptimizationImpact(
                "render_scale.0_8",
                "Render scale 0.8",
                "Resolution",
                0.61f,
                136.43f,
                "Townhall, 04:00 clear, 30s on top of current heavy stack",
                OptimizationImpactConfidence.Rejected,
                OptimizationVisualRisk.Medium,
                false,
                "Adds visible softness without meaningful FPS gain.",
                "Rejected."),

            new OptimizationImpact(
                "render_scale.0_65",
                "Render scale 0.65",
                "Resolution",
                0.2f,
                136.02f,
                "Townhall, 04:00 clear, 30s on top of current heavy stack",
                OptimizationImpactConfidence.Rejected,
                OptimizationVisualRisk.High,
                false,
                "Major softness with no meaningful FPS gain in this scene.",
                "Rejected."),

            new OptimizationImpact(
                "renderer.layer_cull_50",
                "Global layer cull 50m",
                "Culling",
                15.95f,
                151.77f,
                "Townhall, 04:00 clear, 30s on top of LOD 0.40",
                OptimizationImpactConfidence.SingleSample,
                OptimizationVisualRisk.High,
                false,
                "Fast in one Townhall shot, but global layer cull has already caused hidden-building failures.",
                "Do not enable by default; requires strict multi-location screenshot validation."),

            new OptimizationImpact(
                "renderer.visibility_safe_culling",
                "Visibility-safe renderer culling",
                "Culling",
                -128.95f,
                18.17f,
                "Townhall, 04:00 clear, 30s on top of LOD 0.45",
                OptimizationImpactConfidence.Rejected,
                OptimizationVisualRisk.High,
                false,
                "Severe FPS collapse from per-frame renderer bookkeeping/re-enable churn.",
                "Rejected."),

            new OptimizationImpact(
                "renderer.layer22_far_disable",
                "Layer 22 far renderer disable",
                "Culling",
                -5.29f,
                140.53f,
                "Townhall, 04:00 clear, 30s on top of heavy aggressive stack",
                OptimizationImpactConfidence.Rejected,
                OptimizationVisualRisk.High,
                false,
                "Disabled 608 distant foliage/planter renderers and still regressed.",
                "Rejected."),

            new OptimizationImpact(
                "renderer_feature.liquid_depth_off",
                "Liquid Volume Depth PrePass off",
                "Renderer Feature",
                -11.35f,
                132.6f,
                "Townhall, 04:00 clear, 30s on top of heavy aggressive stack",
                OptimizationImpactConfidence.Rejected,
                OptimizationVisualRisk.Medium,
                false,
                "Likely risks liquid/water depth interactions and regressed FPS.",
                "Rejected."),

            new OptimizationImpact(
                "lights.far_disable_25",
                "Far light disabling at 25m",
                "Lighting",
                -7f,
                141.58f,
                "Townhall, 04:00 clear, 30s on top of LOD 0.40",
                OptimizationImpactConfidence.Rejected,
                OptimizationVisualRisk.Medium,
                false,
                "Darkens distant street lighting and regressed FPS.",
                "Rejected."),

            new OptimizationImpact(
                "water.stylized_water_off",
                "Stylized Water 2 off",
                "Renderer Feature",
                7.43f,
                136.71f,
                "Townhall, 04:00 clear, 60s on top of current heavy stack",
                OptimizationImpactConfidence.SingleSample,
                OptimizationVisualRisk.High,
                false,
                "Can remove water/map-edge affordances, including water that teleports players back to the map.",
                "Likely rejected unless a safer water-specific alternative is found.")
        };
    }
}


