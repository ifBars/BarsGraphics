using System;
using BarsGraphics.Config;
using BarsGraphics.Models;
using BarsGraphics.Utils;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BarsGraphics.Services
{
    internal sealed class VisualEnhancementService
    {
        private const float VolumePriority = 10000f;

        private readonly ModConfig _config;
        private GameObject? _volumeObject;
        private Volume? _volume;
        private VolumeProfile? _profile;
        private ColorAdjustments? _colorAdjustments;
        private WhiteBalance? _whiteBalance;
        private string _lastSignature = string.Empty;
        private bool _unavailable;

        public VisualEnhancementService(ModConfig config)
        {
            _config = config;
        }

        public void Update()
        {
            if (_unavailable)
            {
                return;
            }

            string styleId = _config.GetVisualStyle();
            float intensity = _config.GetVisualStyleIntensity();
            bool suppressedByOptimizer = IsSuppressedByOptimizer();
            string signature = $"{styleId}|{intensity:0.000}|{suppressedByOptimizer}";

            if (string.Equals(signature, _lastSignature, StringComparison.Ordinal))
            {
                return;
            }

            try
            {
                Apply(VisualStyleCatalog.Get(styleId), intensity, suppressedByOptimizer);
                _lastSignature = signature;
            }
            catch (Exception ex)
            {
                _unavailable = true;
                DisableVolume();
                MelonLogger.Warning($"[{Constants.ModName}] Visual enhancements are unavailable for this session: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_volumeObject != null)
            {
                UnityEngine.Object.Destroy(_volumeObject);
            }

            if (_profile != null)
            {
                UnityEngine.Object.Destroy(_profile);
            }

            _volumeObject = null;
            _volume = null;
            _profile = null;
            _colorAdjustments = null;
            _whiteBalance = null;
            _lastSignature = string.Empty;
        }

        private void Apply(VisualStyleDefinition style, float intensity, bool suppressedByOptimizer)
        {
            bool enabled = !string.Equals(style.Id, "Off", StringComparison.Ordinal) &&
                intensity > 0f && !suppressedByOptimizer;
            if (!enabled)
            {
                DisableVolume();
                return;
            }

            EnsureVolume();
            if (_volume == null || _colorAdjustments == null || _whiteBalance == null)
            {
                throw new InvalidOperationException("URP Volume components could not be created.");
            }

            float amount = Mathf.Clamp01(intensity);
            _colorAdjustments.postExposure.value = style.PostExposure;
            _colorAdjustments.contrast.value = style.Contrast;
            _colorAdjustments.hueShift.value = style.HueShift;
            _colorAdjustments.saturation.value = style.Saturation;
            _colorAdjustments.colorFilter.value = style.ColorFilter;
            _whiteBalance.temperature.value = style.Temperature;
            _whiteBalance.tint.value = style.Tint;
            _volume.weight = amount;
            _volume.enabled = true;

            if (_config.ShouldLogDiagnostics())
            {
                MelonLogger.Msg($"[{Constants.ModName}] Applied visual style {style.Label} at {amount:0.00} intensity.");
            }
        }

        private void EnsureVolume()
        {
            if (_volumeObject != null && _volume != null && _profile != null &&
                _colorAdjustments != null && _whiteBalance != null)
            {
                return;
            }

            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _profile.name = $"{Constants.ModName} Visual Style";
            _colorAdjustments = _profile.Add<ColorAdjustments>(true);
            _whiteBalance = _profile.Add<WhiteBalance>(true);

            _volumeObject = new GameObject($"{Constants.ModName} Visual Style Volume");
            _volumeObject.hideFlags = HideFlags.HideAndDontSave;
            _volumeObject.layer = ResolveVolumeLayer();
            UnityEngine.Object.DontDestroyOnLoad(_volumeObject);

            _volume = _volumeObject.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = VolumePriority;
            _volume.weight = 1f;
            _volume.sharedProfile = _profile;
        }

        private void DisableVolume()
        {
            if (_volume != null)
            {
                _volume.enabled = false;
            }
        }

        private bool IsSuppressedByOptimizer()
        {
            if (!_config.IsOptimizerEnabled())
            {
                return false;
            }

            EffectiveRenderSettings settings = EffectiveRenderSettings.FromProfile(_config, _config.GetActiveProfile());
            return settings.DisablePostProcessing;
        }

        private static int ResolveVolumeLayer()
        {
            foreach (Volume existingVolume in UnityEngine.Object.FindObjectsOfType<Volume>())
            {
                if (existingVolume != null && existingVolume.isGlobal && existingVolume.gameObject.activeInHierarchy)
                {
                    return existingVolume.gameObject.layer;
                }
            }

            return 0;
        }
    }
}
