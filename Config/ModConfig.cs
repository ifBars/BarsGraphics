using MelonLoader;
using BarsGraphics.Models;
using BarsGraphics.Utils;

namespace BarsGraphics.Config
{
    public sealed class ModConfig
    {
        private MelonPreferences_Category? _category;

        public MelonPreferences_Entry<bool>? EnableOptimizer { get; private set; }
        public MelonPreferences_Entry<string>? ActiveProfile { get; private set; }
        public MelonPreferences_Entry<bool>? EnableHotkeys { get; private set; }
        public MelonPreferences_Entry<bool>? EnableUiMenu { get; private set; }
        public MelonPreferences_Entry<bool>? LogDiagnostics { get; private set; }
        public MelonPreferences_Entry<string>? VisualStyle { get; private set; }
        public MelonPreferences_Entry<float>? VisualStyleIntensity { get; private set; }
#if BARS_GRAPHICS_DEVELOPMENT
        public MelonPreferences_Entry<bool>? CaptureUiMenuScreenshot { get; private set; }
        public MelonPreferences_Entry<bool>? EnableLiveControl { get; private set; }
        public MelonPreferences_Entry<int>? LiveControlPort { get; private set; }
        public MelonPreferences_Entry<bool>? EnablePerfTestAutomation { get; private set; }
        public MelonPreferences_Entry<bool>? AutoLoadSave { get; private set; }
        public MelonPreferences_Entry<int>? AutoLoadSaveSlot { get; private set; }
        public MelonPreferences_Entry<string>? PerfTestProfileSequence { get; private set; }
        public MelonPreferences_Entry<float>? PerfTestInitialSettleSeconds { get; private set; }
        public MelonPreferences_Entry<float>? PerfTestWarmupSeconds { get; private set; }
        public MelonPreferences_Entry<float>? PerfTestSampleSeconds { get; private set; }
        public MelonPreferences_Entry<bool>? RestoreOffAfterPerfTest { get; private set; }
        public MelonPreferences_Entry<bool>? CaptureProfileScreenshots { get; private set; }
        public MelonPreferences_Entry<bool>? QuitGameAfterPerfTest { get; private set; }
#endif

        public MelonPreferences_Entry<bool>? UseRenderScale { get; private set; }
        public MelonPreferences_Entry<float>? RenderScale { get; private set; }
        public MelonPreferences_Entry<bool>? UseFsrUpscaling { get; private set; }
        public MelonPreferences_Entry<float>? FsrRenderScale { get; private set; }
        public MelonPreferences_Entry<float>? FsrSharpness { get; private set; }

        public MelonPreferences_Entry<bool>? UseShadowSettings { get; private set; }
        public MelonPreferences_Entry<float>? ShadowDistance { get; private set; }
        public MelonPreferences_Entry<int>? ShadowCascades { get; private set; }
        public MelonPreferences_Entry<int>? ShadowResolution { get; private set; }

        public MelonPreferences_Entry<bool>? UseLodSettings { get; private set; }
        public MelonPreferences_Entry<float>? LodBias { get; private set; }
        public MelonPreferences_Entry<int>? MaximumLodLevel { get; private set; }
        public MelonPreferences_Entry<bool>? UseShaderMaximumLod { get; private set; }
        public MelonPreferences_Entry<int>? ShaderMaximumLod { get; private set; }
        public MelonPreferences_Entry<bool>? ProtectNearLods { get; private set; }
        public MelonPreferences_Entry<float>? NearLodProtectionDistance { get; private set; }

