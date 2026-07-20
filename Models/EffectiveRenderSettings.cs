using System;
using BarsGraphics.Config;
using UnityEngine;

namespace BarsGraphics.Models
{
    internal sealed class EffectiveRenderSettings
    {
        public string ProfileName { get; private set; } = "Off";
        public bool UseRenderScale { get; private set; }
        public float RenderScale { get; private set; } = 1f;
        public bool UseFsrUpscaling { get; private set; }
        public float FsrRenderScale { get; private set; } = 1f;
        public float FsrSharpness { get; private set; } = 0.8f;
        public bool UseShadowSettings { get; private set; }
        public float ShadowDistance { get; private set; }
        public int ShadowCascades { get; private set; }
        public ShadowResolution ShadowResolution { get; private set; } = ShadowResolution.Medium;
        public bool UseLodSettings { get; private set; }
        public float LodBias { get; private set; } = 1f;
        public int MaximumLodLevel { get; private set; }
        public bool UseShaderMaximumLod { get; private set; }
        public int ShaderMaximumLod { get; private set; } = 1000;
        public bool ProtectNearLods { get; private set; }
        public float NearLodProtectionDistance { get; private set; }
        public bool DisablePostProcessing { get; private set; }
        public bool DisableRealtimeReflectionProbes { get; private set; }
        public bool DisableReflectionProbes { get; private set; }
        public bool UseFrameRateSettings { get; private set; }
        public int VSyncCount { get; private set; }
        public int TargetFrameRate { get; private set; } = -1;
        public bool UsePixelLightCount { get; private set; }
        public int PixelLightCount { get; private set; }
        public bool UseAntiAliasing { get; private set; }
        public int AntiAliasing { get; private set; }
        public bool UseGlobalTextureMipmapLimit { get; private set; }
        public int GlobalTextureMipmapLimit { get; private set; }
        public bool UseAnisotropicFiltering { get; private set; }
        public AnisotropicFiltering AnisotropicFilteringMode { get; private set; } = AnisotropicFiltering.Enable;
        public bool UseAdditionalLightsMode { get; private set; }
        public string AdditionalLightsMode { get; private set; } = string.Empty;
        public int? MaxAdditionalLightsCount { get; private set; }
        public bool DisableFarLightShadows { get; private set; }
        public float FarLightShadowDistance { get; private set; }
        public bool UseCameraFarClip { get; private set; }
        public float CameraFarClipDistance { get; private set; }
        public bool DisableCameraStacks { get; private set; }
        public bool UseLayerCullDistances { get; private set; }
        public float LayerCullDistance { get; private set; }
        public bool DisableCameraOcclusionCulling { get; private set; }
        public bool DisableVolumetricLightBeams { get; private set; }
        public bool UseVisibilitySafeRendererCulling { get; private set; }
        public float RendererCullMinDistance { get; private set; }
        public bool DisableTerrainFoliage { get; private set; }
        public float TerrainDetailObjectDistance { get; private set; }
        public bool DisableOutlineFeature { get; private set; }

