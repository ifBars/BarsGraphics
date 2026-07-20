using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using MelonLoader;
using BarsGraphics.Config;
using BarsGraphics.Models;
using BarsGraphics.Utils;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
#if IL2CPP
using Il2CppInterop.Runtime.InteropTypes;
#endif
#if MONO
using GameConsole = ScheduleOne.Console;
using GameHud = ScheduleOne.UI.HUD;
using GameLoadManager = ScheduleOne.Persistence.LoadManager;
using GameSaveInfo = ScheduleOne.Persistence.SaveInfo;
using GameTimeManager = ScheduleOne.GameTime.TimeManager;
using GameOffenceNotice = ScheduleOne.UI.OffenceNoticeUI;
using GamePlayerCamera = ScheduleOne.PlayerScripts.PlayerCamera;
using GamePlayerInventory = ScheduleOne.PlayerScripts.PlayerInventory;
using GamePlayerMovement = ScheduleOne.PlayerScripts.PlayerMovement;
#else
using GameConsole = Il2CppScheduleOne.Console;
using GameHud = Il2CppScheduleOne.UI.HUD;
using GameLoadManager = Il2CppScheduleOne.Persistence.LoadManager;
using GameSaveInfo = Il2CppScheduleOne.Persistence.SaveInfo;
using GameTimeManager = Il2CppScheduleOne.GameTime.TimeManager;
using GameOffenceNotice = Il2CppScheduleOne.UI.OffenceNoticeUI;
using GamePlayerCamera = Il2CppScheduleOne.PlayerScripts.PlayerCamera;
using GamePlayerInventory = Il2CppScheduleOne.PlayerScripts.PlayerInventory;
using GamePlayerMovement = Il2CppScheduleOne.PlayerScripts.PlayerMovement;
#endif

namespace BarsGraphics.Services
{
    internal sealed class LiveControlService : IDisposable
    {
        private const float MaxSampleSeconds = 90f;
        private const int RequestTimeoutSeconds = 120;

        private readonly ModConfig _config;
        private readonly RenderOptimizerService _optimizer;
        private readonly ConcurrentQueue<LiveRequest> _requests = new ConcurrentQueue<LiveRequest>();
        private readonly object _lifecycleLock = new object();
        private readonly Dictionary<int, LiveRendererSnapshot> _liveRendererSnapshots = new Dictionary<int, LiveRendererSnapshot>();
        private readonly Dictionary<int, LiveCameraStackSnapshot> _liveCameraStackSnapshots = new Dictionary<int, LiveCameraStackSnapshot>();
        private readonly Dictionary<int, LiveLightSnapshot> _liveLightSnapshots = new Dictionary<int, LiveLightSnapshot>();
        private readonly Dictionary<int, LiveRendererFeatureSnapshot> _liveRendererFeatureSnapshots = new Dictionary<int, LiveRendererFeatureSnapshot>();
        private readonly Dictionary<int, LiveUrpSnapshot> _liveUrpSnapshots = new Dictionary<int, LiveUrpSnapshot>();
        private readonly Dictionary<int, LiveMaterialSnapshot> _liveMaterialSnapshots = new Dictionary<int, LiveMaterialSnapshot>();
        private readonly Dictionary<int, LiveTerrainSnapshot> _liveTerrainSnapshots = new Dictionary<int, LiveTerrainSnapshot>();
        private int? _liveGlobalTextureMipmapLimitSnapshot;
        private int? _liveShaderMaximumLodSnapshot;
        private AnisotropicFiltering? _liveAnisotropicFilteringSnapshot;
        private TcpListener? _listener;
        private Thread? _listenerThread;
        private volatile bool _running;
        private FpsSample? _activeSample;

        public LiveControlService(ModConfig config, RenderOptimizerService optimizer)
        {
            _config = config;
            _optimizer = optimizer;
        }

        public void Initialize()
        {
            if (!_config.IsLiveControlEnabled())
            {
                return;
            }

            lock (_lifecycleLock)
            {
                if (_running)
                {
                    return;
                }

                int port = _config.GetLiveControlPort();
                _listener = new TcpListener(IPAddress.Loopback, port);
                _listener.Start();
                _running = true;
                _listenerThread = new Thread(AcceptLoop)
                {
                    IsBackground = true,
                    Name = $"{Constants.ModName} LiveControl"
                };
                _listenerThread.Start();
                MelonLogger.Msg($"[{Constants.ModName}][Live] Listening on 127.0.0.1:{port}.");
            }
        }

        public void Update()
        {
            UpdateSample();

            int processed = 0;
            while (processed < 8 && _requests.TryDequeue(out LiveRequest? request))
            {
                processed++;
                try
                {
                    if (_activeSample != null)
                    {
                        request.Complete(Error("busy", "FPS sampling is already running."));
                        continue;
                    }

                    HandleRequest(request);
                }
                catch (Exception ex)
                {
                    request.Complete(Error("exception", ex.Message));
                }
            }
        }

        public void Dispose()
        {
            _running = false;
            try
            {
                _listener?.Stop();
            }
            catch
            {
                // Best effort during game shutdown.
            }

            _listener = null;
        }

        private void AcceptLoop()
        {
            while (_running)
            {
                try
                {
                    TcpClient client = _listener!.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(_ => HandleClient(client));
                }
                catch (SocketException)
                {
                    if (_running)
                    {
                        MelonLogger.Warning($"[{Constants.ModName}][Live] Listener socket failed.");
                    }
                }
                catch (Exception ex)
                {
                    if (_running)
                    {
                        MelonLogger.Warning($"[{Constants.ModName}][Live] Listener failed: {ex.Message}");
                    }
                }
            }
        }

