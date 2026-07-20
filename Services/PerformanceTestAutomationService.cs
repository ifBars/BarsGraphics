using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using MelonLoader;
using BarsGraphics.Config;
using BarsGraphics.Models;
using BarsGraphics.UI;
using BarsGraphics.Utils;
using UnityEngine;
#if MONO
using GameConsole = ScheduleOne.Console;
using GameHud = ScheduleOne.UI.HUD;
using GameLoadManager = ScheduleOne.Persistence.LoadManager;
using GameSaveInfo = ScheduleOne.Persistence.SaveInfo;
#else
using GameConsole = Il2CppScheduleOne.Console;
using GameHud = Il2CppScheduleOne.UI.HUD;
using GameLoadManager = Il2CppScheduleOne.Persistence.LoadManager;
using GameSaveInfo = Il2CppScheduleOne.Persistence.SaveInfo;
#endif

namespace BarsGraphics.Services
{
    internal sealed class PerformanceTestAutomationService
    {
        private enum Phase
        {
            WaitingForLoadManager,
            LoadingSave,
            WaitingForGameLoaded,
            InitialSettling,
            StartingProfile,
            WarmingUp,
            Sampling,
            Complete
        }

        private readonly ModConfig _config;
        private readonly PerformanceMenuService? _menu;
        private readonly List<string> _profiles = new List<string>();
        private readonly List<float> _samples = new List<float>();

        private Phase _phase = Phase.WaitingForLoadManager;
        private int _profileIndex = -1;
        private float _phaseStartedAt;
        private float _nextSampleAt;
        private bool _started;
        private bool _submittedShowFps;
        private bool _capturedProfileScreenshot;
        private bool _capturedUiMenuScreenshot;
        private float _uiMenuScreenshotShownAt = -1f;
        private float _hideUiMenuScreenshotAt = -1f;

        public PerformanceTestAutomationService(ModConfig config, PerformanceMenuService? menu)
        {
            _config = config;
            _menu = menu;
        }

        public void Update()
        {
            if (!_config.IsPerfTestAutomationEnabled() || _phase == Phase.Complete)
            {
                return;
            }

            try
            {
                UpdateCore();
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[{Constants.ModName}][PerfTest] Automation failed in {_phase}: {ex}");
                _phase = Phase.Complete;
            }
        }

        private void UpdateCore()
        {
            switch (_phase)
            {
                case Phase.WaitingForLoadManager:
                    if (GameLoadManager.InstanceExists)
                    {
                        BeginAutomation();
                    }
                    break;
                case Phase.LoadingSave:
                    TryLoadSave();
                    break;
                case Phase.WaitingForGameLoaded:
                    if (IsGameLoaded())
                    {
                        SubmitShowFps();
                        _phaseStartedAt = Time.unscaledTime;
                        _phase = Phase.InitialSettling;
                        MelonLogger.Msg($"[{Constants.ModName}][PerfTest] INITIAL_SETTLE duration={GetInitialSettleSeconds():0.##}s");
                    }
                    break;
                case Phase.InitialSettling:
                    TryCaptureUiMenuScreenshot();
                    if (Time.unscaledTime - _phaseStartedAt >= GetInitialSettleSeconds())
                    {
                        BeginNextProfile();
                    }
                    break;
                case Phase.StartingProfile:
                    BeginNextProfile();
                    break;
                case Phase.WarmingUp:
                    if (Time.unscaledTime - _phaseStartedAt >= GetWarmupSeconds())
                    {
                        CaptureProfileScreenshot();
                        _samples.Clear();
                        _nextSampleAt = 0f;
                        _phaseStartedAt = Time.unscaledTime;
                        _phase = Phase.Sampling;
                        MelonLogger.Msg($"[{Constants.ModName}][PerfTest] SAMPLE_START profile={CurrentProfile()} duration={GetSampleSeconds():0.##}s");
                    }
                    break;
                case Phase.Sampling:
                    SampleFps();
                    if (Time.unscaledTime - _phaseStartedAt >= GetSampleSeconds())
                    {
                        CompleteProfile();
                        _phase = Phase.StartingProfile;
                    }
                    break;
            }
        }

        private void BeginAutomation()
        {
            if (_started)
            {
                return;
            }

            _started = true;
            ParseProfiles();
            MelonLogger.Msg($"[{Constants.ModName}][PerfTest] START profiles={string.Join(",", _profiles.ToArray())} autoLoad={_config.AutoLoadSave?.Value ?? false} slot={_config.AutoLoadSaveSlot?.Value ?? 0}");

            if (_config.AutoLoadSave?.Value ?? false)
            {
                _phase = Phase.LoadingSave;
                return;
            }

            _phase = IsGameLoaded() ? Phase.StartingProfile : Phase.WaitingForGameLoaded;
        }