        public static EffectiveRenderSettings FromProfile(ModConfig config, string profile)
        {
            string normalized = OptimizationProfileCatalog.Normalize(profile);

            if (string.Equals(normalized, "Custom", StringComparison.OrdinalIgnoreCase))
            {
                return FromCustom(config, normalized);
            }

            EffectiveRenderSettings settings = new EffectiveRenderSettings
            {
                ProfileName = normalized
            };

            switch (normalized)
            {
                case "Conservative":
                    settings.UseFsrUpscaling = true;
                    settings.FsrRenderScale = 0.9f;
                    settings.FsrSharpness = 0.85f;
                    settings.UseShadowSettings = true;
                    settings.ShadowDistance = 45f;
                    settings.ShadowCascades = 1;
                    settings.ShadowResolution = ShadowResolution.Low;
                    settings.UseShaderMaximumLod = true;
                    settings.ShaderMaximumLod = 500;
                    settings.UseAnisotropicFiltering = true;
                    settings.AnisotropicFilteringMode = AnisotropicFiltering.Enable;
                    break;
                case "Balanced":
                    settings.UseFsrUpscaling = true;
                    settings.FsrRenderScale = 0.8f;
                    settings.FsrSharpness = 0.8f;
                    settings.UseShadowSettings = true;
                    settings.ShadowDistance = 10f;
                    settings.ShadowCascades = 1;
                    settings.ShadowResolution = ShadowResolution.Low;
                    settings.UseLodSettings = true;
                    settings.LodBias = 0.5f;
                    settings.MaximumLodLevel = 0;
                    settings.UseShaderMaximumLod = true;
                    settings.ShaderMaximumLod = 400;
                    settings.UseAnisotropicFiltering = true;
                    settings.AnisotropicFilteringMode = AnisotropicFiltering.Enable;
                    settings.DisableOutlineFeature = true;
                    break;
                case "Aggressive":
                    settings.UseFsrUpscaling = true;
                    settings.FsrRenderScale = 0.75f;
                    settings.FsrSharpness = 0.8f;
                    settings.UseShadowSettings = true;
                    settings.ShadowDistance = 0f;
                    settings.ShadowCascades = 0;
                    settings.ShadowResolution = ShadowResolution.Low;
                    settings.UseLodSettings = true;
                    settings.LodBias = 0.45f;
                    settings.MaximumLodLevel = 0;
                    settings.UseShaderMaximumLod = true;
                    settings.ShaderMaximumLod = 350;
                    settings.UseAnisotropicFiltering = true;
                    settings.AnisotropicFilteringMode = AnisotropicFiltering.Enable;
                    settings.DisablePostProcessing = true;
                    settings.UseGlobalTextureMipmapLimit = true;
                    settings.GlobalTextureMipmapLimit = 1;
                    settings.UseAdditionalLightsMode = true;
                    settings.AdditionalLightsMode = "Disabled";
                    settings.MaxAdditionalLightsCount = 0;
                    settings.DisableTerrainFoliage = true;
                    settings.TerrainDetailObjectDistance = 0f;
                    settings.DisableOutlineFeature = true;
                    break;
            }

            return settings;
        }

        public bool IsOff()
        {
            return string.Equals(ProfileName, "Off", StringComparison.OrdinalIgnoreCase);
        }