        private void HandleClient(TcpClient client)
        {
            try
            {
                using (client)
                {
                    client.ReceiveTimeout = 5000;
                    client.SendTimeout = 20000;

                    using NetworkStream stream = client.GetStream();
                    using StreamReader reader = new StreamReader(stream, Encoding.UTF8, false, 1024, true);
                    using StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true)
                    {
                        AutoFlush = true,
                        NewLine = "\n"
                    };

                    string? line = reader.ReadLine();
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        writer.WriteLine(Error("bad_request", "Expected one JSON object per line."));
                        return;
                    }

                    LiveRequest request = new LiveRequest(line);
                    _requests.Enqueue(request);
                    if (!request.Wait())
                    {
                        writer.WriteLine(Error("timeout", "Live command timed out."));
                        return;
                    }

                    writer.WriteLine(request.Response);
                }
            }
            catch (IOException)
            {
                // Client-side probes use short timeouts during scene loads; disconnects should not kill the game.
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void HandleRequest(LiveRequest request)
        {
            string method = Json.GetString(request.Line, "method");
            switch (method)
            {
                case "help":
                    request.Complete(Ok("\"methods\":[\"help\",\"state\",\"optimizationImpacts\",\"loadSave\",\"apply\",\"restore\",\"quietTime\",\"closeOffenceNotice\",\"restoreHud\",\"console\",\"showfps\",\"sampleFps\",\"screenshot\",\"cameraStats\",\"lightStats\",\"pipelineStats\",\"urpStats\",\"rendererFeatureStats\",\"meshStats\",\"materialInstancingStats\",\"setMaterialInstancing\",\"restoreMaterialInstancing\",\"setCameraStacks\",\"restoreCameraStacks\",\"setRendererFeatures\",\"restoreRendererFeatures\",\"setUrpAsset\",\"setUrpRenderer\",\"restoreUrp\",\"setLights\",\"restoreLights\",\"setRenderers\",\"restoreRenderers\",\"setGlobalTextureMipmapLimit\",\"restoreGlobalTextureMipmapLimit\",\"setMasterTextureLimit\",\"restoreMasterTextureLimit\",\"setShaderMaximumLod\",\"restoreShaderMaximumLod\",\"setAnisotropicFiltering\",\"restoreAnisotropicFiltering\",\"setTerrainFoliage\",\"restoreTerrainFoliage\"]"));
                    break;
                case "state":
                    request.Complete(StateResponse());
                    break;
                case "optimizationImpacts":
                    request.Complete(OptimizationImpacts());
                    break;
                case "loadSave":
                    request.Complete(LoadSave(request.Line));
                    break;
                case "apply":
                    Apply(request.Line);
                    request.Complete(StateResponse());
                    break;
                case "restore":
                    _config.SetActiveProfile("Off", false);
                    _optimizer.Restore();
                    RestoreCameraStacks();
                    RestoreRendererFeatures();
                    RestoreUrp();
                    RestoreLiveLights();
                    RestoreLiveRenderers();
                    RestoreMaterialInstancing();
                    RestoreGlobalTextureMipmapLimit();
                    RestoreShaderMaximumLod();
                    RestoreAnisotropicFiltering();
                    RestoreTerrainFoliage();
                    request.Complete(StateResponse());
                    break;
                case "quietTime":
                    request.Complete(QuietTime(request.Line));
                    break;
                case "closeOffenceNotice":
                    request.Complete(CloseOffenceNotice());
                    break;
                case "restoreHud":
                    request.Complete(RestoreHud());
                    break;
                case "console":
                    request.Complete(RunConsoleCommand(request.Line));
                    break;
                case "showfps":
                    GameConsole.SubmitCommand("showfps");
                    request.Complete(Ok("\"showfps\":true"));
                    break;
                case "sampleFps":
                    StartSample(request);
                    break;
                case "screenshot":
                    request.Complete(CaptureScreenshot(Json.GetString(request.Line, "name")));
                    break;
                case "cameraStats":
                    request.Complete(CameraStats());
                    break;
                case "lightStats":
                    request.Complete(LightStats());
                    break;
                case "pipelineStats":
                    request.Complete(PipelineStats());
                    break;
                case "urpStats":
                    request.Complete(UrpStats());
                    break;
                case "rendererFeatureStats":
                    request.Complete(RendererFeatureStats());
                    break;
                case "meshStats":
                    request.Complete(MeshStats(request.Line));
                    break;
                case "materialInstancingStats":
                    request.Complete(MaterialInstancingStats(request.Line));
                    break;
                case "setMaterialInstancing":
                    request.Complete(SetMaterialInstancing(request.Line));
                    break;
                case "restoreMaterialInstancing":
                    request.Complete(RestoreMaterialInstancing());
                    break;
                case "setCameraStacks":
                    request.Complete(SetCameraStacks(request.Line));
                    break;
                case "restoreCameraStacks":
                    request.Complete(RestoreCameraStacks());
                    break;
                case "setRendererFeatures":
                    request.Complete(SetRendererFeatures(request.Line));
                    break;
                case "restoreRendererFeatures":
                    request.Complete(RestoreRendererFeatures());
                    break;
                case "setUrpAsset":
                    request.Complete(SetUrpAsset(request.Line));
                    break;
                case "setUrpRenderer":
                    request.Complete(SetUrpRenderer(request.Line));
                    break;
                case "restoreUrp":
                    request.Complete(RestoreUrp());
                    break;
                case "setLights":
                    request.Complete(SetLights(request.Line));
                    break;
                case "restoreLights":
                    request.Complete(RestoreLiveLights());
                    break;
                case "setRenderers":
                    request.Complete(SetRenderers(request.Line));
                    break;
                case "restoreRenderers":
                    request.Complete(RestoreLiveRenderers());
                    break;
                case "setGlobalTextureMipmapLimit":
                case "setMasterTextureLimit":
                    request.Complete(SetGlobalTextureMipmapLimit(request.Line));
                    break;
                case "restoreGlobalTextureMipmapLimit":
                case "restoreMasterTextureLimit":
                    request.Complete(RestoreGlobalTextureMipmapLimit());
                    break;
                case "setShaderMaximumLod":
                    request.Complete(SetShaderMaximumLod(request.Line));
                    break;
                case "restoreShaderMaximumLod":
                    request.Complete(RestoreShaderMaximumLod());
                    break;
                case "setAnisotropicFiltering":
                    request.Complete(SetAnisotropicFiltering(request.Line));
                    break;
                case "restoreAnisotropicFiltering":
                    request.Complete(RestoreAnisotropicFiltering());
                    break;
                case "setTerrainFoliage":
                    request.Complete(SetTerrainFoliage(request.Line));
                    break;
                case "restoreTerrainFoliage":
                    request.Complete(RestoreTerrainFoliage());
                    break;
                default:
                    MelonLogger.Warning($"[{Constants.ModName}][Live] Unknown method from request: {request.Line}");
                    request.Complete(Error("unknown_method", $"Unknown method '{method}'."));
                    break;
            }
        }

        private void Apply(string line)
        {
            _optimizer.Restore();
            RestoreCameraStacks();
            RestoreRendererFeatures();
            RestoreUrp();
            RestoreLiveLights();
            RestoreLiveRenderers();
            RestoreMaterialInstancing();
            RestoreGlobalTextureMipmapLimit();
            RestoreShaderMaximumLod();
            RestoreAnisotropicFiltering();
            RestoreTerrainFoliage();

            string requestedProfile = Json.GetString(line, "profile");
            if (!string.IsNullOrWhiteSpace(requestedProfile) &&
                !string.Equals(requestedProfile, "Custom", StringComparison.OrdinalIgnoreCase) &&
                !HasCustomApplyOverrides(line))
            {
                _config.SetActiveProfile(requestedProfile, false);
                MelonLogger.Msg($"[{Constants.ModName}][Live] Applied profile {requestedProfile}.");
                return;
            }

            _config.SetActiveProfile("Custom", false);
            SetBool(_config.EnableOptimizer, line, "enableOptimizer");
            SetBool(_config.LogDiagnostics, line, "diagnostics");

            SetBool(_config.UseRenderScale, line, "useRenderScale");
            SetFloat(_config.RenderScale, line, "renderScale");

            SetBool(_config.UseShadowSettings, line, "useShadowSettings");
            SetFloat(_config.ShadowDistance, line, "shadowDistance");
            SetInt(_config.ShadowCascades, line, "shadowCascades");
            SetInt(_config.ShadowResolution, line, "shadowResolution");

            SetBool(_config.UseLodSettings, line, "useLodSettings");
            SetFloat(_config.LodBias, line, "lodBias");
            SetInt(_config.MaximumLodLevel, line, "maximumLodLevel");
            SetBool(_config.ProtectNearLods, line, "protectNearLods");
            SetFloat(_config.NearLodProtectionDistance, line, "nearLodProtectionDistance");

            SetBool(_config.DisablePostProcessing, line, "disablePostProcessing");
            SetBool(_config.DisableRealtimeReflectionProbes, line, "disableRealtimeReflectionProbes");
            SetBool(_config.DisableReflectionProbes, line, "disableReflectionProbes");
            SetBool(_config.UseFrameRateSettings, line, "useFrameRateSettings");
            SetInt(_config.VSyncCount, line, "vSyncCount");
            SetInt(_config.TargetFrameRate, line, "targetFrameRate");
            SetBool(_config.UsePixelLightCount, line, "usePixelLightCount");
            SetInt(_config.PixelLightCount, line, "pixelLightCount");
            SetBool(_config.UseAntiAliasing, line, "useAntiAliasing");
            SetInt(_config.AntiAliasing, line, "antiAliasing");
            SetBool(_config.UseGlobalTextureMipmapLimit, line, "useGlobalTextureMipmapLimit");
            SetInt(_config.GlobalTextureMipmapLimit, line, "globalTextureMipmapLimit");

            SetBool(_config.UseAdditionalLightsMode, line, "useAdditionalLightsMode");
            SetString(_config.AdditionalLightsMode, line, "additionalLightsMode");
            SetInt(_config.MaxAdditionalLightsCount, line, "maxAdditionalLightsCount");
            SetBool(_config.DisableFarLightShadows, line, "disableFarLightShadows");
            SetFloat(_config.FarLightShadowDistance, line, "farLightShadowDistance");

            SetBool(_config.UseCameraFarClip, line, "useCameraFarClip");
            SetFloat(_config.CameraFarClipDistance, line, "cameraFarClipDistance");
            SetBool(_config.DisableCameraStacks, line, "disableCameraStacks");
            SetBool(_config.UseLayerCullDistances, line, "useLayerCullDistances");
            SetFloat(_config.LayerCullDistance, line, "layerCullDistance");
            SetBool(_config.DisableCameraOcclusionCulling, line, "disableCameraOcclusionCulling");
            SetBool(_config.DisableVolumetricLightBeams, line, "disableVolumetricLightBeams");
            SetBool(_config.UseVisibilitySafeRendererCulling, line, "useVisibilitySafeRendererCulling");
            SetFloat(_config.RendererCullMinDistance, line, "rendererCullMinDistance");
            SetBool(_config.DisableOutlineFeature, line, "disableOutlineFeature");
            SetBool(_config.EnableInteractionHoverThrottle, line, "enableInteractionHoverThrottle");
            SetFloat(_config.InteractionHoverThrottleHz, line, "interactionHoverThrottleHz");
            SetBool(_config.EnableWeatherEntityThrottle, line, "enableWeatherEntityThrottle");
            SetFloat(_config.WeatherEntityThrottleHz, line, "weatherEntityThrottleHz");

            MelonLogger.Msg($"[{Constants.ModName}][Live] Applied custom settings.");
        }

        private static bool HasCustomApplyOverrides(string line)
        {
            string[] keys =
            {
                "useRenderScale",
                "renderScale",
                "useShadowSettings",
                "shadowDistance",
                "shadowCascades",
                "shadowResolution",
                "useLodSettings",
                "lodBias",
                "maximumLodLevel",
                "protectNearLods",
                "nearLodProtectionDistance",
                "disablePostProcessing",
                "disableRealtimeReflectionProbes",
                "disableReflectionProbes",
                "useFrameRateSettings",
                "vSyncCount",
                "targetFrameRate",
                "usePixelLightCount",
                "pixelLightCount",
                "useAntiAliasing",
                "antiAliasing",
                "useGlobalTextureMipmapLimit",
                "globalTextureMipmapLimit",
                "useAdditionalLightsMode",
                "additionalLightsMode",
                "maxAdditionalLightsCount",
                "disableFarLightShadows",
                "farLightShadowDistance",
                "useCameraFarClip",
                "cameraFarClipDistance",
                "disableCameraStacks",
                "useLayerCullDistances",
                "layerCullDistance",
                "disableCameraOcclusionCulling",
                "disableVolumetricLightBeams",
                "useVisibilitySafeRendererCulling",
                "rendererCullMinDistance",
                "disableOutlineFeature",
                "enableInteractionHoverThrottle",
                "interactionHoverThrottleHz",
                "enableWeatherEntityThrottle",
                "weatherEntityThrottleHz"
            };

            foreach (string key in keys)
            {
                if (line.IndexOf("\"" + key + "\"", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private void StartSample(LiveRequest request)
        {
            if (!IsGameLoaded())
            {
                request.Complete(Error("not_loaded", "Gameplay scene is not loaded. Run loadSave first and wait for isInGameScene=true."));
                return;
            }

            float seconds = Json.GetFloat(request.Line, "seconds", 4f);
            seconds = Math.Max(1f, Math.Min(MaxSampleSeconds, seconds));
            _activeSample = new FpsSample(request, Time.unscaledTime + seconds, seconds);
        }

        private string LoadSave(string line)
        {
            if (!GameLoadManager.InstanceExists)
            {
                return Error("load_manager_unavailable", "LoadManager is not available yet.");
            }

            GameLoadManager manager = GameLoadManager.Instance;
            if (manager.IsGameLoaded && manager.IsInGameScene)
            {
                return Ok("\"alreadyLoaded\":true,\"isGameLoaded\":true,\"isInGameScene\":true");
            }

            if (manager.IsLoading)
            {
                return Ok("\"loading\":true,\"isGameLoaded\":false,\"isInGameScene\":false");
            }

            manager.RefreshSaveInfo();

            int slot = Json.GetInt(line, "slot", _config.AutoLoadSaveSlot?.Value ?? 0);
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
                return Error("save_unavailable", "No save info is available yet.");
            }

            manager.StartGame(saveInfo, false, true);
            MelonLogger.Msg($"[{Constants.ModName}][Live] Loading save {saveLabel} via LoadManager.StartGame.");
            return Ok($"\"loading\":true,\"save\":{Json.Quote(saveLabel)}");
        }

        private string QuietTime(string line)
        {
            if (!IsGameLoaded())
            {
                return Error("not_loaded", "Gameplay scene is not loaded. Run loadSave first and wait for isInGameScene=true.");
            }

            int time = Json.GetInt(line, "time", 400);
            if (Json.TryGetInt(line, "hour", out int hour))
            {
                int minute = Json.GetInt(line, "minute", 0);
                time = hour * 100 + minute;
            }

            if (!IsValid24HourTime(time))
            {
                return Error("bad_time", "Expected a 24-hour time such as 400, 0400, or hour/minute values.");
            }

            string timeText = time.ToString("0000", CultureInfo.InvariantCulture);
            GameConsole.SubmitCommand("settime " + timeText);

            bool save = Json.GetBool(line, "save", false);
            if (save)
            {
                GameConsole.SubmitCommand("save");
            }

            MelonLogger.Msg($"[{Constants.ModName}][Live] Set quiet test time to {timeText}. save={save}");
            return Ok($"\"quietTime\":true,\"requestedTime\":{Json.Quote(timeText)},\"save\":{Bool(save)},\"currentTime\":{GetCurrentTime()}");
        }

        private string RunConsoleCommand(string line)
        {
            string command = Json.GetString(line, "command");
            if (string.IsNullOrWhiteSpace(command))
            {
                return Error("bad_command", "Expected a non-empty command value.");
            }

            GameConsole.SubmitCommand(command);
            MelonLogger.Msg($"[{Constants.ModName}][Live] Submitted console command: {command}");
            return Ok($"\"console\":{Json.Quote(command)}");
        }

        private string CloseOffenceNotice()
        {
            bool closed = false;
            try
            {
                int matched = 0;
                GameOffenceNotice[] notices = UnityEngine.Object.FindObjectsOfType<GameOffenceNotice>(true);
                for (int index = 0; index < notices.Length; index++)
                {
                    GameOffenceNotice notice = notices[index];
                    if (notice == null)
                    {
                        continue;
                    }

                    matched++;
                    GameObject? container = GetNoticeContainer(notice);
                    if (container != null && container.activeInHierarchy)
                    {
                        container.SetActive(false);
                        closed = true;
                    }
                    else if (notice.gameObject.activeInHierarchy)
                    {
                        notice.gameObject.SetActive(false);
                        closed = true;
                    }
                }

                closed |= CloseOffenceNoticeTextRoots(ref matched);

                if (GamePlayerMovement.InstanceExists)
                {
                    GamePlayerMovement.Instance.CanMove = true;
                }

                if (GamePlayerCamera.InstanceExists)
                {
                    GamePlayerCamera.Instance.RemoveActiveUIElement("OffenceNoticeUI");
                    GamePlayerCamera.Instance.SetCanLook(true);
                    GamePlayerCamera.Instance.SetDoFActive(false, 0.2f);
                }

                if (GamePlayerInventory.InstanceExists)
                {
                    GamePlayerInventory.Instance.SetInventoryEnabled(true);
                }

                if (GameHud.InstanceExists)
                {
                    GameHud.Instance.SetCrosshairVisible(true);
                }
            }
            catch (Exception ex)
            {
                return Error("close_failed", ex.Message);
            }

            return Ok($"\"available\":{Bool(GameOffenceNotice.InstanceExists)},\"matched\":{UnityEngine.Object.FindObjectsOfType<GameOffenceNotice>(true).Length},\"closed\":{Bool(closed)}");
        }

        private static GameObject? GetNoticeContainer(GameOffenceNotice notice)
        {
            FieldInfo? field = typeof(GameOffenceNotice).GetField("container", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null && field.GetValue(notice) is GameObject fieldValue)
            {
                return fieldValue;
            }

            PropertyInfo? property = typeof(GameOffenceNotice).GetProperty("container", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return property == null ? null : property.GetValue(notice) as GameObject;
        }

        private static bool CloseOffenceNoticeTextRoots(ref int matched)
        {
            bool closed = false;
            Text[] texts = UnityEngine.Object.FindObjectsOfType<Text>(true);
            for (int index = 0; index < texts.Length; index++)
            {
                Text text = texts[index];
                if (text == null || string.IsNullOrEmpty(text.text))
                {
                    continue;
                }

                if (!text.text.Contains("OFFENSE NOTICE", StringComparison.OrdinalIgnoreCase) &&
                    !text.text.Contains("OFFENCE NOTICE", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                matched++;
                GameObject? root = GetCanvasChildRoot(text.transform);
                if (root != null && root.activeInHierarchy)
                {
                    root.SetActive(false);
                    closed = true;
                }
            }

            return closed;
        }

        private static GameObject? GetCanvasChildRoot(Transform transform)
        {
            Transform current = transform;
            Transform? parent = transform.parent;
            while (parent != null && parent.GetComponent<Canvas>() == null)
            {
                current = parent;
                parent = parent.parent;
            }

            return current == transform && parent == null ? null : current.gameObject;
        }

        private string RestoreHud()
        {
            if (!IsGameLoaded())
            {
                return Error("not_loaded", "Gameplay scene is not loaded. Run loadSave first and wait for isInGameScene=true.");
            }

            try
            {
                if (GamePlayerCamera.InstanceExists)
                {
                    GamePlayerCamera.Instance.CloseInterface(0f, true);
                    GamePlayerCamera.Instance.SetCanLook(true);
                    GamePlayerCamera.Instance.SetDoFActive(false, 0.2f);
                }

                if (GamePlayerMovement.InstanceExists)
                {
                    GamePlayerMovement.Instance.CanMove = true;
                }

                if (GamePlayerInventory.InstanceExists)
                {
                    GamePlayerInventory.Instance.SetInventoryEnabled(true);
                    GamePlayerInventory.Instance.SetEquippingEnabled(true);
                }

                if (GameHud.InstanceExists)
                {
                    GameHud hud = GameHud.Instance;
                    hud.canvas.enabled = true;
                    hud.gameObject.SetActive(true);
                    hud.HotbarContainer.gameObject.SetActive(true);
                    hud.SetCrosshairVisible(true);
                    hud.fpsLabel.gameObject.SetActive(true);
                    hud.SetBlackOverlayVisible(false, 0f);
                }
            }
            catch (Exception ex)
            {
                return Error("restore_hud_failed", ex.Message);
            }

            return Ok("\"restoredHud\":true");
        }

        private void UpdateSample()
        {
            FpsSample? sample = _activeSample;
            if (sample == null)
            {
                return;
            }

            if (Time.unscaledTime >= sample.NextSampleAt)
            {
                sample.NextSampleAt = Time.unscaledTime + 0.5f;
                if (TryReadNativeFpsLabel(out float fps, out _))
                {
                    sample.Values.Add(fps);
                }
            }

            if (Time.unscaledTime < sample.EndAt)
            {
                return;
            }

            _activeSample = null;
            if (sample.Values.Count == 0)
            {
                sample.Request.Complete(Error("fps_unavailable", "Native FPS label was unavailable. Call showfps after the game is loaded."));
                return;
            }

            float min = float.MaxValue;
            float max = float.MinValue;
            float sum = 0f;
            foreach (float value in sample.Values)
            {
                min = Math.Min(min, value);
                max = Math.Max(max, value);
                sum += value;
            }

            float avg = sum / sample.Values.Count;
            sample.Request.Complete(Ok($"\"samples\":{sample.Values.Count},\"durationSeconds\":{sample.RequestedSeconds.ToString("0.##", CultureInfo.InvariantCulture)},\"avg\":{avg.ToString("0.##", CultureInfo.InvariantCulture)},\"min\":{min.ToString("0.##", CultureInfo.InvariantCulture)},\"max\":{max.ToString("0.##", CultureInfo.InvariantCulture)}"));
        }

        private string CaptureScreenshot(string name)
        {
            string safeName = string.IsNullOrWhiteSpace(name) ? DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) : SanitizeFileName(name);
            string directory = Path.Combine(Application.persistentDataPath, Constants.ModName, "LiveScreenshots");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, $"{safeName}.png");
            ScreenCapture.CaptureScreenshot(path);
            return Ok($"\"path\":{Json.Quote(path)}");
        }

        private string CameraStats()
        {
            Camera[] cameras = UnityEngine.Object.FindObjectsOfType<Camera>();
            StringBuilder items = new StringBuilder();
            int enabled = 0;
            int withPostProcessing = 0;
            int withStacks = 0;
            int stackEntries = 0;

            for (int index = 0; index < cameras.Length; index++)
            {
                Camera camera = cameras[index];
                if (camera == null)
                {
                    continue;
                }

                bool cameraEnabled = camera.enabled && camera.gameObject.activeInHierarchy;
                if (cameraEnabled)
                {
                    enabled++;
                }

                string renderType = string.Empty;
                bool postProcessing = false;
                int stackCount = 0;
                try
                {
                    UniversalAdditionalCameraData data = camera.GetComponent<UniversalAdditionalCameraData>();
                    if (data != null)
                    {
                        renderType = data.renderType.ToString();
                        postProcessing = data.renderPostProcessing;
                        stackCount = data.cameraStack == null ? 0 : data.cameraStack.Count;
                    }
                }
                catch (Exception ex)
                {
                    renderType = "unavailable:" + ex.GetType().Name;
                }

                if (postProcessing)
                {
                    withPostProcessing++;
                }

                if (stackCount > 0)
                {
                    withStacks++;
                    stackEntries += stackCount;
                }

                if (items.Length > 0)
                {
                    items.Append(',');
                }

                items.Append('{')
                    .Append("\"name\":").Append(Json.Quote(camera.name ?? string.Empty)).Append(',')
                    .Append("\"path\":").Append(Json.Quote(GetPath(camera.gameObject))).Append(',')
                    .Append("\"enabled\":").Append(Bool(cameraEnabled)).Append(',')
                    .Append("\"depth\":").Append(camera.depth.ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
                    .Append("\"targetTexture\":").Append(Bool(camera.targetTexture != null)).Append(',')
                    .Append("\"pixelWidth\":").Append(camera.pixelWidth).Append(',')
                    .Append("\"pixelHeight\":").Append(camera.pixelHeight).Append(',')
                    .Append("\"nearClip\":").Append(camera.nearClipPlane.ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
                    .Append("\"farClip\":").Append(camera.farClipPlane.ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
                    .Append("\"occlusion\":").Append(Bool(camera.useOcclusionCulling)).Append(',')
                    .Append("\"renderType\":").Append(Json.Quote(renderType)).Append(',')
                    .Append("\"postProcessing\":").Append(Bool(postProcessing)).Append(',')
                    .Append("\"stackCount\":").Append(stackCount)
                    .Append('}');
            }

            return Ok(
                $"\"total\":{cameras.Length}," +
                $"\"enabled\":{enabled}," +
                $"\"withPostProcessing\":{withPostProcessing}," +
                $"\"withStacks\":{withStacks}," +
                $"\"stackEntries\":{stackEntries}," +
                $"\"cameras\":[{items}]");
        }

        private string LightStats()
        {
            Light[] lights = UnityEngine.Object.FindObjectsOfType<Light>();
            int enabled = 0;
            int shadows = 0;
            StringBuilder items = new StringBuilder();

            Array.Sort(lights, (left, right) =>
            {
                float leftScore = left == null ? 0f : left.range * left.intensity;
                float rightScore = right == null ? 0f : right.range * right.intensity;
                return rightScore.CompareTo(leftScore);
            });

            for (int index = 0; index < lights.Length; index++)
            {
                Light light = lights[index];
                if (light == null)
                {
                    continue;
                }

                bool lightEnabled = light.enabled && light.gameObject.activeInHierarchy;
                if (lightEnabled)
                {
                    enabled++;
                }

                if (light.shadows != LightShadows.None)
                {
                    shadows++;
                }

                if (index >= 25)
                {
                    continue;
                }

                if (items.Length > 0)
                {
                    items.Append(',');
                }

                items.Append('{')
                    .Append("\"name\":").Append(Json.Quote(light.name ?? string.Empty)).Append(',')
                    .Append("\"path\":").Append(Json.Quote(GetPath(light.gameObject))).Append(',')
                    .Append("\"enabled\":").Append(Bool(lightEnabled)).Append(',')
                    .Append("\"type\":").Append(Json.Quote(light.type.ToString())).Append(',')
                    .Append("\"shadows\":").Append(Json.Quote(light.shadows.ToString())).Append(',')
                    .Append("\"range\":").Append(light.range.ToString("0.##", CultureInfo.InvariantCulture)).Append(',')
                    .Append("\"intensity\":").Append(light.intensity.ToString("0.##", CultureInfo.InvariantCulture))
                    .Append('}');
            }

            return Ok(
                $"\"total\":{lights.Length}," +
                $"\"enabled\":{enabled}," +
                $"\"withShadows\":{shadows}," +
                $"\"top\":[{items}]");
        }

        private string PipelineStats()
        {
            object? pipelineAsset = GraphicsSettings.currentRenderPipeline;
            if (pipelineAsset == null)
            {
                return Ok("\"pipeline\":\"none\",\"properties\":[]");
            }

            Type type = pipelineAsset.GetType();
            StringBuilder properties = new StringBuilder();
            int emitted = 0;

            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (property.GetIndexParameters().Length > 0 || !property.CanRead)
                {
                    continue;
                }

                string name = property.Name;
                if (!IsInterestingPipelineMember(name))
                {
                    continue;
                }

                string value;
                try
                {
                    object? raw = property.GetValue(pipelineAsset, null);
                    value = raw == null ? "null" : Convert.ToString(raw, CultureInfo.InvariantCulture) ?? string.Empty;
                }
                catch (Exception ex)
                {
                    value = "unavailable:" + ex.GetType().Name;
                }

                AppendPipelineMember(properties, name, property.PropertyType.Name, value, ref emitted);
            }

            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                string name = field.Name;
                if (!IsInterestingPipelineMember(name))
                {
                    continue;
                }

                string value;
                try
                {
                    object? raw = field.GetValue(pipelineAsset);
                    value = raw == null ? "null" : Convert.ToString(raw, CultureInfo.InvariantCulture) ?? string.Empty;
                }
                catch (Exception ex)
                {
                    value = "unavailable:" + ex.GetType().Name;
                }

                AppendPipelineMember(properties, name, field.FieldType.Name, value, ref emitted);
            }

            return Ok($"\"pipeline\":{Json.Quote(type.FullName ?? type.Name)},\"properties\":[{properties}]");
        }

        private string UrpStats()
        {
            if (!TryGetUniversalAsset(out UniversalRenderPipelineAsset? asset))
            {
                return Error("urp_unavailable", "Current render pipeline is not a UniversalRenderPipelineAsset.");
            }

            StringBuilder assetValues = new StringBuilder();
            AppendUrpValue(assetValues, "supportsCameraDepthTexture", ReadMember(asset, "supportsCameraDepthTexture"));
            AppendUrpValue(assetValues, "supportsCameraOpaqueTexture", ReadMember(asset, "supportsCameraOpaqueTexture"));
            AppendUrpValue(assetValues, "supportsHDR", ReadMember(asset, "supportsHDR"));
            AppendUrpValue(assetValues, "msaaSampleCount", ReadMember(asset, "msaaSampleCount"));
            AppendUrpValue(assetValues, "supportsSoftShadows", ReadMember(asset, "supportsSoftShadows"));
            AppendUrpValue(assetValues, "useSRPBatcher", ReadMember(asset, "useSRPBatcher"));
            AppendUrpValue(assetValues, "supportsDynamicBatching", ReadMember(asset, "supportsDynamicBatching"));
            AppendUrpValue(assetValues, "supportsLightCookies", ReadMember(asset, "supportsLightCookies"));
            AppendUrpValue(assetValues, "mainLightShadowsSupported", ReadMember(asset, "mainLightShadowsSupported"));
            AppendUrpValue(assetValues, "additionalLightShadowsSupported", ReadMember(asset, "additionalLightShadowsSupported"));
            AppendUrpValue(assetValues, "renderScale", ReadMember(asset, "renderScale"));
            AppendUrpValue(assetValues, "upscalingFilter", ReadMember(asset, "upscalingFilter"));
            AppendUrpValue(assetValues, "fsrOverrideSharpness", ReadMember(asset, "fsrOverrideSharpness"));
            AppendUrpValue(assetValues, "fsrSharpness", ReadMember(asset, "fsrSharpness"));

            UniversalRendererData[] rendererDataAssets = Resources.FindObjectsOfTypeAll<UniversalRendererData>();
            StringBuilder rendererValues = new StringBuilder();
            int emitted = 0;
            foreach (UniversalRendererData rendererData in rendererDataAssets)
            {
                if (rendererData == null)
                {
                    continue;
                }

                if (emitted > 0)
                {
                    rendererValues.Append(',');
                }

                rendererValues.Append('{')
                    .Append("\"name\":").Append(Json.Quote(rendererData.name ?? string.Empty)).Append(',')
                    .Append("\"depthPrimingMode\":").Append(Json.Quote(ValueText(ReadMember(rendererData, "depthPrimingMode")))).Append(',')
                    .Append("\"copyDepthMode\":").Append(Json.Quote(ValueText(ReadMember(rendererData, "copyDepthMode")))).Append(',')
                    .Append("\"intermediateTextureMode\":").Append(Json.Quote(ValueText(ReadMember(rendererData, "intermediateTextureMode")))).Append(',')
                    .Append("\"shadowTransparentReceive\":").Append(Json.Quote(ValueText(ReadMember(rendererData, "m_ShadowTransparentReceive")))).Append(',')
                    .Append("\"accurateGbufferNormals\":").Append(Json.Quote(ValueText(ReadMember(rendererData, "accurateGbufferNormals"))))
                    .Append('}');
                emitted++;
            }

            return Ok($"\"asset\":{{{assetValues}}},\"rendererData\":[{rendererValues}],\"tracked\":{_liveUrpSnapshots.Count}");
        }

        private string SetUrpAsset(string line)
        {
            if (!TryGetUniversalAsset(out UniversalRenderPipelineAsset? asset))
            {
                return Error("urp_unavailable", "Current render pipeline is not a UniversalRenderPipelineAsset.");
            }

            int changed = 0;
            changed += ApplyUrpBool(line, asset, "supportsCameraDepthTexture");
            changed += ApplyUrpBool(line, asset, "supportsCameraOpaqueTexture");
            changed += ApplyUrpBool(line, asset, "supportsHDR");
            changed += ApplyUrpInt(line, asset, "msaaSampleCount");
            changed += ApplyUrpBool(line, asset, "supportsSoftShadows");
            changed += ApplyUrpBool(line, asset, "useSRPBatcher");
            changed += ApplyUrpBool(line, asset, "supportsDynamicBatching");
            changed += ApplyUrpBool(line, asset, "supportsLightCookies");
            changed += ApplyUrpBool(line, asset, "mainLightShadowsSupported");
            changed += ApplyUrpBool(line, asset, "additionalLightShadowsSupported");
            changed += ApplyUrpFloat(line, asset, "renderScale");
            changed += ApplyUrpInt(line, asset, "upscalingFilter");
            changed += ApplyUrpString(line, asset, "upscalingFilter");
            changed += ApplyUrpBool(line, asset, "fsrOverrideSharpness");
            changed += ApplyUrpFloat(line, asset, "fsrSharpness");

            return Ok($"\"changed\":{changed},\"tracked\":{_liveUrpSnapshots.Count}");
        }

        private string SetUrpRenderer(string line)
        {
            UniversalRendererData[] rendererDataAssets = Resources.FindObjectsOfTypeAll<UniversalRendererData>();
            int changed = 0;
            int matched = 0;

            foreach (UniversalRendererData rendererData in rendererDataAssets)
            {
                if (rendererData == null)
                {
                    continue;
                }

                matched++;
                changed += ApplyUrpString(line, rendererData, "depthPrimingMode");
                changed += ApplyUrpString(line, rendererData, "copyDepthMode");
                changed += ApplyUrpString(line, rendererData, "intermediateTextureMode");
                changed += ApplyUrpBool(line, rendererData, "shadowTransparentReceive", "m_ShadowTransparentReceive");
                changed += ApplyUrpBool(line, rendererData, "accurateGbufferNormals");
            }

            return Ok($"\"matched\":{matched},\"changed\":{changed},\"tracked\":{_liveUrpSnapshots.Count}");
        }

        private string RestoreUrp()
        {
            int restored = 0;
            foreach (LiveUrpSnapshot snapshot in _liveUrpSnapshots.Values)
            {
                object? target = snapshot.Target;
                if (target == null)
                {
                    continue;
                }

                foreach (KeyValuePair<string, object?> pair in snapshot.Values)
                {
                    if (TrySetMember(target, pair.Key, pair.Value))
                    {
                        restored++;
                    }
                }
            }

            _liveUrpSnapshots.Clear();
            return Ok($"\"restored\":{restored}");
        }

        private string SetGlobalTextureMipmapLimit(string line)
        {
            if (!Json.TryGetInt(line, "globalTextureMipmapLimit", out int requestedLimit) &&
                !Json.TryGetInt(line, "limit", out requestedLimit))
            {
                return Error("bad_request", "Expected integer field 'globalTextureMipmapLimit' or 'limit'.");
            }

            int limit = Math.Max(0, Math.Min(3, requestedLimit));
            _liveGlobalTextureMipmapLimitSnapshot ??= QualitySettings.globalTextureMipmapLimit;
            QualitySettings.globalTextureMipmapLimit = limit;
            MelonLogger.Msg($"[{Constants.ModName}][Live] Set QualitySettings.globalTextureMipmapLimit={limit}.");
            return Ok($"\"globalTextureMipmapLimit\":{QualitySettings.globalTextureMipmapLimit},\"masterTextureLimit\":{QualitySettings.globalTextureMipmapLimit},\"previous\":{_liveGlobalTextureMipmapLimitSnapshot.Value}");
        }

        private string RestoreGlobalTextureMipmapLimit()
        {
            if (!_liveGlobalTextureMipmapLimitSnapshot.HasValue)
            {
                return Ok($"\"restored\":false,\"globalTextureMipmapLimit\":{QualitySettings.globalTextureMipmapLimit},\"masterTextureLimit\":{QualitySettings.globalTextureMipmapLimit}");
            }

            QualitySettings.globalTextureMipmapLimit = _liveGlobalTextureMipmapLimitSnapshot.Value;
            _liveGlobalTextureMipmapLimitSnapshot = null;
            MelonLogger.Msg($"[{Constants.ModName}][Live] Restored QualitySettings.globalTextureMipmapLimit={QualitySettings.globalTextureMipmapLimit}.");
            return Ok($"\"restored\":true,\"globalTextureMipmapLimit\":{QualitySettings.globalTextureMipmapLimit},\"masterTextureLimit\":{QualitySettings.globalTextureMipmapLimit}");
        }

        private string SetShaderMaximumLod(string line)
        {
            if (!Json.TryGetInt(line, "shaderMaximumLod", out int requestedLod) &&
                !Json.TryGetInt(line, "maximumLod", out requestedLod) &&
                !Json.TryGetInt(line, "lod", out requestedLod))
            {
                return Error("bad_request", "Expected integer field 'shaderMaximumLod', 'maximumLod', or 'lod'.");
            }

            int lod = Math.Max(0, Math.Min(1000, requestedLod));
            _liveShaderMaximumLodSnapshot ??= Shader.globalMaximumLOD;
            Shader.globalMaximumLOD = lod;
            MelonLogger.Msg($"[{Constants.ModName}][Live] Set Shader.globalMaximumLOD={lod}.");
            return Ok($"\"shaderMaximumLod\":{Shader.globalMaximumLOD},\"previous\":{_liveShaderMaximumLodSnapshot.Value}");
        }

        private string RestoreShaderMaximumLod()
        {
            if (!_liveShaderMaximumLodSnapshot.HasValue)
            {
                return Ok($"\"restored\":false,\"shaderMaximumLod\":{Shader.globalMaximumLOD}");
            }

            Shader.globalMaximumLOD = _liveShaderMaximumLodSnapshot.Value;
            _liveShaderMaximumLodSnapshot = null;
            MelonLogger.Msg($"[{Constants.ModName}][Live] Restored Shader.globalMaximumLOD={Shader.globalMaximumLOD}.");
            return Ok($"\"restored\":true,\"shaderMaximumLod\":{Shader.globalMaximumLOD}");
        }

        private string SetAnisotropicFiltering(string line)
        {
            string requestedMode = Json.GetString(line, "mode");
            if (string.IsNullOrWhiteSpace(requestedMode))
            {
                requestedMode = Json.GetString(line, "anisotropicFiltering");
            }

            if (string.IsNullOrWhiteSpace(requestedMode))
            {
                return Error("bad_request", "Expected field 'mode' or 'anisotropicFiltering'. Valid values: Disable, Enable, ForceEnable, 0, 1, 2.");
            }

            if (!TryParseAnisotropicFiltering(requestedMode, out AnisotropicFiltering mode))
            {
                return Error("bad_request", $"Invalid anisotropic filtering mode '{requestedMode}'.");
            }

            _liveAnisotropicFilteringSnapshot ??= QualitySettings.anisotropicFiltering;
            QualitySettings.anisotropicFiltering = mode;
            MelonLogger.Msg($"[{Constants.ModName}][Live] Set QualitySettings.anisotropicFiltering={mode}.");
            return Ok($"\"anisotropicFiltering\":{Json.Quote(QualitySettings.anisotropicFiltering.ToString())},\"previous\":{Json.Quote(_liveAnisotropicFilteringSnapshot.Value.ToString())}");
        }

        private string RestoreAnisotropicFiltering()
        {
            if (!_liveAnisotropicFilteringSnapshot.HasValue)
            {
                return Ok($"\"restored\":false,\"anisotropicFiltering\":{Json.Quote(QualitySettings.anisotropicFiltering.ToString())}");
            }

            QualitySettings.anisotropicFiltering = _liveAnisotropicFilteringSnapshot.Value;
            _liveAnisotropicFilteringSnapshot = null;
            MelonLogger.Msg($"[{Constants.ModName}][Live] Restored QualitySettings.anisotropicFiltering={QualitySettings.anisotropicFiltering}.");
            return Ok($"\"restored\":true,\"anisotropicFiltering\":{Json.Quote(QualitySettings.anisotropicFiltering.ToString())}");
        }

        private string SetTerrainFoliage(string line)
        {
            bool hasDrawTrees = Json.TryGetBool(line, "drawTreesAndFoliage", out bool drawTreesAndFoliage);
            bool hasDetailDistance = Json.TryGetFloat(line, "detailObjectDistance", out float detailObjectDistance);
            if (!hasDrawTrees && !hasDetailDistance)
            {
                return Error("bad_request", "Expected 'drawTreesAndFoliage' and/or 'detailObjectDistance'.");
            }

            int matched = 0;
            int changed = 0;
            foreach (Terrain terrain in Terrain.activeTerrains)
            {
                if (terrain == null)
                {
                    continue;
                }

                matched++;
                int id = terrain.GetInstanceID();
                if (!_liveTerrainSnapshots.ContainsKey(id))
                {
                    _liveTerrainSnapshots[id] = new LiveTerrainSnapshot
                    {
                        Terrain = terrain,
                        DrawTreesAndFoliage = terrain.drawTreesAndFoliage,
                        DetailObjectDistance = terrain.detailObjectDistance
                    };
                }

                if (hasDrawTrees && terrain.drawTreesAndFoliage != drawTreesAndFoliage)
                {
                    terrain.drawTreesAndFoliage = drawTreesAndFoliage;
                    changed++;
                }

                if (hasDetailDistance)
                {
                    float clampedDistance = Math.Max(0f, detailObjectDistance);
                    if (Math.Abs(terrain.detailObjectDistance - clampedDistance) > 0.001f)
                    {
                        terrain.detailObjectDistance = clampedDistance;
                        changed++;
                    }
                }
            }

            MelonLogger.Msg($"[{Constants.ModName}][Live] Set terrain foliage. matched={matched}, changed={changed}.");
            return Ok($"\"matched\":{matched},\"changed\":{changed},\"tracked\":{_liveTerrainSnapshots.Count}");
        }

        private string RestoreTerrainFoliage()
        {
            int restored = 0;
            foreach (LiveTerrainSnapshot snapshot in _liveTerrainSnapshots.Values)
            {
                Terrain? terrain = snapshot.Terrain;
                if (terrain == null)
                {
                    continue;
                }

                terrain.drawTreesAndFoliage = snapshot.DrawTreesAndFoliage;
                terrain.detailObjectDistance = snapshot.DetailObjectDistance;
                restored++;
            }

            _liveTerrainSnapshots.Clear();
            return Ok($"\"restored\":{restored}");
        }

        private static bool TryParseAnisotropicFiltering(string value, out AnisotropicFiltering mode)
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intValue) &&
                intValue >= 0 &&
                intValue <= 2)
            {
                mode = (AnisotropicFiltering)intValue;
                return true;
            }

            return Enum.TryParse(value, true, out mode);
        }

        private static bool IsInterestingPipelineMember(string name)
        {
            string lower = name.ToLowerInvariant();
            return lower.Contains("render") ||
                   lower.Contains("shadow") ||
                   lower.Contains("light") ||
                   lower.Contains("msaa") ||
                   lower.Contains("hdr") ||
                   lower.Contains("opaque") ||
                   lower.Contains("depth") ||
                   lower.Contains("terrain") ||
                   lower.Contains("srp") ||
                   lower.Contains("batch");
        }

        private static void AppendPipelineMember(StringBuilder builder, string name, string type, string value, ref int emitted)
        {
            if (emitted >= 80)
            {
                return;
            }

            if (builder.Length > 0)
            {
                builder.Append(',');
            }

            builder.Append('{')
                .Append("\"name\":").Append(Json.Quote(name)).Append(',')
                .Append("\"type\":").Append(Json.Quote(type)).Append(',')
                .Append("\"value\":").Append(Json.Quote(value))
                .Append('}');
            emitted++;
        }

        private static void AppendUrpValue(StringBuilder builder, string name, object? value)
        {
            if (builder.Length > 0)
            {
                builder.Append(',');
            }

            builder.Append(Json.Quote(name)).Append(':').Append(Json.Quote(ValueText(value)));
        }

        private static string ValueText(object? value)
        {
            return value == null ? "null" : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private bool TryGetUniversalAsset(out UniversalRenderPipelineAsset? asset)
        {
            asset = null;
            object? pipelineAsset = GraphicsSettings.currentRenderPipeline;
            if (pipelineAsset == null)
            {
                return false;
            }

            if (pipelineAsset is UniversalRenderPipelineAsset typedAsset)
            {
                asset = typedAsset;
                return true;
            }

#if IL2CPP
            if (pipelineAsset is Il2CppObjectBase il2CppObject)
            {
                try
                {
                    asset = il2CppObject.TryCast<UniversalRenderPipelineAsset>();
                    return asset != null;
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[{Constants.ModName}][Live] Could not IL2CPP-cast pipeline asset to URP: {ex.Message}");
                }
            }
#endif

            return false;
        }

        private int ApplyUrpBool(string line, object target, string jsonKey, string? memberName = null)
        {
            if (!Json.TryGetBool(line, jsonKey, out bool value))
            {
                return 0;
            }

            return ApplyUrpMember(target, memberName ?? jsonKey, value) ? 1 : 0;
        }

        private int ApplyUrpInt(string line, object target, string jsonKey, string? memberName = null)
        {
            if (!Json.TryGetInt(line, jsonKey, out int value))
            {
                return 0;
            }

            return ApplyUrpMember(target, memberName ?? jsonKey, value) ? 1 : 0;
        }

        private int ApplyUrpFloat(string line, object target, string jsonKey, string? memberName = null)
        {
            if (!Json.TryGetFloat(line, jsonKey, out float value))
            {
                return 0;
            }

            return ApplyUrpMember(target, memberName ?? jsonKey, value) ? 1 : 0;
        }

        private int ApplyUrpString(string line, object target, string jsonKey, string? memberName = null)
        {
            string value = Json.GetString(line, jsonKey);
            if (string.IsNullOrWhiteSpace(value))
            {
                return 0;
            }

            return ApplyUrpMember(target, memberName ?? jsonKey, value) ? 1 : 0;
        }

        private bool ApplyUrpMember(object target, string memberName, object? value)
        {
            CaptureUrpMember(target, memberName);
            return TrySetMember(target, memberName, value);
        }

        private void CaptureUrpMember(object target, string memberName)
        {
            int id = GetUnityObjectId(target);
            if (id == 0)
            {
                return;
            }

            if (!_liveUrpSnapshots.TryGetValue(id, out LiveUrpSnapshot snapshot))
            {
                snapshot = new LiveUrpSnapshot { Target = target };
                _liveUrpSnapshots[id] = snapshot;
            }

            if (!snapshot.Values.ContainsKey(memberName))
            {
                snapshot.Values[memberName] = ReadMember(target, memberName);
            }
        }

        private static object? ReadMember(object? target, string memberName)
        {
            if (target == null)
            {
                return null;
            }

            Type type = target.GetType();
            PropertyInfo? property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.CanRead && property.GetIndexParameters().Length == 0)
            {
                try
                {
                    return property.GetValue(target, null);
                }
                catch
                {
                    return null;
                }
            }

            FieldInfo? field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                try
                {
                    return field.GetValue(target);
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }

        private static bool TrySetMember(object? target, string memberName, object? value)
        {
            if (target == null)
            {
                return false;
            }

            Type type = target.GetType();
            PropertyInfo? property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.CanWrite && property.GetIndexParameters().Length == 0)
            {
                try
                {
                    property.SetValue(target, ConvertMemberValue(value, property.PropertyType), null);
                    return true;
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[{Constants.ModName}][Live] Could not set {type.Name}.{memberName}: {ex.Message}");
                    return false;
                }
            }

            FieldInfo? field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                try
                {
                    field.SetValue(target, ConvertMemberValue(value, field.FieldType));
                    return true;
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[{Constants.ModName}][Live] Could not set {type.Name}.{memberName}: {ex.Message}");
                    return false;
                }
            }

            return false;
        }

        private static object? ConvertMemberValue(object? value, Type targetType)
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
                string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                if (Enum.TryParse(targetType, text, true, out object parsed))
                {
                    return parsed;
                }

                return Enum.ToObject(targetType, Convert.ToInt32(value, CultureInfo.InvariantCulture));
            }

            return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
        }

        private string RendererFeatureStats()
        {
            UniversalRendererData[] rendererDataAssets = Resources.FindObjectsOfTypeAll<UniversalRendererData>();
            StringBuilder items = new StringBuilder();
            int featureCount = 0;
            int activeCount = 0;

            foreach (UniversalRendererData rendererData in rendererDataAssets)
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

                    featureCount++;
                    bool active;
                    try
                    {
                        active = feature.isActive;
                    }
                    catch
                    {
                        active = false;
                    }

                    if (active)
                    {
                        activeCount++;
                    }

                    if (items.Length > 0)
                    {
                        items.Append(',');
                    }

                    items.Append('{')
                        .Append("\"rendererData\":").Append(Json.Quote(rendererData.name ?? string.Empty)).Append(',')
                        .Append("\"name\":").Append(Json.Quote(feature.name ?? string.Empty)).Append(',')
                        .Append("\"type\":").Append(Json.Quote(feature.GetType().FullName ?? feature.GetType().Name)).Append(',')
                        .Append("\"active\":").Append(Bool(active))
                        .Append('}');
                }
            }

            return Ok($"\"rendererDataAssets\":{rendererDataAssets.Length},\"features\":{featureCount},\"active\":{activeCount},\"items\":[{items}]");
        }