        private void TryLoadSave()
        {
            if (!GameLoadManager.InstanceExists)
            {
                return;
            }

            GameLoadManager manager = GameLoadManager.Instance;
            if (manager.IsGameLoaded || manager.IsLoading)
            {
                _phase = Phase.WaitingForGameLoaded;
                return;
            }

            manager.RefreshSaveInfo();

            int slot = _config.AutoLoadSaveSlot?.Value ?? 0;
            GameSaveInfo? saveInfo = null;
            string saveLabel = "LastPlayed";

            if (slot >= 1 && slot <= GameLoadManager.SaveGames.Length)
            {
                saveInfo = GameLoadManager.SaveGames[slot - 1];
                saveLabel = $"SaveGame_{slot}";
            }

            if (saveInfo == null)
            {
                saveInfo = GameLoadManager.LastPlayedGame;
                saveLabel = "LastPlayed";
            }

            if (saveInfo == null)
            {
                MelonLogger.Warning($"[{Constants.ModName}][PerfTest] No save info found yet; waiting.");
                return;
            }

            MelonLogger.Msg($"[{Constants.ModName}][PerfTest] Loading save {saveLabel} via LoadManager.StartGame.");
            manager.StartGame(saveInfo, false, true);
            _phase = Phase.WaitingForGameLoaded;
        }

        private bool IsGameLoaded()
        {
            return GameLoadManager.InstanceExists && GameLoadManager.Instance.IsGameLoaded && GameLoadManager.Instance.IsInGameScene;
        }

        private void SubmitShowFps()
        {
            if (_submittedShowFps)
            {
                return;
            }

            GameConsole.SubmitCommand("showfps");
            _submittedShowFps = true;
            MelonLogger.Msg($"[{Constants.ModName}][PerfTest] Executed native console command: showfps");
        }

        private void BeginNextProfile()
        {
            _profileIndex++;
            if (_profileIndex >= _profiles.Count)
            {
                FinishAutomation();
                return;
            }

            string profile = CurrentProfile();
            _config.SetActiveProfile(profile);
            _capturedProfileScreenshot = false;
            _phaseStartedAt = Time.unscaledTime;
            _phase = Phase.WarmingUp;
            MelonLogger.Msg($"[{Constants.ModName}][PerfTest] PROFILE_START profile={profile} warmup={GetWarmupSeconds():0.##}s");
        }

        private void SampleFps()
        {
            if (Time.unscaledTime < _nextSampleAt)
            {
                return;
            }

            _nextSampleAt = Time.unscaledTime + 1f;
            if (!TryReadNativeFpsLabel(out float fps, out string label))
            {
                MelonLogger.Warning($"[{Constants.ModName}][PerfTest] FPS_SAMPLE profile={CurrentProfile()} unavailable");
                return;
            }

            _samples.Add(fps);
            MelonLogger.Msg($"[{Constants.ModName}][PerfTest] FPS_SAMPLE profile={CurrentProfile()} fps={fps:0.##} label=\"{label}\"");
        }

        private void CompleteProfile()
        {
            if (_samples.Count == 0)
            {
                MelonLogger.Warning($"[{Constants.ModName}][PerfTest] PROFILE_RESULT profile={CurrentProfile()} samples=0");
                return;
            }

            float min = float.MaxValue;
            float max = float.MinValue;
            float sum = 0f;
            foreach (float sample in _samples)
            {
                min = Math.Min(min, sample);
                max = Math.Max(max, sample);
                sum += sample;
            }

            float avg = sum / _samples.Count;
            MelonLogger.Msg($"[{Constants.ModName}][PerfTest] PROFILE_RESULT profile={CurrentProfile()} samples={_samples.Count} avg={avg:0.##} min={min:0.##} max={max:0.##}");
        }

        private void FinishAutomation()
        {
            if (_config.RestoreOffAfterPerfTest?.Value ?? true)
            {
                _config.SetActiveProfile("Off");
            }

            _phase = Phase.Complete;
            MelonLogger.Msg($"[{Constants.ModName}][PerfTest] COMPLETE");

            if (_config.QuitGameAfterPerfTest?.Value ?? true)
            {
                MelonLogger.Msg($"[{Constants.ModName}][PerfTest] Quitting game after automated performance test.");
                Application.Quit();
            }
        }