        private static EffectiveRenderSettings FromCustom(ModConfig config, string normalized)
        {
            return new EffectiveRenderSettings
            {
                ProfileName = normalized,
                UseRenderScale = config.UseRenderScale?.Value ?? false,
                RenderScale = Clamp(config.RenderScale?.Value ?? 1f, 0.35f, 1f),
                UseFsrUpscaling = config.UseFsrUpscaling?.Value ?? false,
                FsrRenderScale = Clamp(config.FsrRenderScale?.Value ?? 0.8f, 0.5f, 1f),
                FsrSharpness = Clamp(config.FsrSharpness?.Value ?? 0.8f, 0f, 1f),
                UseShadowSettings = config.UseShadowSettings?.Value ?? false,
                ShadowDistance = Math.Max(0f, config.ShadowDistance?.Value ?? QualitySettings.shadowDistance),
                ShadowCascades = Math.Max(0, config.ShadowCascades?.Value ?? QualitySettings.shadowCascades),
                ShadowResolution = ToShadowResolution(config.ShadowResolution?.Value ?? 1),
                UseLodSettings = config.UseLodSettings?.Value ?? false,
                LodBias = Clamp(config.LodBias?.Value ?? 1f, 0.25f, 2f),
                MaximumLodLevel = Math.Max(0, config.MaximumLodLevel?.Value ?? 0),
                UseShaderMaximumLod = config.UseShaderMaximumLod?.Value ?? false,
                ShaderMaximumLod = ClampInt(config.ShaderMaximumLod?.Value ?? 400, 100, 1000),
                ProtectNearLods = config.ProtectNearLods?.Value ?? false,
                NearLodProtectionDistance = Math.Max(1f, config.NearLodProtectionDistance?.Value ?? 25f),
                DisablePostProcessing = config.DisablePostProcessing?.Value ?? false,
                DisableRealtimeReflectionProbes = config.DisableRealtimeReflectionProbes?.Value ?? false,
                DisableReflectionProbes = config.DisableReflectionProbes?.Value ?? false,
                UseFrameRateSettings = config.UseFrameRateSettings?.Value ?? false,
                VSyncCount = Math.Max(0, config.VSyncCount?.Value ?? QualitySettings.vSyncCount),
                TargetFrameRate = Math.Max(-1, config.TargetFrameRate?.Value ?? Application.targetFrameRate),
                UsePixelLightCount = config.UsePixelLightCount?.Value ?? false,
                PixelLightCount = Math.Max(0, config.PixelLightCount?.Value ?? QualitySettings.pixelLightCount),
                UseAntiAliasing = config.UseAntiAliasing?.Value ?? false,
                AntiAliasing = Math.Max(0, config.AntiAliasing?.Value ?? QualitySettings.antiAliasing),
                UseGlobalTextureMipmapLimit = config.UseGlobalTextureMipmapLimit?.Value ?? false,
                GlobalTextureMipmapLimit = ClampInt(config.GlobalTextureMipmapLimit?.Value ?? QualitySettings.globalTextureMipmapLimit, 0, 3),
                UseAnisotropicFiltering = config.UseAnisotropicFiltering?.Value ?? false,
                AnisotropicFilteringMode = ToAnisotropicFiltering(config.AnisotropicFilteringMode?.Value ?? 1),
                UseAdditionalLightsMode = config.UseAdditionalLightsMode?.Value ?? false,
                AdditionalLightsMode = config.AdditionalLightsMode?.Value ?? string.Empty,
                MaxAdditionalLightsCount = Math.Max(0, config.MaxAdditionalLightsCount?.Value ?? 0),
                DisableFarLightShadows = config.DisableFarLightShadows?.Value ?? false,
                FarLightShadowDistance = Math.Max(0f, config.FarLightShadowDistance?.Value ?? 0f),
                UseCameraFarClip = config.UseCameraFarClip?.Value ?? false,
                CameraFarClipDistance = Math.Max(20f, config.CameraFarClipDistance?.Value ?? 220f),
                DisableCameraStacks = config.DisableCameraStacks?.Value ?? false,
                UseLayerCullDistances = config.UseLayerCullDistances?.Value ?? false,
                LayerCullDistance = Math.Max(20f, config.LayerCullDistance?.Value ?? 180f),
                DisableCameraOcclusionCulling = config.DisableCameraOcclusionCulling?.Value ?? false,
                DisableVolumetricLightBeams = config.DisableVolumetricLightBeams?.Value ?? false,
                UseVisibilitySafeRendererCulling = config.UseVisibilitySafeRendererCulling?.Value ?? false,
                RendererCullMinDistance = Math.Max(5f, config.RendererCullMinDistance?.Value ?? 35f),
                DisableTerrainFoliage = config.DisableTerrainFoliage?.Value ?? false,
                TerrainDetailObjectDistance = Clamp(config.TerrainDetailObjectDistance?.Value ?? 0f, 0f, 150f),
                DisableOutlineFeature = config.DisableOutlineFeature?.Value ?? false
            };
        }

        private static ShadowResolution ToShadowResolution(int value)
        {
            switch (value)
            {
                case 0:
                    return ShadowResolution.Low;
                case 2:
                    return ShadowResolution.High;
                case 3:
                    return ShadowResolution.VeryHigh;
                default:
                    return ShadowResolution.Medium;
            }
        }

        private static AnisotropicFiltering ToAnisotropicFiltering(int value)
        {
            switch (value)
            {
                case 0:
                    return AnisotropicFiltering.Disable;
                case 2:
                    return AnisotropicFiltering.ForceEnable;
                default:
                    return AnisotropicFiltering.Enable;
            }
        }

        private static float Clamp(float value, float min, float max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        private static int ClampInt(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }
    }
}