        private string SetRendererFeatures(string line)
        {
            string nameContains = Json.GetString(line, "nameContains");
            bool enabled = Json.GetBool(line, "enabled", false);
            if (string.IsNullOrWhiteSpace(nameContains))
            {
                return Error("bad_request", "Expected nameContains.");
            }

            int matched = 0;
            int changed = 0;

            foreach (UniversalRendererData rendererData in Resources.FindObjectsOfTypeAll<UniversalRendererData>())
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

                    matched++;
                    int id = feature.GetInstanceID();
                    bool active = feature.isActive;
                    if (!_liveRendererFeatureSnapshots.ContainsKey(id))
                    {
                        _liveRendererFeatureSnapshots[id] = new LiveRendererFeatureSnapshot
                        {
                            Feature = feature,
                            Active = active
                        };
                    }

                    if (active == enabled)
                    {
                        continue;
                    }

                    try
                    {
                        feature.SetActive(enabled);
                        changed++;
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Warning($"[{Constants.ModName}][Live] Could not toggle renderer feature {name}: {ex.Message}");
                    }
                }
            }

            return Ok($"\"nameContains\":{Json.Quote(nameContains)},\"enabled\":{Bool(enabled)},\"matched\":{matched},\"changed\":{changed},\"tracked\":{_liveRendererFeatureSnapshots.Count}");
        }

        private string RestoreRendererFeatures()
        {
            int restored = 0;
            foreach (LiveRendererFeatureSnapshot snapshot in _liveRendererFeatureSnapshots.Values)
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
                        restored++;
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[{Constants.ModName}][Live] Could not restore renderer feature {feature.name}: {ex.Message}");
                }
            }

            _liveRendererFeatureSnapshots.Clear();
            return Ok($"\"restored\":{restored}");
        }

        private string SetCameraStacks(string line)
        {
            bool enabled = Json.GetBool(line, "enabled", false);
            int changed = 0;
            int stackEntries = 0;

            foreach (Camera camera in UnityEngine.Object.FindObjectsOfType<Camera>())
            {
                if (camera == null)
                {
                    continue;
                }

                UniversalAdditionalCameraData data;
                try
                {
                    data = camera.GetComponent<UniversalAdditionalCameraData>();
                }
                catch
                {
                    continue;
                }

                if (data == null || data.cameraStack == null)
                {
                    continue;
                }

                int id = camera.GetInstanceID();
                if (!_liveCameraStackSnapshots.ContainsKey(id))
                {
                    LiveCameraStackSnapshot snapshot = new LiveCameraStackSnapshot { Camera = camera };
                    for (int index = 0; index < data.cameraStack.Count; index++)
                    {
                        Camera stackedCamera = data.cameraStack[index];
                        if (stackedCamera != null)
                        {
                            snapshot.Stack.Add(stackedCamera);
                        }
                    }

                    _liveCameraStackSnapshots[id] = snapshot;
                }

                LiveCameraStackSnapshot original = _liveCameraStackSnapshots[id];
                stackEntries += original.Stack.Count;

                try
                {
                    data.cameraStack.Clear();
                    if (enabled)
                    {
                        foreach (Camera stackedCamera in original.Stack)
                        {
                            if (stackedCamera != null)
                            {
                                data.cameraStack.Add(stackedCamera);
                            }
                        }
                    }

                    changed++;
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[{Constants.ModName}][Live] Could not {(enabled ? "restore" : "clear")} camera stack for {camera.name}: {ex.Message}");
                }
            }

            if (enabled)
            {
                _liveCameraStackSnapshots.Clear();
            }

            return Ok($"\"enabled\":{Bool(enabled)},\"changed\":{changed},\"tracked\":{_liveCameraStackSnapshots.Count},\"stackEntries\":{stackEntries}");
        }

        private string RestoreCameraStacks()
        {
            int restored = 0;
            foreach (LiveCameraStackSnapshot snapshot in _liveCameraStackSnapshots.Values)
            {
                Camera? camera = snapshot.Camera;
                if (camera == null)
                {
                    continue;
                }

                try
                {
                    UniversalAdditionalCameraData data = camera.GetComponent<UniversalAdditionalCameraData>();
                    if (data == null || data.cameraStack == null)
                    {
                        continue;
                    }

                    data.cameraStack.Clear();
                    foreach (Camera stackedCamera in snapshot.Stack)
                    {
                        if (stackedCamera != null)
                        {
                            data.cameraStack.Add(stackedCamera);
                        }
                    }

                    restored++;
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[{Constants.ModName}][Live] Could not restore camera stack for {camera.name}: {ex.Message}");
                }
            }

            _liveCameraStackSnapshots.Clear();
            return Ok($"\"restored\":{restored}");
        }

        private string SetLights(string line)
        {
            bool enabled = Json.GetBool(line, "enabled", false);
            float minDistance = Math.Max(0f, Json.GetFloat(line, "minDistance", 50f));
            Camera camera = Camera.main;
            Vector3 origin = camera == null ? Vector3.zero : camera.transform.position;
            int changed = 0;
            int considered = 0;

            foreach (Light light in UnityEngine.Object.FindObjectsOfType<Light>())
            {
                if (light == null)
                {
                    continue;
                }

                float distance = camera == null ? 0f : Vector3.Distance(origin, light.transform.position);
                if (distance < minDistance)
                {
                    continue;
                }

                considered++;
                int id = light.GetInstanceID();
                if (!_liveLightSnapshots.ContainsKey(id))
                {
                    _liveLightSnapshots[id] = new LiveLightSnapshot
                    {
                        Light = light,
                        Enabled = light.enabled
                    };
                }

                try
                {
                    if (light.enabled != enabled)
                    {
                        light.enabled = enabled;
                        changed++;
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[{Constants.ModName}][Live] Could not toggle light {light.name}: {ex.Message}");
                }
            }

            return Ok($"\"enabled\":{Bool(enabled)},\"minDistance\":{minDistance.ToString("0.##", CultureInfo.InvariantCulture)},\"considered\":{considered},\"changed\":{changed},\"tracked\":{_liveLightSnapshots.Count}");
        }

        private string RestoreLiveLights()
        {
            int restored = 0;
            foreach (LiveLightSnapshot snapshot in _liveLightSnapshots.Values)
            {
                Light? light = snapshot.Light;
                if (light == null)
                {
                    continue;
                }

                try
                {
                    if (light.enabled != snapshot.Enabled)
                    {
                        light.enabled = snapshot.Enabled;
                        restored++;
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[{Constants.ModName}][Live] Could not restore light {light.name}: {ex.Message}");
                }
            }

            _liveLightSnapshots.Clear();
            return Ok($"\"restored\":{restored}");
        }

        private string MeshStats(string line)
        {
            if (!IsGameLoaded())
            {
                return Error("not_loaded", "Gameplay scene is not loaded. Run loadSave first and wait for isInGameScene=true.");
            }

            int limit = Math.Max(1, Math.Min(Json.GetInt(line, "limit", 20), 100));
            bool visibleOnly = Json.GetBool(line, "visibleOnly", false);
            Camera camera = Camera.main;
            Plane[]? planes = camera == null ? null : GeometryUtility.CalculateFrustumPlanes(camera);
            Vector3 cameraPosition = camera == null ? Vector3.zero : camera.transform.position;

            Dictionary<int, MeshStat> stats = new Dictionary<int, MeshStat>();
            int rendererCount = 0;
            int visibleRendererCount = 0;
            long totalTriangles = 0;
            long visibleTriangles = 0;

            foreach (MeshFilter filter in UnityEngine.Object.FindObjectsOfType<MeshFilter>())
            {
                if (filter == null)
                {
                    continue;
                }

                Renderer renderer = filter.GetComponent<Renderer>();
                Mesh mesh = filter.sharedMesh;
                AddMeshStat(mesh, renderer, filter.gameObject, false, visibleOnly, planes, cameraPosition, stats, ref rendererCount, ref visibleRendererCount, ref totalTriangles, ref visibleTriangles);
            }

            foreach (SkinnedMeshRenderer renderer in UnityEngine.Object.FindObjectsOfType<SkinnedMeshRenderer>())
            {
                if (renderer == null)
                {
                    continue;
                }

                AddMeshStat(renderer.sharedMesh, renderer, renderer.gameObject, true, visibleOnly, planes, cameraPosition, stats, ref rendererCount, ref visibleRendererCount, ref totalTriangles, ref visibleTriangles);
            }

            List<MeshStat> ordered = new List<MeshStat>(stats.Values);
            ordered.Sort((left, right) =>
            {
                int triangleCompare = right.VisibleMeshTriangles.CompareTo(left.VisibleMeshTriangles);
                return triangleCompare != 0 ? triangleCompare : right.MeshTriangles.CompareTo(left.MeshTriangles);
            });

            StringBuilder items = new StringBuilder();
            int count = Math.Min(limit, ordered.Count);
            for (int index = 0; index < count; index++)
            {
                MeshStat stat = ordered[index];
                if (index > 0)
                {
                    items.Append(',');
                }

                items.Append('{')
                    .Append("\"mesh\":").Append(Json.Quote(stat.MeshName)).Append(',')
                    .Append("\"path\":").Append(Json.Quote(stat.SamplePath)).Append(',')
                    .Append("\"layer\":").Append(stat.Layer).Append(',')
                    .Append("\"skinned\":").Append(Bool(stat.Skinned)).Append(',')
                    .Append("\"staticBatch\":").Append(Bool(stat.StaticBatch)).Append(',')
                    .Append("\"combinedMesh\":").Append(Bool(stat.CombinedMesh)).Append(',')
                    .Append("\"meshTriangles\":").Append(stat.MeshTriangles).Append(',')
                    .Append("\"visibleMeshTriangles\":").Append(stat.VisibleMeshTriangles).Append(',')
                    .Append("\"subMeshes\":").Append(stat.SubMeshCount).Append(',')
                    .Append("\"materials\":").Append(stat.MaterialCount).Append(',')
                    .Append("\"renderers\":").Append(stat.Renderers).Append(',')
                    .Append("\"visibleRenderers\":").Append(stat.VisibleRenderers).Append(',')
                    .Append("\"triangleInstances\":").Append(stat.TotalTriangleInstances).Append(',')
                    .Append("\"visibleTriangleInstances\":").Append(stat.VisibleTriangleInstances).Append(',')
                    .Append("\"nearestDistance\":").Append(stat.NearestDistance.ToString("0.##", CultureInfo.InvariantCulture))
                    .Append('}');
            }

            return Ok(
                $"\"camera\":{Json.Quote(camera == null ? string.Empty : camera.name)}," +
                $"\"visibleOnly\":{Bool(visibleOnly)}," +
                $"\"meshes\":{stats.Count}," +
                $"\"renderers\":{rendererCount}," +
                $"\"visibleRenderers\":{visibleRendererCount}," +
                $"\"triangleInstances\":{totalTriangles}," +
                $"\"visibleTriangleInstances\":{visibleTriangles}," +
                $"\"top\":[{items}]");
        }

        private string SetRenderers(string line)
        {
            if (!IsGameLoaded())
            {
                return Error("not_loaded", "Gameplay scene is not loaded. Run loadSave first and wait for isInGameScene=true.");
            }

            string contains = Json.GetString(line, "contains");
            string meshContains = Json.GetString(line, "meshContains");
            string pathContains = Json.GetString(line, "pathContains");
            int layer = Json.GetInt(line, "layer", -1);
            float minDistance = Json.GetFloat(line, "minDistance", 0f);
            float maxDistance = Json.GetFloat(line, "maxDistance", float.MaxValue);
            int limit = Math.Max(1, Math.Min(Json.GetInt(line, "limit", 500), 10000));
            bool enabled = Json.GetBool(line, "enabled", false);

            Camera camera = Camera.main;
            Vector3 cameraPosition = camera == null ? Vector3.zero : camera.transform.position;
            int matched = 0;
            int changed = 0;
            long triangles = 0;
            StringBuilder samples = new StringBuilder();

            foreach (Renderer renderer in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (renderer == null || matched >= limit)
                {
                    continue;
                }

                GameObject gameObject = renderer.gameObject;
                if (gameObject == null)
                {
                    continue;
                }

                Mesh mesh = GetRendererMesh(renderer);
                string meshName = mesh == null ? string.Empty : mesh.name ?? string.Empty;
                string path = GetPath(gameObject);
                if (!MatchesRendererFilter(path, meshName, gameObject.layer, contains, pathContains, meshContains, layer))
                {
                    continue;
                }

                float distance = camera == null ? 0f : Vector3.Distance(cameraPosition, renderer.bounds.center);
                if (distance < minDistance || distance > maxDistance)
                {
                    continue;
                }

                matched++;
                long meshTriangles = mesh == null ? 0 : CountTriangles(mesh);
                triangles += meshTriangles;
                if (samples.Length < 1200)
                {
                    if (samples.Length > 0)
                    {
                        samples.Append(',');
                    }

                    samples.Append('{')
                        .Append("\"path\":").Append(Json.Quote(path)).Append(',')
                        .Append("\"mesh\":").Append(Json.Quote(meshName)).Append(',')
                        .Append("\"layer\":").Append(gameObject.layer).Append(',')
                        .Append("\"distance\":").Append(distance.ToString("0.##", CultureInfo.InvariantCulture)).Append(',')
                        .Append("\"triangles\":").Append(meshTriangles)
                        .Append('}');
                }

                if (renderer.enabled == enabled)
                {
                    continue;
                }

                int id = renderer.GetInstanceID();
                if (!_liveRendererSnapshots.ContainsKey(id))
                {
                    _liveRendererSnapshots[id] = new LiveRendererSnapshot { Renderer = renderer, Enabled = renderer.enabled };
                }

                renderer.enabled = enabled;
                changed++;
            }

            return Ok(
                $"\"matched\":{matched}," +
                $"\"changed\":{changed}," +
                $"\"enabled\":{Bool(enabled)}," +
                $"\"tracked\":{_liveRendererSnapshots.Count}," +
                $"\"triangleInstances\":{triangles}," +
                $"\"samples\":[{samples}]");
        }

        private static string OptimizationImpacts()
        {
            StringBuilder items = new StringBuilder();
            int emitted = 0;

            foreach (OptimizationImpact impact in OptimizationImpactCatalog.Items)
            {
                if (emitted > 0)
                {
                    items.Append(',');
                }

                items.Append('{')
                    .Append("\"id\":").Append(Json.Quote(impact.Id)).Append(',')
                    .Append("\"label\":").Append(Json.Quote(impact.Label)).Append(',')
                    .Append("\"category\":").Append(Json.Quote(impact.Category)).Append(',')
                    .Append("\"measuredFpsDelta\":").Append(NullableFloat(impact.MeasuredFpsDelta)).Append(',')
                    .Append("\"measuredTotalFps\":").Append(NullableFloat(impact.MeasuredTotalFps)).Append(',')
                    .Append("\"baselineFps\":").Append(Value(OptimizationImpactCatalog.TownhallBaselineFps)).Append(',')
                    .Append("\"measurementContext\":").Append(Json.Quote(impact.MeasurementContext)).Append(',')
                    .Append("\"confidence\":").Append(Json.Quote(impact.Confidence.ToString())).Append(',')
                    .Append("\"visualRisk\":").Append(Json.Quote(impact.VisualRisk.ToString())).Append(',')
                    .Append("\"recommended\":").Append(Bool(impact.Recommended)).Append(',')
                    .Append("\"qualityNotes\":").Append(Json.Quote(impact.QualityNotes)).Append(',')
                    .Append("\"measurementNotes\":").Append(Json.Quote(impact.MeasurementNotes))
                    .Append('}');
                emitted++;
            }

            return Ok($"\"baselineFps\":{Value(OptimizationImpactCatalog.TownhallBaselineFps)},\"items\":[{items}]");
        }

        private string RestoreLiveRenderers()
        {
            int restored = 0;
            foreach (LiveRendererSnapshot snapshot in _liveRendererSnapshots.Values)
            {
                Renderer? renderer = snapshot.Renderer;
                if (renderer == null)
                {
                    continue;
                }

                renderer.enabled = snapshot.Enabled;
                restored++;
            }

            _liveRendererSnapshots.Clear();
            return Ok($"\"restored\":{restored}");
        }

        private string MaterialInstancingStats(string line)
        {
            if (!IsGameLoaded())
            {
                return Error("not_loaded", "Gameplay scene is not loaded. Run loadSave first and wait for isInGameScene=true.");
            }

            bool visibleOnly = Json.GetBool(line, "visibleOnly", false);
            int limit = Math.Max(1, Math.Min(Json.GetInt(line, "limit", 20), 100));
            MaterialInstancingCounts counts = CountMaterialInstancing(visibleOnly, limit, out string samples);
            return Ok(MaterialInstancingCountsJson(counts, visibleOnly, samples));
        }

        private string SetMaterialInstancing(string line)
        {
            if (!IsGameLoaded())
            {
                return Error("not_loaded", "Gameplay scene is not loaded. Run loadSave first and wait for isInGameScene=true.");
            }

            bool enabled = Json.GetBool(line, "enabled", true);
            bool visibleOnly = Json.GetBool(line, "visibleOnly", false);
            int changed = 0;
            int failed = 0;
            HashSet<int> seen = new HashSet<int>();

            foreach (Renderer renderer in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (renderer == null || !RendererPassesVisibilityFilter(renderer, visibleOnly))
                {
                    continue;
                }

                Material[]? materials = renderer.sharedMaterials;
                if (materials == null)
                {
                    continue;
                }

                foreach (Material material in materials)
                {
                    if (material == null)
                    {
                        continue;
                    }

                    int id = material.GetInstanceID();
                    if (!seen.Add(id))
                    {
                        continue;
                    }

                    if (!_liveMaterialSnapshots.ContainsKey(id))
                    {
                        _liveMaterialSnapshots[id] = new LiveMaterialSnapshot
                        {
                            Material = material,
                            EnableInstancing = material.enableInstancing
                        };
                    }

                    try
                    {
                        if (material.enableInstancing != enabled)
                        {
                            material.enableInstancing = enabled;
                            changed++;
                        }
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        MelonLogger.Warning($"[{Constants.ModName}][Live] Could not set instancing on material {material.name}: {ex.Message}");
                    }
                }
            }

            MaterialInstancingCounts counts = CountMaterialInstancing(visibleOnly, 20, out string samples);
            return Ok(
                $"\"enabled\":{Bool(enabled)}," +
                $"\"visibleOnly\":{Bool(visibleOnly)}," +
                $"\"changed\":{changed}," +
                $"\"failed\":{failed}," +
                $"\"tracked\":{_liveMaterialSnapshots.Count}," +
                MaterialInstancingCountsJson(counts, visibleOnly, samples).TrimStart('{').TrimEnd('}'));
        }

        private string RestoreMaterialInstancing()
        {
            int restored = 0;
            foreach (LiveMaterialSnapshot snapshot in _liveMaterialSnapshots.Values)
            {
                Material? material = snapshot.Material;
                if (material == null)
                {
                    continue;
                }

                try
                {
                    if (material.enableInstancing != snapshot.EnableInstancing)
                    {
                        material.enableInstancing = snapshot.EnableInstancing;
                        restored++;
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[{Constants.ModName}][Live] Could not restore instancing on material {material.name}: {ex.Message}");
                }
            }

            _liveMaterialSnapshots.Clear();
            return Ok($"\"restored\":{restored}");
        }

        private static MaterialInstancingCounts CountMaterialInstancing(bool visibleOnly, int limit, out string samples)
        {
            MaterialInstancingCounts counts = new MaterialInstancingCounts();
            Dictionary<int, MaterialInstancingSample> sampleMap = new Dictionary<int, MaterialInstancingSample>();
            HashSet<int> uniqueMaterials = new HashSet<int>();
            HashSet<string> uniqueShaders = new HashSet<string>(StringComparer.Ordinal);

            foreach (Renderer renderer in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (renderer == null || !RendererPassesVisibilityFilter(renderer, visibleOnly))
                {
                    continue;
                }

                counts.Renderers++;
                if (renderer.isPartOfStaticBatch)
                {
                    counts.StaticBatchRenderers++;
                }

                Material[]? materials = renderer.sharedMaterials;
                if (materials == null)
                {
                    continue;
                }

                foreach (Material material in materials)
                {
                    if (material == null)
                    {
                        continue;
                    }

                    counts.MaterialReferences++;
                    int materialId = material.GetInstanceID();
                    bool firstMaterialReference = uniqueMaterials.Add(materialId);
                    if (firstMaterialReference)
                    {
                        counts.UniqueMaterials++;
                        if (material.enableInstancing)
                        {
                            counts.UniqueInstancedMaterials++;
                        }

                        string shaderName = material.shader == null ? string.Empty : material.shader.name ?? string.Empty;
                        if (uniqueShaders.Add(shaderName))
                        {
                            counts.UniqueShaders++;
                        }
                    }

                    if (material.enableInstancing)
                    {
                        counts.InstancedMaterialReferences++;
                    }

                    if (!sampleMap.TryGetValue(materialId, out MaterialInstancingSample sample))
                    {
                        sample = new MaterialInstancingSample
                        {
                            MaterialName = material.name ?? string.Empty,
                            ShaderName = material.shader == null ? string.Empty : material.shader.name ?? string.Empty,
                            EnableInstancing = material.enableInstancing
                        };
                        sampleMap[materialId] = sample;
                    }

                    sample.Renderers++;
                }
            }

            List<MaterialInstancingSample> ordered = new List<MaterialInstancingSample>(sampleMap.Values);
            ordered.Sort((left, right) => right.Renderers.CompareTo(left.Renderers));
            StringBuilder builder = new StringBuilder();
            int count = Math.Min(limit, ordered.Count);
            for (int index = 0; index < count; index++)
            {
                MaterialInstancingSample sample = ordered[index];
                if (index > 0)
                {
                    builder.Append(',');
                }

                builder.Append('{')
                    .Append("\"material\":").Append(Json.Quote(sample.MaterialName)).Append(',')
                    .Append("\"shader\":").Append(Json.Quote(sample.ShaderName)).Append(',')
                    .Append("\"instancing\":").Append(Bool(sample.EnableInstancing)).Append(',')
                    .Append("\"renderers\":").Append(sample.Renderers)
                    .Append('}');
            }

            samples = builder.ToString();
            return counts;
        }

        private static string MaterialInstancingCountsJson(MaterialInstancingCounts counts, bool visibleOnly, string samples)
        {
            return
                $"\"visibleOnly\":{Bool(visibleOnly)}," +
                $"\"renderers\":{counts.Renderers}," +
                $"\"staticBatchRenderers\":{counts.StaticBatchRenderers}," +
                $"\"materialReferences\":{counts.MaterialReferences}," +
                $"\"instancedMaterialReferences\":{counts.InstancedMaterialReferences}," +
                $"\"uniqueMaterials\":{counts.UniqueMaterials}," +
                $"\"uniqueInstancedMaterials\":{counts.UniqueInstancedMaterials}," +
                $"\"uniqueShaders\":{counts.UniqueShaders}," +
                $"\"samples\":[{samples}]";
        }

        private static bool RendererPassesVisibilityFilter(Renderer renderer, bool visibleOnly)
        {
            return !visibleOnly || renderer.isVisible;
        }

        private static void AddMeshStat(
            Mesh mesh,
            Renderer renderer,
            GameObject gameObject,
            bool skinned,
            bool visibleOnly,
            Plane[]? planes,
            Vector3 cameraPosition,
            Dictionary<int, MeshStat> stats,
            ref int rendererCount,
            ref int visibleRendererCount,
            ref long totalTriangles,
            ref long visibleTriangles)
        {
            if (mesh == null || renderer == null || gameObject == null || !renderer.enabled)
            {
                return;
            }

            bool visible = planes == null || GeometryUtility.TestPlanesAABB(planes, renderer.bounds);
            if (visibleOnly && !visible)
            {
                return;
            }

            long meshTriangles = CountTriangles(mesh);
            if (meshTriangles <= 0)
            {
                return;
            }

            rendererCount++;
            totalTriangles += meshTriangles;
            if (visible)
            {
                visibleRendererCount++;
                visibleTriangles += meshTriangles;
            }

            int meshId = mesh.GetInstanceID();
            if (!stats.TryGetValue(meshId, out MeshStat stat))
            {
                stat = new MeshStat
                {
                    MeshName = mesh.name ?? string.Empty,
                    MeshTriangles = meshTriangles,
                    VisibleMeshTriangles = visible ? meshTriangles : 0,
                    SubMeshCount = mesh.subMeshCount,
                    SamplePath = GetPath(gameObject),
                    Layer = gameObject.layer,
                    Skinned = skinned,
                    StaticBatch = renderer.isPartOfStaticBatch,
                    CombinedMesh = (mesh.name ?? string.Empty).StartsWith("Combined Mesh", StringComparison.OrdinalIgnoreCase),
                    NearestDistance = float.MaxValue
                };
                stats[meshId] = stat;
            }

            int materialCount = 0;
            try
            {
                materialCount = renderer.sharedMaterials?.Length ?? 0;
            }
            catch
            {
                materialCount = 0;
            }

            float distance = planes == null ? 0f : Vector3.Distance(cameraPosition, renderer.bounds.center);
            stat.Renderers++;
            stat.TotalTriangleInstances += meshTriangles;
            stat.MaterialCount = Math.Max(stat.MaterialCount, materialCount);
            stat.Skinned = stat.Skinned || skinned;
            stat.StaticBatch = stat.StaticBatch || renderer.isPartOfStaticBatch;
            stat.NearestDistance = Math.Min(stat.NearestDistance, distance);
            if (visible)
            {
                stat.VisibleRenderers++;
                stat.VisibleTriangleInstances += meshTriangles;
                stat.VisibleMeshTriangles = stat.MeshTriangles;
            }
        }

        private static long CountTriangles(Mesh mesh)
        {
            long indices = 0;
            int subMeshCount = mesh.subMeshCount;
            for (int subMesh = 0; subMesh < subMeshCount; subMesh++)
            {
                try
                {
                    indices += mesh.GetIndexCount(subMesh);
                }
                catch
                {
                    return 0;
                }
            }

            return indices / 3;
        }

        private static Mesh GetRendererMesh(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned)
            {
                return skinned.sharedMesh;
            }

            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            return filter == null ? null : filter.sharedMesh;
        }

        private static bool MatchesRendererFilter(
            string path,
            string meshName,
            int actualLayer,
            string contains,
            string pathContains,
            string meshContains,
            int layer)
        {
            if (layer >= 0 && actualLayer != layer)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(contains) &&
                path.IndexOf(contains, StringComparison.OrdinalIgnoreCase) < 0 &&
                meshName.IndexOf(contains, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(pathContains) &&
                path.IndexOf(pathContains, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(meshContains) &&
                meshName.IndexOf(meshContains, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(contains) ||
                   !string.IsNullOrWhiteSpace(pathContains) ||
                   !string.IsNullOrWhiteSpace(meshContains) ||
                   layer >= 0;
        }

        private static string GetPath(GameObject gameObject)
        {
            StringBuilder path = new StringBuilder(gameObject.name ?? string.Empty);
            Transform parent = gameObject.transform.parent;
            int depth = 0;
            while (parent != null && depth < 8)
            {
                path.Insert(0, (parent.name ?? string.Empty) + "/");
                parent = parent.parent;
                depth++;
            }

            return path.ToString();
        }

        private string StateResponse()
        {
            TryReadNativeFpsLabel(out float fps, out string label);
            return Ok(
                $"\"profile\":{Json.Quote(_config.GetActiveProfile())}," +
                $"\"optimizer\":{Bool(_config.IsOptimizerEnabled())}," +
                $"\"loadManager\":{Bool(GameLoadManager.InstanceExists)}," +
                $"\"isGameLoaded\":{Bool(GameLoadManager.InstanceExists && GameLoadManager.Instance.IsGameLoaded)}," +
                $"\"isInGameScene\":{Bool(IsGameLoaded())}," +
                $"\"currentTime\":{GetCurrentTime()}," +
                $"\"fpsLabel\":{Json.Quote(label)}," +
                $"\"fps\":{fps.ToString("0.##", CultureInfo.InvariantCulture)}," +
                $"\"renderScale\":{Value(_config.RenderScale?.Value ?? 1f)}," +
                $"\"useRenderScale\":{Bool(_config.UseRenderScale?.Value ?? false)}," +
                $"\"useShadowSettings\":{Bool(_config.UseShadowSettings?.Value ?? false)}," +
                $"\"shadowDistance\":{Value(_config.ShadowDistance?.Value ?? 0f)}," +
                $"\"shadowCascades\":{_config.ShadowCascades?.Value ?? 0}," +
                $"\"disablePostProcessing\":{Bool(_config.DisablePostProcessing?.Value ?? false)}," +
                $"\"useFrameRateSettings\":{Bool(_config.UseFrameRateSettings?.Value ?? false)}," +
                $"\"vSyncCount\":{_config.VSyncCount?.Value ?? 0}," +
                $"\"targetFrameRate\":{_config.TargetFrameRate?.Value ?? -1}," +
                $"\"usePixelLightCount\":{Bool(_config.UsePixelLightCount?.Value ?? false)}," +
                $"\"pixelLightCount\":{_config.PixelLightCount?.Value ?? 0}," +
                $"\"useAntiAliasing\":{Bool(_config.UseAntiAliasing?.Value ?? false)}," +
                $"\"antiAliasing\":{_config.AntiAliasing?.Value ?? 0}," +
                $"\"useGlobalTextureMipmapLimit\":{Bool(_config.UseGlobalTextureMipmapLimit?.Value ?? false)}," +
                $"\"globalTextureMipmapLimit\":{_config.GlobalTextureMipmapLimit?.Value ?? 0}," +
                $"\"useLodSettings\":{Bool(_config.UseLodSettings?.Value ?? false)}," +
                $"\"lodBias\":{Value(_config.LodBias?.Value ?? 1f)}," +
                $"\"maximumLodLevel\":{_config.MaximumLodLevel?.Value ?? 0}," +
                $"\"protectNearLods\":{Bool(_config.ProtectNearLods?.Value ?? false)}," +
                $"\"nearLodProtectionDistance\":{Value(_config.NearLodProtectionDistance?.Value ?? 25f)}," +
                $"\"useCameraFarClip\":{Bool(_config.UseCameraFarClip?.Value ?? false)}," +
                $"\"disableCameraStacks\":{Bool(_config.DisableCameraStacks?.Value ?? false)}," +
                $"\"useLayerCullDistances\":{Bool(_config.UseLayerCullDistances?.Value ?? false)}," +
                $"\"disableFarLightShadows\":{Bool(_config.DisableFarLightShadows?.Value ?? false)}," +
                $"\"farLightShadowDistance\":{Value(_config.FarLightShadowDistance?.Value ?? 35f)}," +
                $"\"disableCameraOcclusionCulling\":{Bool(_config.DisableCameraOcclusionCulling?.Value ?? false)}," +
                $"\"disableVolumetricLightBeams\":{Bool(_config.DisableVolumetricLightBeams?.Value ?? false)}," +
                $"\"useVisibilitySafeRendererCulling\":{Bool(_config.UseVisibilitySafeRendererCulling?.Value ?? false)}," +
                $"\"rendererCullMinDistance\":{Value(_config.RendererCullMinDistance?.Value ?? 35f)}," +
                $"\"disableOutlineFeature\":{Bool(_config.DisableOutlineFeature?.Value ?? false)}," +
                $"\"enableInteractionHoverThrottle\":{Bool(_config.EnableInteractionHoverThrottle?.Value ?? false)}," +
                $"\"interactionHoverThrottleHz\":{Value(_config.InteractionHoverThrottleHz?.Value ?? 30f)}," +
                $"\"enableWeatherEntityThrottle\":{Bool(_config.EnableWeatherEntityThrottle?.Value ?? false)}," +
                $"\"weatherEntityThrottleHz\":{Value(_config.WeatherEntityThrottleHz?.Value ?? 10f)}," +
                $"\"runtimeMasterTextureLimit\":{QualitySettings.globalTextureMipmapLimit}," +
                $"\"runtimeShaderMaximumLod\":{Shader.globalMaximumLOD}," +
                $"\"runtimeAnisotropicFiltering\":{Json.Quote(QualitySettings.anisotropicFiltering.ToString())}," +
                $"\"liveTerrainFoliageSnapshots\":{_liveTerrainSnapshots.Count}," +
                _optimizer.GetRuntimeStateJson());
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

        private static bool IsGameLoaded()
        {
            return GameLoadManager.InstanceExists && GameLoadManager.Instance.IsGameLoaded && GameLoadManager.Instance.IsInGameScene;
        }

        private static int GetCurrentTime()
        {
            return GameTimeManager.InstanceExists ? GameTimeManager.Instance.CurrentTime : -1;
        }

        private static bool IsValid24HourTime(int time)
        {
            int hour = time / 100;
            int minute = time - hour * 100;
            return hour >= 0 && hour <= 23 && minute >= 0 && minute <= 59;
        }

        private static void SetBool(MelonPreferences_Entry<bool>? entry, string line, string key)
        {
            if (entry != null && Json.TryGetBool(line, key, out bool value))
            {
                entry.Value = value;
            }
        }

        private static void SetFloat(MelonPreferences_Entry<float>? entry, string line, string key)
        {
            if (entry != null && Json.TryGetFloat(line, key, out float value))
            {
                entry.Value = value;
            }
        }

        private static void SetInt(MelonPreferences_Entry<int>? entry, string line, string key)
        {
            if (entry != null && Json.TryGetInt(line, key, out int value))
            {
                entry.Value = value;
            }
        }

        private static void SetString(MelonPreferences_Entry<string>? entry, string line, string key)
        {
            string value = Json.GetString(line, key);
            if (entry != null && !string.IsNullOrWhiteSpace(value))
            {
                entry.Value = value;
            }
        }

        private static string Ok(string members)
        {
            return "{\"ok\":true," + members + "}";
        }

        private static string Error(string code, string message)
        {
            return "{\"ok\":false,\"error\":" + Json.Quote(code) + ",\"message\":" + Json.Quote(message) + "}";
        }

        private static string Bool(bool value)
        {
            return value ? "true" : "false";
        }

        private static string Value(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string NullableFloat(float? value)
        {
            return value.HasValue ? Value(value.Value) : "null";
        }

        private static string SanitizeFileName(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalid, '_');
            }

            return value;
        }

        private static int GetUnityObjectId(object target)
        {
            return target is UnityEngine.Object unityObject ? unityObject.GetInstanceID() : 0;
        }

        private sealed class LiveRequest
        {
            private readonly ManualResetEventSlim _done = new ManualResetEventSlim(false);

            public LiveRequest(string line)
            {
                Line = line;
            }

            public string Line { get; }
            public string Response { get; private set; } = string.Empty;

            public void Complete(string response)
            {
                Response = response;
                _done.Set();
            }

            public bool Wait()
            {
                return _done.Wait(TimeSpan.FromSeconds(RequestTimeoutSeconds));
            }
        }

        private sealed class FpsSample
        {
            public FpsSample(LiveRequest request, float endAt, float requestedSeconds)
            {
                Request = request;
                EndAt = endAt;
                RequestedSeconds = requestedSeconds;
            }

            public LiveRequest Request { get; }
            public float EndAt { get; }
            public float RequestedSeconds { get; }
            public float NextSampleAt { get; set; }
            public List<float> Values { get; } = new List<float>();
        }

        private sealed class MeshStat
        {
            public string MeshName { get; set; } = string.Empty;
            public string SamplePath { get; set; } = string.Empty;
            public int Layer { get; set; }
            public bool Skinned { get; set; }
            public bool StaticBatch { get; set; }
            public bool CombinedMesh { get; set; }
            public long MeshTriangles { get; set; }
            public long VisibleMeshTriangles { get; set; }
            public int SubMeshCount { get; set; }
            public int MaterialCount { get; set; }
            public int Renderers { get; set; }
            public int VisibleRenderers { get; set; }
            public long TotalTriangleInstances { get; set; }
            public long VisibleTriangleInstances { get; set; }
            public float NearestDistance { get; set; }
        }

        private sealed class LiveRendererSnapshot
        {
            public Renderer? Renderer { get; set; }
            public bool Enabled { get; set; }
        }

        private sealed class LiveMaterialSnapshot
        {
            public Material? Material { get; set; }
            public bool EnableInstancing { get; set; }
        }

        private sealed class LiveTerrainSnapshot
        {
            public Terrain? Terrain { get; set; }
            public bool DrawTreesAndFoliage { get; set; }
            public float DetailObjectDistance { get; set; }
        }

        private sealed class MaterialInstancingCounts
        {
            public int Renderers { get; set; }
            public int StaticBatchRenderers { get; set; }
            public int MaterialReferences { get; set; }
            public int InstancedMaterialReferences { get; set; }
            public int UniqueMaterials { get; set; }
            public int UniqueInstancedMaterials { get; set; }
            public int UniqueShaders { get; set; }
        }

        private sealed class MaterialInstancingSample
        {
            public string MaterialName { get; set; } = string.Empty;
            public string ShaderName { get; set; } = string.Empty;
            public bool EnableInstancing { get; set; }
            public int Renderers { get; set; }
        }

        private sealed class LiveCameraStackSnapshot
        {
            public Camera? Camera { get; set; }
            public List<Camera> Stack { get; } = new List<Camera>();
        }

        private sealed class LiveLightSnapshot
        {
            public Light? Light { get; set; }
            public bool Enabled { get; set; }
        }

        private sealed class LiveRendererFeatureSnapshot
        {
            public ScriptableRendererFeature? Feature { get; set; }
            public bool Active { get; set; }
        }

        private sealed class LiveUrpSnapshot
        {
            public object? Target { get; set; }
            public Dictionary<string, object?> Values { get; } = new Dictionary<string, object?>();
        }

        private static class Json
        {
            public static string GetString(string json, string key)
            {
                json = Normalize(json);
                string marker = "\"" + key + "\"";
                int keyIndex = json.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                int markerLength = marker.Length;
                if (keyIndex < 0)
                {
                    marker = key;
                    keyIndex = json.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                    markerLength = marker.Length;
                    if (keyIndex < 0)
                    {
                        return string.Empty;
                    }
                }

                int colonIndex = json.IndexOf(':', keyIndex + markerLength);
                if (colonIndex < 0)
                {
                    return string.Empty;
                }

                int valueStart = colonIndex + 1;
                while (valueStart < json.Length && char.IsWhiteSpace(json[valueStart]))
                {
                    valueStart++;
                }

                if (valueStart >= json.Length)
                {
                    return string.Empty;
                }

                if (json[valueStart] != '"')
                {
                    int valueEnd = valueStart;
                    while (valueEnd < json.Length && json[valueEnd] != ',' && json[valueEnd] != '}')
                    {
                        valueEnd++;
                    }

                    return json.Substring(valueStart, valueEnd - valueStart).Trim();
                }

                StringBuilder value = new StringBuilder();
                bool escaped = false;
                for (int index = valueStart + 1; index < json.Length; index++)
                {
                    char current = json[index];
                    if (escaped)
                    {
                        value.Append(current);
                        escaped = false;
                        continue;
                    }

                    if (current == '\\')
                    {
                        escaped = true;
                        continue;
                    }

                    if (current == '"')
                    {
                        return value.ToString();
                    }

                    value.Append(current);
                }

                return string.Empty;
            }

            public static float GetFloat(string json, string key, float fallback)
            {
                return TryGetFloat(json, key, out float value) ? value : fallback;
            }

            public static int GetInt(string json, string key, int fallback)
            {
                return TryGetInt(json, key, out int value) ? value : fallback;
            }

            public static bool GetBool(string json, string key, bool fallback)
            {
                return TryGetBool(json, key, out bool value) ? value : fallback;
            }

            public static bool TryGetFloat(string json, string key, out float value)
            {
                json = Normalize(json);
                return float.TryParse(GetRawValue(json, key), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            }

            public static bool TryGetInt(string json, string key, out int value)
            {
                json = Normalize(json);
                return int.TryParse(GetRawValue(json, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
            }

            public static bool TryGetBool(string json, string key, out bool value)
            {
                json = Normalize(json);
                if (bool.TryParse(GetRawValue(json, key), out value))
                {
                    return true;
                }

                value = false;
                return false;
            }

            public static string Quote(string value)
            {
                return "\"" + (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            }

            private static string Normalize(string json)
            {
                return string.IsNullOrWhiteSpace(json) ? string.Empty : json.Trim().Replace("\\\"", "\"");
            }

            private static string GetRawValue(string json, string key)
            {
                int keyIndex = FindKey(json, key, out int markerLength);
                if (keyIndex < 0)
                {
                    return string.Empty;
                }

                int colonIndex = json.IndexOf(':', keyIndex + markerLength);
                if (colonIndex < 0)
                {
                    return string.Empty;
                }

                int valueStart = colonIndex + 1;
                while (valueStart < json.Length && char.IsWhiteSpace(json[valueStart]))
                {
                    valueStart++;
                }

                int valueEnd = valueStart;
                while (valueEnd < json.Length && json[valueEnd] != ',' && json[valueEnd] != '}')
                {
                    valueEnd++;
                }

                return json.Substring(valueStart, valueEnd - valueStart).Trim().Trim('"');
            }

            private static int FindKey(string json, string key, out int markerLength)
            {
                string marker = "\"" + key + "\"";
                int keyIndex = json.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                markerLength = marker.Length;
                if (keyIndex >= 0)
                {
                    return keyIndex;
                }

                marker = key;
                markerLength = marker.Length;
                int searchIndex = 0;
                while (searchIndex < json.Length)
                {
                    keyIndex = json.IndexOf(marker, searchIndex, StringComparison.OrdinalIgnoreCase);
                    if (keyIndex < 0)
                    {
                        return -1;
                    }

                    bool validPrefix = keyIndex == 0 || json[keyIndex - 1] == '{' || json[keyIndex - 1] == ',' || char.IsWhiteSpace(json[keyIndex - 1]);
                    int afterKey = keyIndex + marker.Length;
                    while (afterKey < json.Length && char.IsWhiteSpace(json[afterKey]))
                    {
                        afterKey++;
                    }

                    if (validPrefix && afterKey < json.Length && json[afterKey] == ':')
                    {
                        return keyIndex;
                    }

                    searchIndex = keyIndex + marker.Length;
                }

                return -1;
            }
        }
    }
}