        private void CaptureProfileScreenshot()
        {
            if (_capturedProfileScreenshot || !(_config.CaptureProfileScreenshots?.Value ?? true))
            {
                return;
            }

            _capturedProfileScreenshot = true;

            try
            {
                string directory = Path.Combine(Application.persistentDataPath, Constants.ModName, "Screenshots");
                Directory.CreateDirectory(directory);

                string fileName = $"{DateTime.Now:yyyyMMdd-HHmmss}-{SanitizeFileName(CurrentProfile())}.png";
                string path = Path.Combine(directory, fileName);
                ScreenCapture.CaptureScreenshot(path);
                MelonLogger.Msg($"[{Constants.ModName}][PerfTest] SCREENSHOT profile={CurrentProfile()} path=\"{path}\"");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[{Constants.ModName}][PerfTest] Screenshot capture failed for {CurrentProfile()}: {ex.Message}");
            }
        }

        private void TryCaptureUiMenuScreenshot()
        {
            if (_hideUiMenuScreenshotAt > 0f && Time.unscaledTime >= _hideUiMenuScreenshotAt)
            {
                _menu?.Hide();
                _hideUiMenuScreenshotAt = -1f;
            }

            if (_capturedUiMenuScreenshot || !(_config.CaptureUiMenuScreenshot?.Value ?? false))
            {
                return;
            }

            if (_menu == null)
            {
                MelonLogger.Warning($"[{Constants.ModName}][PerfTest] UI menu screenshot requested but menu service is unavailable.");
                _capturedUiMenuScreenshot = true;
                return;
            }

            if (_uiMenuScreenshotShownAt < 0f)
            {
                _menu.Show();
                _uiMenuScreenshotShownAt = Time.unscaledTime;
                MelonLogger.Msg($"[{Constants.ModName}][PerfTest] UI menu opened for screenshot.");
                return;
            }

            if (Time.unscaledTime - _uiMenuScreenshotShownAt < 1f)
            {
                return;
            }

            _capturedUiMenuScreenshot = true;

            try
            {
                string directory = Path.Combine(Application.persistentDataPath, Constants.ModName, "Screenshots");
                Directory.CreateDirectory(directory);

                string path = Path.Combine(directory, $"{DateTime.Now:yyyyMMdd-HHmmss}-ui-menu.png");
                ScreenCapture.CaptureScreenshot(path);
                MelonLogger.Msg($"[{Constants.ModName}][PerfTest] UI_SCREENSHOT path=\"{path}\"");
                _hideUiMenuScreenshotAt = Time.unscaledTime + 1f;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[{Constants.ModName}][PerfTest] UI screenshot capture failed: {ex.Message}");
            }
        }

        private static string SanitizeFileName(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalid, '_');
            }

            return string.IsNullOrWhiteSpace(value) ? "profile" : value;
        }

        private bool TryReadNativeFpsLabel(out float fps, out string label)
        {
            fps = 0f;
            label = string.Empty;

            if (!GameHud.InstanceExists || GameHud.Instance.fpsLabel == null)
            {
                return false;
            }

            label = GameHud.Instance.fpsLabel.text ?? string.Empty;
            string numeric = label.Replace("FPS", string.Empty).Trim();
            return float.TryParse(numeric, NumberStyles.Float, CultureInfo.InvariantCulture, out fps);
        }

        private void ParseProfiles()
        {
            _profiles.Clear();
            string raw = _config.PerfTestProfileSequence?.Value ?? string.Empty;
            string[] parts = raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                string profile = part.Trim();
                if (profile.Length > 0)
                {
                    _profiles.Add(OptimizationProfileCatalog.Normalize(profile));
                }
            }

            if (_profiles.Count == 0)
            {
                _profiles.Add("Off");
                _profiles.Add("Balanced");
            }
            else if (!string.Equals(_profiles[0], "Off", StringComparison.OrdinalIgnoreCase))
            {
                _profiles.Insert(0, "Off");
                MelonLogger.Warning($"[{Constants.ModName}][PerfTest] Inserted Off as the first profile so every run captures a base-game FPS and screenshot baseline.");
            }
        }

        private string CurrentProfile()
        {
            if (_profileIndex < 0 || _profileIndex >= _profiles.Count)
            {
                return "Off";
            }

            return _profiles[_profileIndex];
        }

        private float GetWarmupSeconds()
        {
            return Math.Max(1f, _config.PerfTestWarmupSeconds?.Value ?? 8f);
        }

        private float GetInitialSettleSeconds()
        {
            return Math.Max(0f, _config.PerfTestInitialSettleSeconds?.Value ?? 5f);
        }

        private float GetSampleSeconds()
        {
            return Math.Max(3f, _config.PerfTestSampleSeconds?.Value ?? 20f);
        }
    }
}