        public MelonPreferences_Entry<bool>? DisablePostProcessing { get; private set; }
        public MelonPreferences_Entry<bool>? DisableRealtimeReflectionProbes { get; private set; }
        public MelonPreferences_Entry<bool>? DisableReflectionProbes { get; private set; }
        public MelonPreferences_Entry<bool>? UseFrameRateSettings { get; private set; }
        public MelonPreferences_Entry<int>? VSyncCount { get; private set; }
        public MelonPreferences_Entry<int>? TargetFrameRate { get; private set; }
        public MelonPreferences_Entry<bool>? UsePixelLightCount { get; private set; }
        public MelonPreferences_Entry<int>? PixelLightCount { get; private set; }
        public MelonPreferences_Entry<bool>? UseAntiAliasing { get; private set; }
        public MelonPreferences_Entry<int>? AntiAliasing { get; private set; }
        public MelonPreferences_Entry<bool>? UseGlobalTextureMipmapLimit { get; private set; }
        public MelonPreferences_Entry<int>? GlobalTextureMipmapLimit { get; private set; }
        public MelonPreferences_Entry<bool>? UseAnisotropicFiltering { get; private set; }
        public MelonPreferences_Entry<int>? AnisotropicFilteringMode { get; private set; }

        public MelonPreferences_Entry<bool>? UseAdditionalLightsMode { get; private set; }
        public MelonPreferences_Entry<string>? AdditionalLightsMode { get; private set; }
        public MelonPreferences_Entry<int>? MaxAdditionalLightsCount { get; private set; }
        public MelonPreferences_Entry<bool>? DisableFarLightShadows { get; private set; }
        public MelonPreferences_Entry<float>? FarLightShadowDistance { get; private set; }

        public MelonPreferences_Entry<bool>? UseCameraFarClip { get; private set; }
        public MelonPreferences_Entry<float>? CameraFarClipDistance { get; private set; }
        public MelonPreferences_Entry<bool>? DisableCameraStacks { get; private set; }
        public MelonPreferences_Entry<bool>? UseLayerCullDistances { get; private set; }
        public MelonPreferences_Entry<float>? LayerCullDistance { get; private set; }
        public MelonPreferences_Entry<bool>? DisableCameraOcclusionCulling { get; private set; }
        public MelonPreferences_Entry<bool>? DisableVolumetricLightBeams { get; private set; }
        public MelonPreferences_Entry<bool>? UseVisibilitySafeRendererCulling { get; private set; }
        public MelonPreferences_Entry<float>? RendererCullMinDistance { get; private set; }
        public MelonPreferences_Entry<bool>? DisableTerrainFoliage { get; private set; }
        public MelonPreferences_Entry<float>? TerrainDetailObjectDistance { get; private set; }
        public MelonPreferences_Entry<bool>? DisableOutlineFeature { get; private set; }
#if BARS_GRAPHICS_DEVELOPMENT
        public MelonPreferences_Entry<bool>? EnableInteractionHoverThrottle { get; private set; }
        public MelonPreferences_Entry<float>? InteractionHoverThrottleHz { get; private set; }
        public MelonPreferences_Entry<bool>? EnableWeatherEntityThrottle { get; private set; }
        public MelonPreferences_Entry<float>? WeatherEntityThrottleHz { get; private set; }
#endif

