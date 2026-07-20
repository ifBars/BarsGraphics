using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
#if IL2CPP
using Il2CppInterop.Runtime.InteropTypes;
#endif
using MelonLoader;
using BarsGraphics.Config;
using BarsGraphics.Models;
using BarsGraphics.Utils;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace BarsGraphics.Services
{
    internal sealed class RenderOptimizerService
    {
        private static readonly string[] Profiles = OptimizationProfileCatalog.RuntimeProfileIds;

        private readonly ModConfig _config;
        private readonly Dictionary<int, CameraSnapshot> _cameraSnapshots = new Dictionary<int, CameraSnapshot>();
        private readonly Dictionary<int, ReflectionProbeSnapshot> _probeSnapshots = new Dictionary<int, ReflectionProbeSnapshot>();
        private readonly Dictionary<int, ComponentPropertySnapshot> _componentSnapshots = new Dictionary<int, ComponentPropertySnapshot>();
        private readonly Dictionary<int, BehaviourSnapshot> _behaviourSnapshots = new Dictionary<int, BehaviourSnapshot>();
        private readonly Dictionary<int, RendererSnapshot> _rendererSnapshots = new Dictionary<int, RendererSnapshot>();
        private readonly Dictionary<int, LightSnapshot> _lightSnapshots = new Dictionary<int, LightSnapshot>();
        private readonly Dictionary<int, LodGroupSnapshot> _lodGroupSnapshots = new Dictionary<int, LodGroupSnapshot>();
        private readonly Dictionary<int, RendererFeatureSnapshot> _rendererFeatureSnapshots = new Dictionary<int, RendererFeatureSnapshot>();
        private readonly Dictionary<int, TerrainSnapshot> _terrainSnapshots = new Dictionary<int, TerrainSnapshot>();
        private readonly List<Renderer> _rendererCullCandidates = new List<Renderer>();
        private readonly List<UniversalRendererData> _urpRendererDataAssets = new List<UniversalRendererData>();

        private QualitySnapshot? _qualitySnapshot;
        private PipelineAssetSnapshot? _pipelineSnapshot;
        private object? _cachedRendererDataPipelineAsset;
        private bool _active;
        private bool _farLightShadowMutationUnavailable;
        private float _nextRendererCullAt;
        private float _nextRendererCullRefreshAt;
        private float _nextNearLodProtectionAt;
        private string _lastAppliedProfile = string.Empty;
        private string _lastAppliedSignature = string.Empty;

        public RenderOptimizerService(ModConfig config)
        {
            _config = config;
        }

        public void Update()
        {
            HandleHotkeys();

            string profile = _config.GetActiveProfile();
            EffectiveRenderSettings settings = EffectiveRenderSettings.FromProfile(_config, profile);
            string settingsSignature = BuildSettingsSignature(settings);

            if (_active && !string.Equals(_lastAppliedSignature, string.Empty, StringComparison.Ordinal) &&
                !string.Equals(_lastAppliedSignature, settingsSignature, StringComparison.Ordinal))
            {
                Restore();
            }

            if (!_config.IsOptimizerEnabled() || settings.IsOff())
            {
                if (_active)
                {
                    Restore();
                    MelonLogger.Msg($"[{Constants.ModName}] Restored baseline render settings.");
                }

                return;
            }

            if (!_active)
            {
                CaptureBaseline();
                _active = true;
                _lastAppliedProfile = string.Empty;
                _lastAppliedSignature = string.Empty;
                MelonLogger.Msg($"[{Constants.ModName}] Captured baseline render settings.");
            }

            if (!string.Equals(_lastAppliedSignature, settingsSignature, StringComparison.Ordinal))
            {
                Apply(settings);
                _lastAppliedProfile = settings.ProfileName;
                _lastAppliedSignature = settingsSignature;
            }

            MaintainVisibilitySafeRendererCulling(settings);
            MaintainNearLodProtection(settings);
        }

        public void Restore()
        {
            if (!_active)
            {
                return;
            }

            RestoreQualitySettings();
            RestorePipelineAsset();
            RestoreCameras();
            RestoreReflectionProbes();
            RestoreComponentProperties();
            RestoreBehaviours();
            RestoreLights();
            RestoreLodGroups();
            RestoreRenderers();
            RestoreTerrainFoliage();
            RestoreRendererFeatures();

            _active = false;
            _lastAppliedProfile = string.Empty;
            _lastAppliedSignature = string.Empty;
        }

        private static string BuildSettingsSignature(EffectiveRenderSettings settings)
        {
            Scene scene = SceneManager.GetActiveScene();
            return string.Join("|", new[]
            {
                scene.buildIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
                scene.name ?? string.Empty,
                settings.ProfileName,
                Bool(settings.UseRenderScale),
                Number(settings.RenderScale),
                Bool(settings.UseFsrUpscaling),
                Number(settings.FsrRenderScale),
                Number(settings.FsrSharpness),
                Bool(settings.UseShadowSettings),
                Number(settings.ShadowDistance),
                settings.ShadowCascades.ToString(System.Globalization.CultureInfo.InvariantCulture),
                settings.ShadowResolution.ToString(),
                Bool(settings.UseLodSettings),
                Number(settings.LodBias),
                settings.MaximumLodLevel.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Bool(settings.UseShaderMaximumLod),
                settings.ShaderMaximumLod.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Bool(settings.ProtectNearLods),
                Number(settings.NearLodProtectionDistance),
                Bool(settings.DisablePostProcessing),
                Bool(settings.DisableRealtimeReflectionProbes),
                Bool(settings.DisableReflectionProbes),
                Bool(settings.UseFrameRateSettings),
                settings.VSyncCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                settings.TargetFrameRate.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Bool(settings.UsePixelLightCount),
                settings.PixelLightCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Bool(settings.UseAntiAliasing),
                settings.AntiAliasing.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Bool(settings.UseGlobalTextureMipmapLimit),
                settings.GlobalTextureMipmapLimit.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Bool(settings.UseAnisotropicFiltering),
                settings.AnisotropicFilteringMode.ToString(),
                Bool(settings.UseAdditionalLightsMode),
                settings.AdditionalLightsMode ?? string.Empty,
                (settings.MaxAdditionalLightsCount ?? -1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                Bool(settings.DisableFarLightShadows),
                Number(settings.FarLightShadowDistance),
                Bool(settings.UseCameraFarClip),
                Number(settings.CameraFarClipDistance),
                Bool(settings.DisableCameraStacks),
                Bool(settings.UseLayerCullDistances),
                Number(settings.LayerCullDistance),
                Bool(settings.DisableCameraOcclusionCulling),
                Bool(settings.DisableVolumetricLightBeams),
                Bool(settings.UseVisibilitySafeRendererCulling),
                Number(settings.RendererCullMinDistance),
                Bool(settings.DisableTerrainFoliage),
                Number(settings.TerrainDetailObjectDistance),
                Bool(settings.DisableOutlineFeature)
            });
        }

        private static string Bool(bool value)
        {
            return value ? "1" : "0";
        }

        private static string Number(float value)
        {
            return value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }

        public string GetRuntimeStateJson()
        {
            object? pipelineAsset = GraphicsSettings.currentRenderPipeline;
            string renderScale =
                (TryGetUniversalPipelineValue(pipelineAsset, "renderScale", out object? directScale) ||
                 TryGetProperty(pipelineAsset, "renderScale", out directScale) ||
                 TryGetField(pipelineAsset, "m_RenderScale", out directScale))
                    ? Convert.ToString(directScale, System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"
                    : "unknown";
            string upscalingFilter =
                (TryGetUniversalPipelineValue(pipelineAsset, "upscalingFilter", out object? filterValue) ||
                 TryGetProperty(pipelineAsset, "upscalingFilter", out filterValue) ||
                 TryGetField(pipelineAsset, "m_UpscalingFilter", out filterValue))
                    ? Convert.ToString(filterValue, System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"
                    : "unknown";

            return
                $"\"runtimeRenderScale\":{QuoteJson(renderScale)}," +
                $"\"runtimeUpscalingFilter\":{QuoteJson(upscalingFilter)}," +
                $"\"runtimeShadowDistance\":{QualitySettings.shadowDistance.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}," +
                $"\"runtimeShadowCascades\":{QualitySettings.shadowCascades}," +
                $"\"runtimeShadowResolution\":{QuoteJson(Convert.ToString(QualitySettings.shadowResolution) ?? string.Empty)}," +
                $"\"runtimeLodBias\":{QualitySettings.lodBias.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}," +
                $"\"runtimeMaximumLodLevel\":{QualitySettings.maximumLODLevel}," +
                $"\"runtimeShaderMaximumLod\":{Shader.globalMaximumLOD}," +
                $"\"runtimePixelLightCount\":{QualitySettings.pixelLightCount}," +
                $"\"runtimeAntiAliasing\":{QualitySettings.antiAliasing}," +
                $"\"runtimeGlobalTextureMipmapLimit\":{QualitySettings.globalTextureMipmapLimit}," +
                $"\"runtimeAnisotropicFiltering\":{QuoteJson(QualitySettings.anisotropicFiltering.ToString())}," +
                $"\"runtimeVSyncCount\":{QualitySettings.vSyncCount}," +
                $"\"runtimeTargetFrameRate\":{Application.targetFrameRate}," +
                $"\"runtimeForcedNearLodGroups\":{_lodGroupSnapshots.Count}," +
                $"\"runtimeDisabledRenderers\":{_rendererSnapshots.Count}," +
                $"\"runtimeTerrainSnapshots\":{_terrainSnapshots.Count}," +
                $"\"runtimePipeline\":{QuoteJson(pipelineAsset?.GetType().FullName ?? "none")}";
        }

        private void HandleHotkeys()
        {
            if (!_config.AreHotkeysEnabled())
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.F6))
            {
                bool enabled = !_config.IsOptimizerEnabled();
                _config.SetOptimizerEnabled(enabled);
                MelonLogger.Msg($"[{Constants.ModName}] Optimizer {(enabled ? "enabled" : "disabled")}.");
            }
        }

        private void CaptureBaseline()
        {
            _qualitySnapshot = new QualitySnapshot
            {
                ShadowDistance = QualitySettings.shadowDistance,
                ShadowCascades = QualitySettings.shadowCascades,
                ShadowResolution = QualitySettings.shadowResolution,
                LodBias = QualitySettings.lodBias,
                MaximumLodLevel = QualitySettings.maximumLODLevel,
                PixelLightCount = QualitySettings.pixelLightCount,
                RealtimeReflectionProbes = QualitySettings.realtimeReflectionProbes,
                AntiAliasing = QualitySettings.antiAliasing,
                GlobalTextureMipmapLimit = QualitySettings.globalTextureMipmapLimit,
                ShaderMaximumLod = Shader.globalMaximumLOD,
                AnisotropicFiltering = QualitySettings.anisotropicFiltering,
                VSyncCount = QualitySettings.vSyncCount,
                TargetFrameRate = Application.targetFrameRate
            };

            object? pipelineAsset = GraphicsSettings.currentRenderPipeline;
            if (pipelineAsset != null)
            {
                _pipelineSnapshot = CapturePipelineSnapshot(pipelineAsset);
            }
        }

        private void Apply(EffectiveRenderSettings settings)
        {
            ApplyQualitySettings(settings);
            ApplyPipelineAsset(settings);
            ApplyCameras(settings);
            ApplyReflectionProbes(settings);
            ApplySceneBehaviours(settings);
            ApplyFarLightShadows(settings);
            ApplyTerrainFoliage(settings);
            ApplyRendererFeatures(settings);
        }

        private void ApplyQualitySettings(EffectiveRenderSettings settings)
        {
            if (settings.UseShadowSettings)
            {
                QualitySettings.shadowDistance = Math.Min(QualitySettings.shadowDistance, settings.ShadowDistance);
                QualitySettings.shadowCascades = settings.ShadowCascades;
                QualitySettings.shadowResolution = settings.ShadowResolution;
            }

            if (settings.UseLodSettings)
            {
                QualitySettings.lodBias = Math.Min(QualitySettings.lodBias, settings.LodBias);
                QualitySettings.maximumLODLevel = Math.Max(QualitySettings.maximumLODLevel, settings.MaximumLodLevel);
            }

            if (settings.DisableRealtimeReflectionProbes)
            {
                QualitySettings.realtimeReflectionProbes = false;
            }

            if (settings.UseFrameRateSettings)
            {
                QualitySettings.vSyncCount = settings.VSyncCount;
                Application.targetFrameRate = settings.TargetFrameRate;
            }

            if (settings.UsePixelLightCount)
            {
                QualitySettings.pixelLightCount = settings.PixelLightCount;
            }

            if (settings.UseAntiAliasing)
            {
                QualitySettings.antiAliasing = settings.AntiAliasing;
            }

            if (settings.UseGlobalTextureMipmapLimit)
            {
                QualitySettings.globalTextureMipmapLimit = settings.GlobalTextureMipmapLimit;
            }

            if (settings.UseShaderMaximumLod)
            {
                Shader.globalMaximumLOD = settings.ShaderMaximumLod;
            }

            if (settings.UseAnisotropicFiltering)
            {
                QualitySettings.anisotropicFiltering = settings.AnisotropicFilteringMode;
            }
        }

        private void ApplyPipelineAsset(EffectiveRenderSettings settings)
        {
            object? pipelineAsset = GraphicsSettings.currentRenderPipeline;
            if (pipelineAsset == null)
            {
                return;
            }

            if (_pipelineSnapshot == null || !ReferenceEquals(_pipelineSnapshot.Asset, pipelineAsset))
            {
                _pipelineSnapshot = CapturePipelineSnapshot(pipelineAsset);
            }

            if (settings.UseRenderScale)
            {
                if (!TrySetUniversalRenderScale(pipelineAsset, settings.RenderScale))
                {
                    if (!TrySetProperty(pipelineAsset, "renderScale", settings.RenderScale))
                    {
                        TrySetField(pipelineAsset, "m_RenderScale", settings.RenderScale);
                    }
                }
            }

            if (settings.UseFsrUpscaling)
            {
                SetPipelineMember(pipelineAsset, "renderScale", settings.FsrRenderScale);
                SetPipelineMember(pipelineAsset, "upscalingFilter", 3);
                SetPipelineMember(pipelineAsset, "fsrOverrideSharpness", true);
                SetPipelineMember(pipelineAsset, "fsrSharpness", settings.FsrSharpness);
            }

            if (settings.UseShadowSettings)
            {
                if (!TrySetUniversalShadowDistance(pipelineAsset, settings.ShadowDistance))
                {
                    if (!TrySetProperty(pipelineAsset, "shadowDistance", settings.ShadowDistance))
                    {
                        TrySetField(pipelineAsset, "m_ShadowDistance", settings.ShadowDistance);
                    }
                }

                TrySetProperty(pipelineAsset, "mainLightShadowmapResolution", 1024);
                TrySetProperty(pipelineAsset, "additionalLightsShadowmapResolution", 512);
                TrySetProperty(pipelineAsset, "supportsSoftShadows", false);
            }

            if (settings.UseAdditionalLightsMode)
            {
                if (!TrySetUniversalPipelineValue(pipelineAsset, "additionalLightsRenderingMode", settings.AdditionalLightsMode))
                {
                    if (!TrySetProperty(pipelineAsset, "additionalLightsRenderingMode", settings.AdditionalLightsMode))
                    {
                        TrySetField(pipelineAsset, "m_AdditionalLightsRenderingMode", settings.AdditionalLightsMode);
                    }
                }

                if (settings.MaxAdditionalLightsCount.HasValue)
                {
                    if (!TrySetUniversalPipelineValue(pipelineAsset, "maxAdditionalLightsCount", settings.MaxAdditionalLightsCount.Value))
                    {
                        TrySetProperty(pipelineAsset, "maxAdditionalLightsCount", settings.MaxAdditionalLightsCount.Value);
                    }
                }
            }
        }

        private static void SetPipelineMember(object pipelineAsset, string memberName, object? value)
        {
            if (!TrySetUniversalPipelineValue(pipelineAsset, memberName, value))
            {
                if (!TrySetProperty(pipelineAsset, memberName, value))
                {
                    TrySetField(pipelineAsset, ToBackingFieldName(memberName), value);
                }
            }
        }

        private void ApplyRendererFeatures(EffectiveRenderSettings settings)
        {
            if (settings.DisableOutlineFeature)
            {
                SetRendererFeatureActive("URPOutlineFeature", false);
            }
        }

        private void ApplyCameras(EffectiveRenderSettings settings)
        {
            Camera[] cameras = Camera.allCameras;
            foreach (Camera camera in cameras)
            {
                if (camera == null)
                {
                    continue;
                }

                int id = camera.GetInstanceID();
                if (!_cameraSnapshots.ContainsKey(id))
                {
                    CameraSnapshot newSnapshot = new CameraSnapshot
                    {
                        FarClipPlane = camera.farClipPlane,
                        LayerCullSpherical = camera.layerCullSpherical,
                        UseOcclusionCulling = camera.useOcclusionCulling,
                        LayerCullDistances = CopyLayerCullDistances(camera.layerCullDistances)
                    };

                    TryCaptureCameraStack(camera, newSnapshot);
                    _cameraSnapshots[id] = newSnapshot;
                }

                CameraSnapshot snapshot = _cameraSnapshots[id];
                camera.farClipPlane = snapshot.FarClipPlane;
                camera.layerCullSpherical = snapshot.LayerCullSpherical;
                camera.useOcclusionCulling = snapshot.UseOcclusionCulling;
                camera.layerCullDistances = CopyLayerCullDistances(snapshot.LayerCullDistances);
                RestoreCameraStack(camera, snapshot);

                if (settings.UseCameraFarClip && camera.farClipPlane > settings.CameraFarClipDistance)
                {
                    camera.farClipPlane = settings.CameraFarClipDistance;
                }

                if (settings.UseLayerCullDistances)
                {
                    camera.layerCullSpherical = true;
                    camera.layerCullDistances = BuildLayerCullDistances(snapshot.LayerCullDistances, settings.LayerCullDistance);
                }

                if (settings.DisableCameraOcclusionCulling)
                {
                    camera.useOcclusionCulling = false;
                }

                if (settings.DisablePostProcessing)
                {
                    DisableCameraPostProcessing(camera);
                }

                if (settings.DisableCameraStacks)
                {
                    ClearCameraStack(camera);
                }
            }
        }

        private void ApplyReflectionProbes(EffectiveRenderSettings settings)
        {
            if (!settings.DisableReflectionProbes)
            {
                return;
            }

            ReflectionProbe[] probes = UnityEngine.Object.FindObjectsOfType<ReflectionProbe>();
            foreach (ReflectionProbe probe in probes)
            {
                if (probe == null)
                {
                    continue;
                }

                int id = probe.GetInstanceID();
                if (!_probeSnapshots.ContainsKey(id))
                {
                    _probeSnapshots[id] = new ReflectionProbeSnapshot
                    {
                        Enabled = probe.enabled
                    };
                }

                probe.enabled = false;
            }
        }

        private static void TryCaptureCameraStack(Camera camera, CameraSnapshot snapshot)
        {
            try
            {
                UniversalAdditionalCameraData data = camera.GetComponent<UniversalAdditionalCameraData>();
                if (data?.cameraStack == null)
                {
                    return;
                }

                for (int index = 0; index < data.cameraStack.Count; index++)
                {
                    Camera stackedCamera = data.cameraStack[index];
                    if (stackedCamera != null)
                    {
                        snapshot.Stack.Add(stackedCamera);
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[{Constants.ModName}] Could not capture camera stack for {camera.name}: {ex.Message}");
            }
        }

        private static void RestoreCameraStack(Camera camera, CameraSnapshot snapshot)
        {
            try
            {
                UniversalAdditionalCameraData data = camera.GetComponent<UniversalAdditionalCameraData>();
                if (data?.cameraStack == null)
                {
                    return;
                }

                data.cameraStack.Clear();
                foreach (Camera stackedCamera in snapshot.Stack)
                {
                    if (stackedCamera != null)
                    {
                        data.cameraStack.Add(stackedCamera);
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[{Constants.ModName}] Could not restore camera stack for {camera.name}: {ex.Message}");
            }
        }

        private static void ClearCameraStack(Camera camera)
        {
            try
            {
                UniversalAdditionalCameraData data = camera.GetComponent<UniversalAdditionalCameraData>();
                if (data?.cameraStack == null || data.cameraStack.Count == 0)
                {
                    return;
                }

                data.cameraStack.Clear();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[{Constants.ModName}] Could not clear camera stack for {camera.name}: {ex.Message}");
            }
        }

        private void ApplySceneBehaviours(EffectiveRenderSettings settings)
        {
            if (!settings.DisableVolumetricLightBeams)
            {
                return;
            }

            foreach (Behaviour behaviour in UnityEngine.Object.FindObjectsOfType<Behaviour>())
            {
                if (behaviour == null)
                {
                    continue;
                }

                Type type = behaviour.GetType();
                string fullName = type.FullName ?? type.Name;
                if (!fullName.StartsWith("VLB.", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int id = behaviour.GetInstanceID();
                if (!_behaviourSnapshots.ContainsKey(id))
                {
                    _behaviourSnapshots[id] = new BehaviourSnapshot { Enabled = behaviour.enabled };
                }

                behaviour.enabled = false;
            }
        }

        private void ApplyFarLightShadows(EffectiveRenderSettings settings)
        {
            if (!settings.DisableFarLightShadows)
            {
                RestoreLights();
                _farLightShadowMutationUnavailable = false;
                return;
            }

            if (_farLightShadowMutationUnavailable)
            {
                return;
            }

            Camera camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            Vector3 cameraPosition = camera.transform.position;
            foreach (Light light in UnityEngine.Object.FindObjectsOfType<Light>())
            {
                if (!TryApplyFarLightShadowSetting(light, cameraPosition, settings.FarLightShadowDistance))
                {
                    _farLightShadowMutationUnavailable = true;
                    RestoreLights();
                    MelonLogger.Warning($"[{Constants.ModName}] Disabled far-light shadow mutation for this session because Unity rejected a light shadow update.");
                    return;
                }
            }
        }

        private bool TryApplyFarLightShadowSetting(Light light, Vector3 cameraPosition, float farLightShadowDistance)
        {
            try
            {
                if (light == null || light.type == LightType.Directional)
                {
                    return true;
                }

                int id = light.GetInstanceID();
                if (!_lightSnapshots.ContainsKey(id))
                {
                    _lightSnapshots[id] = new LightSnapshot
                    {
                        Shadows = light.shadows,
                        ShadowResolution = light.shadowResolution
                    };
                }

                LightSnapshot snapshot = _lightSnapshots[id];
                float distance = Vector3.Distance(cameraPosition, light.transform.position);
                if (distance <= farLightShadowDistance)
                {
                    light.shadows = snapshot.Shadows;
                    light.shadowResolution = snapshot.ShadowResolution;
                    return true;
                }

                light.shadows = LightShadows.None;
                return true;
            }
            catch (Exception ex)
            {
                string name = light == null ? "unknown" : light.name;
                MelonLogger.Warning($"[{Constants.ModName}] Could not mutate light shadows for {name}: {ex.Message}");
                return false;
            }
        }

        private void ApplyTerrainFoliage(EffectiveRenderSettings settings)
        {
            if (!settings.DisableTerrainFoliage)
            {
                RestoreTerrainFoliage();
                return;
            }

            foreach (Terrain terrain in Terrain.activeTerrains)
            {
                if (terrain == null)
                {
                    continue;
                }

                int id = terrain.GetInstanceID();
                if (!_terrainSnapshots.ContainsKey(id))
                {
                    _terrainSnapshots[id] = new TerrainSnapshot
                    {
                        Terrain = terrain,
                        DrawTreesAndFoliage = terrain.drawTreesAndFoliage,
                        DetailObjectDistance = terrain.detailObjectDistance
                    };
                }

                terrain.drawTreesAndFoliage = false;
                terrain.detailObjectDistance = settings.TerrainDetailObjectDistance;
            }
        }

        private void DisableCameraPostProcessing(Camera camera)
        {
            Component[] components = camera.GetComponents<Component>();
            foreach (Component component in components)
            {
                if (component == null)
                {
                    continue;
                }

                Type type = component.GetType();
                string fullName = type.FullName ?? type.Name;
                if (fullName.IndexOf("UniversalAdditionalCameraData", StringComparison.OrdinalIgnoreCase) < 0 &&
                    fullName.IndexOf("AdditionalCameraData", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                CaptureComponentProperty(component, "renderPostProcessing");
                TrySetProperty(component, "renderPostProcessing", false);

                CaptureComponentProperty(component, "antialiasing");
                TrySetProperty(component, "antialiasing", "None");
            }
        }

        private PipelineAssetSnapshot CapturePipelineSnapshot(object pipelineAsset)
        {
            PipelineAssetSnapshot snapshot = new PipelineAssetSnapshot(pipelineAsset);
            string[] names =
            {
                "renderScale",
                "shadowDistance",
                "mainLightShadowmapResolution",
                "additionalLightsShadowmapResolution",
                "supportsSoftShadows",
                "additionalLightsRenderingMode",
                "maxAdditionalLightsCount",
                "upscalingFilter",
                "fsrOverrideSharpness",
                "fsrSharpness"
            };

            foreach (string name in names)
            {
                if (TryGetUniversalPipelineValue(pipelineAsset, name, out object? value) ||
                    TryGetProperty(pipelineAsset, name, out value) ||
                    TryGetField(pipelineAsset, ToBackingFieldName(name), out value))
                {
                    snapshot.Values[name] = value;
                }
            }

            return snapshot;
        }

        private void CaptureComponentProperty(object target, string propertyName)
        {
            int id = GetUnityObjectId(target);
            if (id == 0)
            {
                return;
            }

            if (!_componentSnapshots.TryGetValue(id, out ComponentPropertySnapshot snapshot))
            {
                snapshot = new ComponentPropertySnapshot();
                _componentSnapshots[id] = snapshot;
            }

            if (!snapshot.Values.ContainsKey(propertyName) && TryGetProperty(target, propertyName, out object? value))
            {
                snapshot.Values[propertyName] = value;
            }
        }

        private void SetRendererFeatureActive(string nameContains, bool active)
        {
            foreach (UniversalRendererData rendererData in GetUrpRendererDataAssets())
            {
                if (rendererData == null || rendererData.rendererFeatures == null)
                {
                    continue;
                }

                foreach (ScriptableRendererFeature feature in rendererData.rendererFeatures)
                {
                    if (feature == null)
                    {
                        continue;
                    }

                    string name = feature.name ?? string.Empty;
                    string typeName = feature.GetType().FullName ?? feature.GetType().Name;
                    if (name.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) < 0 &&
                        typeName.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    int id = feature.GetInstanceID();
                    if (!_rendererFeatureSnapshots.ContainsKey(id))
                    {
                        _rendererFeatureSnapshots[id] = new RendererFeatureSnapshot
                        {
                            Feature = feature,
                            Active = feature.isActive
                        };
                    }

                    if (feature.isActive == active)
                    {
                        continue;
                    }

                    try
                    {
                        feature.SetActive(active);
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Warning($"[{Constants.ModName}] Could not toggle renderer feature {name}: {ex.Message}");
                    }
                }
            }
        }

        private void RestoreQualitySettings()
        {
            if (_qualitySnapshot == null)
            {
                return;
            }

            QualitySettings.shadowDistance = _qualitySnapshot.ShadowDistance;
            QualitySettings.shadowCascades = _qualitySnapshot.ShadowCascades;
            QualitySettings.shadowResolution = _qualitySnapshot.ShadowResolution;
            QualitySettings.lodBias = _qualitySnapshot.LodBias;
            QualitySettings.maximumLODLevel = _qualitySnapshot.MaximumLodLevel;
            QualitySettings.pixelLightCount = _qualitySnapshot.PixelLightCount;
            QualitySettings.realtimeReflectionProbes = _qualitySnapshot.RealtimeReflectionProbes;
            QualitySettings.antiAliasing = _qualitySnapshot.AntiAliasing;
            QualitySettings.globalTextureMipmapLimit = _qualitySnapshot.GlobalTextureMipmapLimit;
            Shader.globalMaximumLOD = _qualitySnapshot.ShaderMaximumLod;
            QualitySettings.anisotropicFiltering = _qualitySnapshot.AnisotropicFiltering;
            QualitySettings.vSyncCount = _qualitySnapshot.VSyncCount;
            Application.targetFrameRate = _qualitySnapshot.TargetFrameRate;
        }

        private void RestorePipelineAsset()
        {
            if (_pipelineSnapshot == null)
            {
                return;
            }

            foreach (KeyValuePair<string, object?> value in _pipelineSnapshot.Values)
            {
                if (!TrySetUniversalPipelineValue(_pipelineSnapshot.Asset, value.Key, value.Value))
                {
                    if (!TrySetProperty(_pipelineSnapshot.Asset, value.Key, value.Value))
                    {
                        TrySetField(_pipelineSnapshot.Asset, ToBackingFieldName(value.Key), value.Value);
                    }
                }
            }
        }

        private void RestoreRendererFeatures()
        {
            foreach (RendererFeatureSnapshot snapshot in _rendererFeatureSnapshots.Values)
            {
                ScriptableRendererFeature? feature = snapshot.Feature;
                if (feature == null)
                {
                    continue;
                }

                try
                {
                    if (feature.isActive != snapshot.Active)
                    {
                        feature.SetActive(snapshot.Active);
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[{Constants.ModName}] Could not restore renderer feature {feature.name}: {ex.Message}");
                }
            }

            _rendererFeatureSnapshots.Clear();
        }

        private IReadOnlyList<UniversalRendererData> GetUrpRendererDataAssets()
        {
            object? pipelineAsset = GraphicsSettings.currentRenderPipeline;
            if (_cachedRendererDataPipelineAsset == pipelineAsset && _urpRendererDataAssets.Count > 0)
            {
                return _urpRendererDataAssets;
            }

            _cachedRendererDataPipelineAsset = pipelineAsset;
            _urpRendererDataAssets.Clear();

            foreach (UniversalRendererData rendererData in Resources.FindObjectsOfTypeAll<UniversalRendererData>())
            {
                if (rendererData != null)
                {
                    _urpRendererDataAssets.Add(rendererData);
                }
            }

            return _urpRendererDataAssets;
        }

        private void RestoreCameras()
        {
            Camera[] cameras = Camera.allCameras;
            foreach (Camera camera in cameras)
            {
                if (camera == null || !_cameraSnapshots.TryGetValue(camera.GetInstanceID(), out CameraSnapshot snapshot))
                {
                    continue;
                }

                camera.farClipPlane = snapshot.FarClipPlane;
                camera.layerCullSpherical = snapshot.LayerCullSpherical;
                camera.useOcclusionCulling = snapshot.UseOcclusionCulling;
                camera.layerCullDistances = CopyLayerCullDistances(snapshot.LayerCullDistances);
                RestoreCameraStack(camera, snapshot);
            }
        }

        private void RestoreReflectionProbes()
        {
            ReflectionProbe[] probes = UnityEngine.Object.FindObjectsOfType<ReflectionProbe>();
            foreach (ReflectionProbe probe in probes)
            {
                if (probe == null || !_probeSnapshots.TryGetValue(probe.GetInstanceID(), out ReflectionProbeSnapshot snapshot))
                {
                    continue;
                }

                probe.enabled = snapshot.Enabled;
            }
        }

        private void RestoreComponentProperties()
        {
            foreach (Component component in UnityEngine.Object.FindObjectsOfType<Component>())
            {
                if (component == null)
                {
                    continue;
                }

                int id = component.GetInstanceID();
                if (!_componentSnapshots.TryGetValue(id, out ComponentPropertySnapshot snapshot))
                {
                    continue;
                }

                foreach (KeyValuePair<string, object?> value in snapshot.Values)
                {
                    TrySetProperty(component, value.Key, value.Value);
                }
            }
        }

        private void RestoreBehaviours()
        {
            foreach (Behaviour behaviour in UnityEngine.Object.FindObjectsOfType<Behaviour>())
            {
                if (behaviour == null || !_behaviourSnapshots.TryGetValue(behaviour.GetInstanceID(), out BehaviourSnapshot snapshot))
                {
                    continue;
                }

                behaviour.enabled = snapshot.Enabled;
            }

            _behaviourSnapshots.Clear();
        }

        private void RestoreLights()
        {
            foreach (Light light in UnityEngine.Object.FindObjectsOfType<Light>())
            {
                if (light == null || !_lightSnapshots.TryGetValue(light.GetInstanceID(), out LightSnapshot snapshot))
                {
                    continue;
                }

                try
                {
                    light.shadows = snapshot.Shadows;
                    light.shadowResolution = snapshot.ShadowResolution;
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[{Constants.ModName}] Could not restore light shadows for {light.name}: {ex.Message}");
                }
            }

            _lightSnapshots.Clear();
        }

        private void MaintainNearLodProtection(EffectiveRenderSettings settings)
        {
            if (!settings.ProtectNearLods)
            {
                RestoreLodGroups();
                return;
            }

            if (Time.unscaledTime < _nextNearLodProtectionAt)
            {
                return;
            }

            Camera camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            Vector3 cameraPosition = camera.transform.position;
            float maxDistance = settings.NearLodProtectionDistance;
            foreach (LODGroup group in UnityEngine.Object.FindObjectsOfType<LODGroup>())
            {
                if (group == null)
                {
                    continue;
                }

                int id = group.GetInstanceID();
                float distance = Vector3.Distance(cameraPosition, group.transform.TransformPoint(group.localReferencePoint));
                if (distance <= maxDistance)
                {
                    if (!_lodGroupSnapshots.ContainsKey(id))
                    {
                        _lodGroupSnapshots[id] = new LodGroupSnapshot { Group = group };
                    }

                    group.ForceLOD(0);
                    continue;
                }

                if (_lodGroupSnapshots.ContainsKey(id))
                {
                    group.ForceLOD(-1);
                    _lodGroupSnapshots.Remove(id);
                }
            }

            _nextNearLodProtectionAt = Time.unscaledTime + 0.25f;
        }

        private void RestoreLodGroups()
        {
            foreach (LodGroupSnapshot snapshot in _lodGroupSnapshots.Values)
            {
                LODGroup? group = snapshot.Group;
                if (group == null)
                {
                    continue;
                }

                group.ForceLOD(-1);
            }

            _lodGroupSnapshots.Clear();
        }

        private void MaintainVisibilitySafeRendererCulling(EffectiveRenderSettings settings)
        {
            if (!settings.UseVisibilitySafeRendererCulling)
            {
                RestoreRenderers();
                return;
            }

            Camera camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(camera);
            Vector3 cameraPosition = camera.transform.position;

            List<int>? restoredIds = null;
            foreach (KeyValuePair<int, RendererSnapshot> entry in _rendererSnapshots)
            {
                Renderer? renderer = entry.Value.Renderer;
                if (renderer == null)
                {
                    restoredIds ??= new List<int>();
                    restoredIds.Add(entry.Key);
                    continue;
                }

                bool inFrustum = GeometryUtility.TestPlanesAABB(planes, renderer.bounds);
                float distance = Vector3.Distance(cameraPosition, renderer.bounds.center);
                if (inFrustum || distance < settings.RendererCullMinDistance)
                {
                    renderer.enabled = entry.Value.Enabled;
                    restoredIds ??= new List<int>();
                    restoredIds.Add(entry.Key);
                }
            }

            if (restoredIds != null)
            {
                foreach (int id in restoredIds)
                {
                    _rendererSnapshots.Remove(id);
                }
            }

            if (Time.unscaledTime < _nextRendererCullAt)
            {
                return;
            }

            RefreshRendererCullCandidates();

            foreach (Renderer renderer in _rendererCullCandidates)
            {
                if (renderer == null || !renderer.enabled)
                {
                    continue;
                }

                int id = renderer.GetInstanceID();
                if (_rendererSnapshots.ContainsKey(id))
                {
                    continue;
                }

                bool inFrustum = GeometryUtility.TestPlanesAABB(planes, renderer.bounds);
                float distance = Vector3.Distance(cameraPosition, renderer.bounds.center);
                if (inFrustum || distance < settings.RendererCullMinDistance)
                {
                    continue;
                }

                _rendererSnapshots[id] = new RendererSnapshot { Renderer = renderer, Enabled = renderer.enabled };
                renderer.enabled = false;
            }

            _nextRendererCullAt = Time.unscaledTime + 0.25f;
        }

        private void RefreshRendererCullCandidates()
        {
            if (Time.unscaledTime < _nextRendererCullRefreshAt)
            {
                return;
            }

            _rendererCullCandidates.Clear();
            foreach (Renderer renderer in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (renderer == null || ShouldPreserveRenderer(renderer))
                {
                    continue;
                }

                _rendererCullCandidates.Add(renderer);
            }

            _nextRendererCullRefreshAt = Time.unscaledTime + 1f;
        }

        private void RestoreRenderers()
        {
            foreach (RendererSnapshot snapshot in _rendererSnapshots.Values)
            {
                Renderer? renderer = snapshot.Renderer;
                if (renderer == null)
                {
                    continue;
                }

                renderer.enabled = snapshot.Enabled;
            }

            _rendererSnapshots.Clear();
            _rendererCullCandidates.Clear();
        }

        private void RestoreTerrainFoliage()
        {
            foreach (TerrainSnapshot snapshot in _terrainSnapshots.Values)
            {
                Terrain? terrain = snapshot.Terrain;
                if (terrain == null)
                {
                    continue;
                }

                terrain.drawTreesAndFoliage = snapshot.DrawTreesAndFoliage;
                terrain.detailObjectDistance = snapshot.DetailObjectDistance;
            }

            _terrainSnapshots.Clear();
        }

        private static bool ShouldPreserveRenderer(Renderer renderer)
        {
            GameObject gameObject = renderer.gameObject;
            if (gameObject == null)
            {
                return true;
            }

            if (gameObject.layer == LayerMask.NameToLayer("UI"))
            {
                return true;
            }

            string typeName = renderer.GetType().Name;
            return typeName.IndexOf("Particle", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeName.IndexOf("Trail", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeName.IndexOf("Line", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void LogDiagnostics(string reason)
        {
            int cameraCount = Camera.allCamerasCount;
            int probeCount = UnityEngine.Object.FindObjectsOfType<ReflectionProbe>().Length;
            object? pipelineAsset = GraphicsSettings.currentRenderPipeline;
            string renderScale =
                (TryGetUniversalPipelineValue(pipelineAsset, "renderScale", out object? directScale) ||
                 TryGetProperty(pipelineAsset, "renderScale", out directScale) ||
                 TryGetField(pipelineAsset, "m_RenderScale", out directScale))
                    ? Convert.ToString(directScale) ?? "unknown"
                    : "unknown";
            string pipelineType = pipelineAsset?.GetType().FullName ?? "none";

            MelonLogger.Msg(
                $"[{Constants.ModName}] {reason}: profile={_config.GetActiveProfile()}, renderScale={renderScale}, " +
                $"shadowDistance={QualitySettings.shadowDistance:0.##}, cascades={QualitySettings.shadowCascades}, " +
                $"shadowResolution={QualitySettings.shadowResolution}, lodBias={QualitySettings.lodBias:0.##}, " +
                $"maxLOD={QualitySettings.maximumLODLevel}, shaderLOD={Shader.globalMaximumLOD}, aniso={QualitySettings.anisotropicFiltering}, " +
                $"cameras={cameraCount}, reflectionProbes={probeCount}, pipeline={pipelineType}.");
        }

        private static bool TrySetUniversalRenderScale(object? pipelineAsset, float renderScale)
        {
            if (!TryGetUniversalAsset(pipelineAsset, out UniversalRenderPipelineAsset? universalAsset))
            {
                return false;
            }

            try
            {
                universalAsset.renderScale = renderScale;
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[{Constants.ModName}] Could not set URP renderScale directly: {ex.Message}");
                return false;
            }
        }

        private static bool TrySetUniversalShadowDistance(object? pipelineAsset, float shadowDistance)
        {
            if (!TryGetUniversalAsset(pipelineAsset, out UniversalRenderPipelineAsset? universalAsset))
            {
                return false;
            }

            try
            {
                universalAsset.shadowDistance = shadowDistance;
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[{Constants.ModName}] Could not set URP shadowDistance directly: {ex.Message}");
                return false;
            }
        }

        private static bool TryGetUniversalPipelineValue(object? pipelineAsset, string propertyName, out object? value)
        {
            value = null;
            if (!TryGetUniversalAsset(pipelineAsset, out UniversalRenderPipelineAsset? universalAsset))
            {
                return false;
            }

            try
            {
                switch (propertyName)
                {
                    case "renderScale":
                        value = universalAsset.renderScale;
                        return true;
                    case "shadowDistance":
                        value = universalAsset.shadowDistance;
                        return true;
                    case "maxAdditionalLightsCount":
                        value = universalAsset.maxAdditionalLightsCount;
                        return true;
                    case "additionalLightsRenderingMode":
                        value = NormalizeLightRenderingMode(universalAsset.additionalLightsRenderingMode);
                        return true;
                    case "supportsSoftShadows":
                        value = universalAsset.supportsSoftShadows;
                        return true;
                    case "upscalingFilter":
                    case "fsrOverrideSharpness":
                    case "fsrSharpness":
                        return false;
                    default:
                        return false;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[{Constants.ModName}] Could not read URP {propertyName} directly: {ex.Message}");
                return false;
            }
        }

        private static bool TrySetUniversalPipelineValue(object? pipelineAsset, string propertyName, object? value)
        {
            if (!TryGetUniversalAsset(pipelineAsset, out UniversalRenderPipelineAsset? universalAsset))
            {
                return false;
            }

            try
            {
                switch (propertyName)
                {
                    case "renderScale":
                        universalAsset.renderScale = Convert.ToSingle(value);
                        return true;
                    case "shadowDistance":
                        universalAsset.shadowDistance = Convert.ToSingle(value);
                        return true;
                    case "maxAdditionalLightsCount":
                        universalAsset.maxAdditionalLightsCount = Convert.ToInt32(value);
                        return true;
                    case "additionalLightsRenderingMode":
#if IL2CPP
                        if (TryParseLightRenderingMode(value, out LightRenderingMode mode))
                        {
                            universalAsset.additionalLightsRenderingMode = mode;
                            return true;
                        }

                        return false;
#else
                        return false;
#endif
                    case "upscalingFilter":
                    case "fsrOverrideSharpness":
                    case "fsrSharpness":
                        return false;
                    default:
                        return false;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[{Constants.ModName}] Could not restore URP {propertyName} directly: {ex.Message}");
                return false;
            }
        }

        private static bool TryGetUniversalAsset(object? pipelineAsset, out UniversalRenderPipelineAsset? universalAsset)
        {
            universalAsset = null;
            if (pipelineAsset == null)
            {
                return false;
            }

            if (pipelineAsset is UniversalRenderPipelineAsset typedAsset)
            {
                universalAsset = typedAsset;
                return true;
            }

#if IL2CPP
            if (pipelineAsset is Il2CppObjectBase il2CppObject)
            {
                try
                {
                    universalAsset = il2CppObject.TryCast<UniversalRenderPipelineAsset>();
                    return universalAsset != null;
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[{Constants.ModName}] Could not IL2CPP-cast pipeline asset to URP: {ex.Message}");
                }
            }
#endif

            return false;
        }

        private static bool TryGetProperty(object? target, string propertyName, out object? value)
        {
            value = null;
            if (target == null)
            {
                return false;
            }

            PropertyInfo? property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property == null || !property.CanRead)
            {
                return false;
            }

            try
            {
                value = property.GetValue(target);
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[{Constants.ModName}] Could not read {target.GetType().Name}.{propertyName}: {ex.Message}");
                return false;
            }
        }

        private static bool TrySetProperty(object? target, string propertyName, object? value)
        {
            if (target == null)
            {
                return false;
            }

            PropertyInfo? property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property == null || !property.CanWrite)
            {
                return false;
            }

            try
            {
                object? converted = ConvertValue(value, property.PropertyType);
                property.SetValue(target, converted);
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[{Constants.ModName}] Could not set {target.GetType().Name}.{propertyName}: {ex.Message}");
                return false;
            }
        }

        private static bool TryGetField(object? target, string fieldName, out object? value)
        {
            value = null;
            if (target == null || string.IsNullOrWhiteSpace(fieldName))
            {
                return false;
            }

            FieldInfo? field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null)
            {
                return false;
            }

            try
            {
                value = field.GetValue(target);
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[{Constants.ModName}] Could not read {target.GetType().Name}.{fieldName}: {ex.Message}");
                return false;
            }
        }

        private static bool TrySetField(object? target, string fieldName, object? value)
        {
            if (target == null || string.IsNullOrWhiteSpace(fieldName))
            {
                return false;
            }

            FieldInfo? field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null)
            {
                return false;
            }

            try
            {
                object? converted = ConvertValue(value, field.FieldType);
                field.SetValue(target, converted);
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[{Constants.ModName}] Could not set {target.GetType().Name}.{fieldName}: {ex.Message}");
                return false;
            }
        }

        private static string ToBackingFieldName(string propertyName)
        {
            switch (propertyName)
            {
                case "renderScale":
                    return "m_RenderScale";
                case "shadowDistance":
                    return "m_ShadowDistance";
                case "additionalLightsRenderingMode":
                    return "m_AdditionalLightsRenderingMode";
                case "maxAdditionalLightsCount":
                    return "m_AdditionalLightsPerObjectLimit";
                case "supportsSoftShadows":
                    return "m_SoftShadowsSupported";
                case "upscalingFilter":
                    return "m_UpscalingFilter";
                case "fsrOverrideSharpness":
                    return "m_FsrOverrideSharpness";
                case "fsrSharpness":
                    return "m_FsrSharpness";
                default:
                    return string.Empty;
            }
        }

        private static string QuoteJson(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static object? ConvertValue(object? value, Type targetType)
        {
            if (value == null)
            {
                return null;
            }

            Type valueType = value.GetType();
            if (targetType.IsAssignableFrom(valueType))
            {
                return value;
            }

            if (targetType.IsEnum)
            {
                if (value is string text && Enum.TryParse(targetType, text, true, out object parsed))
                {
                    return parsed;
                }

                return Enum.ToObject(targetType, Convert.ToInt32(value));
            }

            return Convert.ChangeType(value, targetType);
        }

        private static LightRenderingMode ToLightRenderingMode(object? value)
        {
            if (value is LightRenderingMode typed)
            {
                return typed;
            }

            if (TryParseLightRenderingMode(value, out LightRenderingMode parsed))
            {
                return parsed;
            }

            return (LightRenderingMode)Convert.ToInt32(value);
        }

        private static object NormalizeLightRenderingMode(object? value)
        {
            if (TryParseLightRenderingMode(value, out LightRenderingMode mode))
            {
                return mode;
            }

            return value?.ToString() ?? string.Empty;
        }

        private static bool TryParseLightRenderingMode(object? value, out LightRenderingMode mode)
        {
            mode = LightRenderingMode.PerVertex;
            if (value == null)
            {
                return false;
            }

            string text = value.ToString() ?? string.Empty;
            if (Enum.TryParse(text, true, out mode))
            {
                return true;
            }

            if (int.TryParse(text, out int numeric))
            {
                mode = (LightRenderingMode)numeric;
                return true;
            }

            if (text.IndexOf("Disabled", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                mode = LightRenderingMode.Disabled;
                return true;
            }

            if (text.IndexOf("PerPixel", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                mode = LightRenderingMode.PerPixel;
                return true;
            }

            if (text.IndexOf("PerVertex", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                mode = LightRenderingMode.PerVertex;
                return true;
            }

            return false;
        }

        private static int GetUnityObjectId(object target)
        {
            if (target is UnityEngine.Object unityObject)
            {
                return unityObject.GetInstanceID();
            }

            return 0;
        }

        private static float[] CopyLayerCullDistances(float[] distances)
        {
            float[] copy = new float[32];
            if (distances == null)
            {
                return copy;
            }

            Array.Copy(distances, copy, Math.Min(distances.Length, copy.Length));
            return copy;
        }

        private static float[] BuildLayerCullDistances(float[] original, float maxDistance)
        {
            float[] distances = CopyLayerCullDistances(original);
            for (int index = 0; index < distances.Length; index++)
            {
                distances[index] = distances[index] > 0f ? Math.Min(distances[index], maxDistance) : maxDistance;
            }

            return distances;
        }
    }
}


