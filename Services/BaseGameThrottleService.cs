using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using BarsGraphics.Config;
using BarsGraphics.Utils;
using UnityEngine;

namespace BarsGraphics.Services
{
    internal sealed class BaseGameThrottleService : IDisposable
    {
        private const string HarmonyId = "bars.BarsGraphics.basegamethrottles";

        private static readonly Dictionary<int, float> NextInteractionHoverAt = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> NextWeatherEntityUpdateAt = new Dictionary<int, float>();

        private static ModConfig? _config;

        private readonly HarmonyLib.Harmony _harmony = new HarmonyLib.Harmony(HarmonyId);
        private readonly ModConfig _instanceConfig;
        private bool _initialized;

        public BaseGameThrottleService(ModConfig config)
        {
            _instanceConfig = config;
        }

        public void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            _config = _instanceConfig;
            PatchPrefix(
                new[]
                {
                    "ScheduleOne.Interaction.InteractionManager",
                    "Il2CppScheduleOne.Interaction.InteractionManager"
                },
                "CheckHover",
                nameof(InteractionHoverPrefix));
            PatchPrefix(
                new[]
                {
                    "ScheduleOne.Weather.EnvironmentManager",
                    "Il2CppScheduleOne.Weather.EnvironmentManager"
                },
                "UpdateWeatherEntities",
                nameof(WeatherEntityUpdatePrefix));

            _initialized = true;
        }

        public void Dispose()
        {
            if (!_initialized)
            {
                return;
            }

            _harmony.UnpatchSelf();
            NextInteractionHoverAt.Clear();
            NextWeatherEntityUpdateAt.Clear();
            _config = null;
            _initialized = false;
        }

        private void PatchPrefix(string[] typeNames, string methodName, string prefixName)
        {
            Type? targetType = FindType(typeNames);
            if (targetType == null)
            {
                MelonLogger.Warning($"[{Constants.ModName}] Could not find target type for {methodName} throttle.");
                return;
            }

            MethodInfo? target = AccessTools.Method(targetType, methodName);
            MethodInfo? prefix = AccessTools.Method(typeof(BaseGameThrottleService), prefixName);
            if (target == null || prefix == null)
            {
                MelonLogger.Warning($"[{Constants.ModName}] Could not patch {targetType.FullName}.{methodName}.");
                return;
            }

            _harmony.Patch(target, prefix: new HarmonyLib.HarmonyMethod(prefix));
            MelonLogger.Msg($"[{Constants.ModName}] Patched {targetType.FullName}.{methodName} throttle.");
        }

        private static bool InteractionHoverPrefix(object __instance)
        {
            ModConfig? config = _config;
            if (config == null || !config.ShouldThrottleInteractionHover())
            {
                return true;
            }

            return ShouldRunNow(__instance, NextInteractionHoverAt, config.GetInteractionHoverThrottleHz());
        }

        private static bool WeatherEntityUpdatePrefix(object __instance)
        {
            ModConfig? config = _config;
            if (config == null || !config.ShouldThrottleWeatherEntities())
            {
                return true;
            }

            return ShouldRunNow(__instance, NextWeatherEntityUpdateAt, config.GetWeatherEntityThrottleHz());
        }

        private static bool ShouldRunNow(object instance, Dictionary<int, float> nextRunByInstance, float hz)
        {
            int id = GetInstanceKey(instance);
            if (id == 0)
            {
                return true;
            }

            float now = Time.unscaledTime;
            if (nextRunByInstance.TryGetValue(id, out float nextRunAt) && now < nextRunAt)
            {
                return false;
            }

            nextRunByInstance[id] = now + 1f / hz;
            return true;
        }

        private static int GetInstanceKey(object instance)
        {
            if (instance is UnityEngine.Object unityObject)
            {
                return unityObject.GetInstanceID();
            }

            return instance?.GetHashCode() ?? 0;
        }

        private static Type? FindType(IEnumerable<string> typeNames)
        {
            foreach (string typeName in typeNames)
            {
                Type? type = FindLoadedType(typeName);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private static Type? FindLoadedType(string typeName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type? type = assembly.GetType(typeName, throwOnError: false, ignoreCase: false);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }
    }
}