        public void Initialize()
        {
            _category = MelonPreferences.CreateCategory(Constants.PreferencesCategory);

            EnableOptimizer = _category.CreateEntry(nameof(EnableOptimizer), true, "Enable optimizer", "Master switch. F6 toggles this at runtime.");
            ActiveProfile = _category.CreateEntry(nameof(ActiveProfile), "Off", "Active profile", "Off, Conservative, Balanced, Aggressive, or Custom.");
            EnableHotkeys = _category.CreateEntry(nameof(EnableHotkeys), true, "Enable hotkeys", "F5 toggles the UI menu and F6 toggles the optimizer.");
            EnableUiMenu = _category.CreateEntry(nameof(EnableUiMenu), true, "Enable UI menu", "Shows the built-in settings menu when F5 is pressed.");
            LogDiagnostics = _category.CreateEntry(nameof(LogDiagnostics), true, "Log diagnostics", "Logs active render settings and camera/light counts when profiles are applied.");
            VisualStyle = _category.CreateEntry(nameof(VisualStyle), "Off", "Visual style", "Optional color-grading style. Independent from the optimizer profile and off by default.");
            VisualStyleIntensity = _category.CreateEntry(nameof(VisualStyleIntensity), 1f, "Visual style intensity", "Blends the selected visual style from 0 to 1 without adding another render pass.");
#if BARS_GRAPHICS_DEVELOPMENT
            CaptureUiMenuScreenshot = _category.CreateEntry(nameof(CaptureUiMenuScreenshot), false, "Capture UI menu screenshot", "Automation helper: opens the UI menu and captures one screenshot after gameplay loads.");
            EnableLiveControl = _category.CreateEntry(nameof(EnableLiveControl), true, "Enable live control", "Starts a local-only 127.0.0.1 JSONL tuning endpoint for fast FPS/screenshot iteration.");
            LiveControlPort = _category.CreateEntry(nameof(LiveControlPort), 40501, "Live control port", "Local TCP port used by the live tuning endpoint.");
            EnablePerfTestAutomation = _category.CreateEntry(nameof(EnablePerfTestAutomation), false, "Enable performance test automation", "Loads into gameplay, invokes showfps through game code, cycles profiles, and logs native FPS label samples.");
            AutoLoadSave = _category.CreateEntry(nameof(AutoLoadSave), true, "Auto-load save", "Automatically loads a save from the main menu for repeatable FPS testing.");
            AutoLoadSaveSlot = _category.CreateEntry(nameof(AutoLoadSaveSlot), 2, "Auto-load save slot", "1-5 loads that slot. 0 loads the game's LastPlayedGame.");
            PerfTestProfileSequence = _category.CreateEntry(nameof(PerfTestProfileSequence), "Off,Conservative,Balanced,Aggressive", "Performance test profile sequence", "Comma-separated profiles measured in order.");
            PerfTestInitialSettleSeconds = _category.CreateEntry(nameof(PerfTestInitialSettleSeconds), 10f, "Performance test initial settle seconds", "Seconds to wait after gameplay load before the Off baseline profile starts.");
            PerfTestWarmupSeconds = _category.CreateEntry(nameof(PerfTestWarmupSeconds), 5f, "Performance test warmup seconds", "Seconds to wait after each profile switch before sampling FPS.");
            PerfTestSampleSeconds = _category.CreateEntry(nameof(PerfTestSampleSeconds), 60f, "Performance test sample seconds", "Seconds to sample the native HUD FPS label for each profile.");
            RestoreOffAfterPerfTest = _category.CreateEntry(nameof(RestoreOffAfterPerfTest), true, "Restore Off after performance test", "Restores ActiveProfile=Off when the automated sequence completes.");
            CaptureProfileScreenshots = _category.CreateEntry(nameof(CaptureProfileScreenshots), true, "Capture profile screenshots", "Captures one screenshot per measured profile after warmup.");
            QuitGameAfterPerfTest = _category.CreateEntry(nameof(QuitGameAfterPerfTest), true, "Quit game after performance test", "Closes the launched game process after the automated sequence completes.");
#endif

            UseRenderScale = _category.CreateEntry(nameof(UseRenderScale), false, "Use render scale", "Applies RenderScale when ActiveProfile is Custom.");
            RenderScale = _category.CreateEntry(nameof(RenderScale), 0.65f, "Render scale", "Lower internal render resolution. Strong performance lever; visual softness is the trade-off.");
            UseFsrUpscaling = _category.CreateEntry(nameof(UseFsrUpscaling), false, "Use FSR upscaling", "Applies URP FSR upscaling when ActiveProfile is Custom. Preferred over plain render scale for quality.");
            FsrRenderScale = _category.CreateEntry(nameof(FsrRenderScale), 0.8f, "FSR render scale", "Internal render scale used with URP FidelityFX Super Resolution.");
            FsrSharpness = _category.CreateEntry(nameof(FsrSharpness), 0.8f, "FSR sharpness", "URP FSR sharpening amount from 0 to 1.");

            UseShadowSettings = _category.CreateEntry(nameof(UseShadowSettings), false, "Use shadow settings", "Applies shadow distance, cascades, and resolution when ActiveProfile is Custom.");
            ShadowDistance = _category.CreateEntry(nameof(ShadowDistance), 45f, "Shadow distance", "Caps visible realtime shadow distance.");
            ShadowCascades = _category.CreateEntry(nameof(ShadowCascades), 1, "Shadow cascades", "0 disables cascades; 1 or 2 preserves more nearby stability.");
            ShadowResolution = _category.CreateEntry(nameof(ShadowResolution), 1, "Shadow resolution", "0 Low, 1 Medium, 2 High, 3 VeryHigh.");

            UseLodSettings = _category.CreateEntry(nameof(UseLodSettings), false, "Use LOD settings", "Applies LodBias and MaximumLodLevel when ActiveProfile is Custom.");
            LodBias = _category.CreateEntry(nameof(LodBias), 0.5f, "LOD bias", "Lower values select lower-detail LODs sooner.");
            MaximumLodLevel = _category.CreateEntry(nameof(MaximumLodLevel), 0, "Maximum LOD level", "Keep at 0 to avoid skipping to missing or invisible lower LODs.");
            UseShaderMaximumLod = _category.CreateEntry(nameof(UseShaderMaximumLod), false, "Use shader maximum LOD", "Caps shader variant/detail level when ActiveProfile is Custom.");
            ShaderMaximumLod = _category.CreateEntry(nameof(ShaderMaximumLod), 400, "Shader maximum LOD", "Lower values can skip expensive shader paths; too low may visibly simplify materials.");
            ProtectNearLods = _category.CreateEntry(nameof(ProtectNearLods), false, "Protect near LODs", "Forces nearby LODGroups to LOD0 while lower global LOD bias affects farther objects.");
            NearLodProtectionDistance = _category.CreateEntry(nameof(NearLodProtectionDistance), 25f, "Near LOD protection distance", "LODGroups within this distance of the active camera are forced to LOD0.");

            DisablePostProcessing = _category.CreateEntry(nameof(DisablePostProcessing), false, "Disable post processing", "Disables URP camera post processing where accessible.");
            DisableRealtimeReflectionProbes = _category.CreateEntry(nameof(DisableRealtimeReflectionProbes), false, "Disable realtime reflection probes", "Disables QualitySettings realtime reflection probes.");
            DisableReflectionProbes = _category.CreateEntry(nameof(DisableReflectionProbes), false, "Disable reflection probes", "Disables scene reflection probes. More aggressive visual trade-off.");
            UseFrameRateSettings = _category.CreateEntry(nameof(UseFrameRateSettings), false, "Use frame rate settings", "Applies VSyncCount and TargetFrameRate when ActiveProfile is Custom.");
            VSyncCount = _category.CreateEntry(nameof(VSyncCount), 0, "VSync count", "0 disables vsync during measurement; restore returns the previous value.");
            TargetFrameRate = _category.CreateEntry(nameof(TargetFrameRate), -1, "Target frame rate", "-1 leaves Unity uncapped when vsync is disabled.");
            UsePixelLightCount = _category.CreateEntry(nameof(UsePixelLightCount), false, "Use pixel light count", "Applies QualitySettings.pixelLightCount when ActiveProfile is Custom.");
            PixelLightCount = _category.CreateEntry(nameof(PixelLightCount), 0, "Pixel light count", "Caps per-pixel lights for render cost testing.");
            UseAntiAliasing = _category.CreateEntry(nameof(UseAntiAliasing), false, "Use anti-aliasing", "Applies QualitySettings.antiAliasing when ActiveProfile is Custom.");
            AntiAliasing = _category.CreateEntry(nameof(AntiAliasing), 0, "Anti-aliasing", "0 disables quality-level MSAA where the active renderer honors it.");
            UseGlobalTextureMipmapLimit = _category.CreateEntry(nameof(UseGlobalTextureMipmapLimit), false, "Use texture mipmap limit", "Applies QualitySettings.globalTextureMipmapLimit when ActiveProfile is Custom.");
            GlobalTextureMipmapLimit = _category.CreateEntry(nameof(GlobalTextureMipmapLimit), 1, "Texture mipmap limit", "0 Full, 1 Half, 2 Quarter, 3 Eighth texture resolution. Usually saves VRAM more than FPS.");
            UseAnisotropicFiltering = _category.CreateEntry(nameof(UseAnisotropicFiltering), false, "Use anisotropic filtering", "Applies QualitySettings.anisotropicFiltering when ActiveProfile is Custom.");
            AnisotropicFilteringMode = _category.CreateEntry(nameof(AnisotropicFilteringMode), 1, "Anisotropic filtering mode", "0 Disable, 1 Enable, 2 ForceEnable.");

            UseAdditionalLightsMode = _category.CreateEntry(nameof(UseAdditionalLightsMode), false, "Use additional lights mode", "Changes URP additional light rendering mode when ActiveProfile is Custom.");
            AdditionalLightsMode = _category.CreateEntry(nameof(AdditionalLightsMode), "PerVertex", "Additional lights mode", "URP mode: PerVertex or Disabled are the intended performance values.");
            MaxAdditionalLightsCount = _category.CreateEntry(nameof(MaxAdditionalLightsCount), 2, "Max additional lights", "Caps URP additional lights when the property exists.");
            DisableFarLightShadows = _category.CreateEntry(nameof(DisableFarLightShadows), false, "Disable far light shadows", "Removes non-directional shadows beyond FarLightShadowDistance. Disabled by default because IL2CPP light mutation can be unstable.");
            FarLightShadowDistance = _category.CreateEntry(nameof(FarLightShadowDistance), 35f, "Far light shadow distance", "Non-directional lights farther than this from the main camera lose shadows.");

            UseCameraFarClip = _category.CreateEntry(nameof(UseCameraFarClip), false, "Use camera far clip", "Caps camera far clip plane when ActiveProfile is Custom.");
            CameraFarClipDistance = _category.CreateEntry(nameof(CameraFarClipDistance), 220f, "Camera far clip distance", "Lower values reduce distant rendering but can visibly cut skyline/world.");
            DisableCameraStacks = _category.CreateEntry(nameof(DisableCameraStacks), false, "Disable camera stacks", "Clears URP camera stacks. Manual diagnostic only because it can hide the player's phone UI.");
            UseLayerCullDistances = _category.CreateEntry(nameof(UseLayerCullDistances), false, "Use layer cull distances", "Caps layer cull distances for all camera layers. Aggressive.");
            LayerCullDistance = _category.CreateEntry(nameof(LayerCullDistance), 180f, "Layer cull distance", "Layer-level distance cap when UseLayerCullDistances is enabled.");
            DisableCameraOcclusionCulling = _category.CreateEntry(nameof(DisableCameraOcclusionCulling), false, "Disable camera occlusion culling", "Turns off Camera.useOcclusionCulling. Useful to test whether Unity culling CPU cost outweighs occlusion wins.");
            DisableVolumetricLightBeams = _category.CreateEntry(nameof(DisableVolumetricLightBeams), false, "Disable volumetric light beams", "Disables VLB behaviours. Trace-backed test for VLB.OnWillRenderObject cost.");
            UseVisibilitySafeRendererCulling = _category.CreateEntry(nameof(UseVisibilitySafeRendererCulling), false, "Use visibility-safe renderer culling", "Disables only renderers outside the active camera frustum and re-enables them before they become visible.");
            RendererCullMinDistance = _category.CreateEntry(nameof(RendererCullMinDistance), 35f, "Renderer cull min distance", "Never disables renderers closer than this distance to the active camera.");
            DisableTerrainFoliage = _category.CreateEntry(nameof(DisableTerrainFoliage), false, "Disable terrain foliage", "Turns off terrain tree/grass rendering. Aggressive visual trade-off.");
            TerrainDetailObjectDistance = _category.CreateEntry(nameof(TerrainDetailObjectDistance), 0f, "Terrain detail distance", "Terrain detail draw distance while terrain foliage is disabled.");
            DisableOutlineFeature = _category.CreateEntry(nameof(DisableOutlineFeature), true, "Disable outline feature", "Disables URPOutlineFeature. Keep disabled only if interaction outlines remain playable in validation screenshots.");
#if BARS_GRAPHICS_DEVELOPMENT
            EnableInteractionHoverThrottle = _category.CreateEntry(nameof(EnableInteractionHoverThrottle), true, "Enable interaction hover throttle", "Experimental: caps interaction hover checks to reduce raycast/sort/allocation cost while preserving prompt responsiveness.");
            InteractionHoverThrottleHz = _category.CreateEntry(nameof(InteractionHoverThrottleHz), 30f, "Interaction hover throttle Hz", "Experimental hover-check cadence. 30 Hz is the conservative profiling default.");
            EnableWeatherEntityThrottle = _category.CreateEntry(nameof(EnableWeatherEntityThrottle), true, "Enable weather entity throttle", "Experimental: caps weather entity updates to reduce per-frame cover/profile checks.");
            WeatherEntityThrottleHz = _category.CreateEntry(nameof(WeatherEntityThrottleHz), 10f, "Weather entity throttle Hz", "Experimental weather-entity cadence. 10 Hz is the conservative profiling default.");
#endif

            _category.SaveToFile(false);
        }

        public string GetActiveProfile()
        {
            return OptimizationProfileCatalog.Normalize(ActiveProfile?.Value ?? "Off");
        }

        public bool IsOptimizerEnabled()
        {
            return EnableOptimizer?.Value ?? true;
        }

        public bool AreHotkeysEnabled()
        {
            return EnableHotkeys?.Value ?? true;
        }

        public bool IsUiMenuEnabled()
        {
            return EnableUiMenu?.Value ?? true;
        }

        public bool ShouldLogDiagnostics()
        {
            return LogDiagnostics?.Value ?? false;
        }

        public string GetVisualStyle()
        {
            return VisualStyleCatalog.Normalize(VisualStyle?.Value ?? "Off");
        }

        public float GetVisualStyleIntensity()
        {
            float value = VisualStyleIntensity?.Value ?? 1f;
            return value < 0f ? 0f : value > 1f ? 1f : value;
        }

#if BARS_GRAPHICS_DEVELOPMENT
        public bool IsPerfTestAutomationEnabled()
        {
            return EnablePerfTestAutomation?.Value ?? false;
        }

        public bool IsLiveControlEnabled()
        {
            return EnableLiveControl?.Value ?? false;
        }

        public int GetLiveControlPort()
        {
            int port = LiveControlPort?.Value ?? 40501;
            return port < 1024 || port > 65535 ? 40501 : port;
        }
#endif

        public void SetOptimizerEnabled(bool enabled)
        {
            if (EnableOptimizer != null)
            {
                EnableOptimizer.Value = enabled;
            }

            Save();
        }

        public void SetActiveProfile(string profile)
        {
            SetActiveProfile(profile, true);
        }

        public void SetActiveProfile(string profile, bool save)
        {
            if (ActiveProfile != null)
            {
                ActiveProfile.Value = OptimizationProfileCatalog.Normalize(profile);
            }

            if (save)
            {
                Save();
            }
        }

        public void Save()
        {
            _category?.SaveToFile(false);
        }

#if BARS_GRAPHICS_DEVELOPMENT
        public bool ShouldThrottleInteractionHover()
        {
            return EnableInteractionHoverThrottle?.Value ?? false;
        }

        public float GetInteractionHoverThrottleHz()
        {
            return ClampThrottleHz(InteractionHoverThrottleHz?.Value ?? 30f, 1f, 60f);
        }

        public bool ShouldThrottleWeatherEntities()
        {
            return EnableWeatherEntityThrottle?.Value ?? false;
        }

        public float GetWeatherEntityThrottleHz()
        {
            return ClampThrottleHz(WeatherEntityThrottleHz?.Value ?? 10f, 1f, 30f);
        }
#endif

        private static float ClampThrottleHz(float value, float min, float max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }
    }
}


