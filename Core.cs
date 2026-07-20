using MelonLoader;
using BarsGraphics.Config;
using BarsGraphics.Services;
using BarsGraphics.UI;
using BarsGraphics.Utils;

[assembly: MelonInfo(typeof(BarsGraphics.Core), Constants.ModName, Constants.ModVersion, Constants.ModAuthor)]
[assembly: MelonGame(Constants.GameStudio, Constants.GameName)]

namespace BarsGraphics
{
    public sealed class Core : MelonMod
    {
        private RenderOptimizerService? _optimizer;
        private VisualEnhancementService? _visualEnhancements;
#if BARS_GRAPHICS_DEVELOPMENT
        private PerformanceTestAutomationService? _automation;
        private LiveControlService? _liveControl;
#endif
        private PerformanceMenuService? _menu;
#if BARS_GRAPHICS_DEVELOPMENT
        private BaseGameThrottleService? _baseGameThrottles;
#endif

        public static Core? Instance { get; private set; }

        public ModConfig? Config { get; private set; }

        public override void OnInitializeMelon()
        {
            Instance = this;

            Config = new ModConfig();
            Config.Initialize();

            _optimizer = new RenderOptimizerService(Config);
            _visualEnhancements = new VisualEnhancementService(Config);
#if BARS_GRAPHICS_DEVELOPMENT
            _baseGameThrottles = new BaseGameThrottleService(Config);
            _baseGameThrottles.Initialize();
#endif
            _menu = new PerformanceMenuService(Config);
#if BARS_GRAPHICS_DEVELOPMENT
            _automation = new PerformanceTestAutomationService(Config, _menu);
            _liveControl = new LiveControlService(Config, _optimizer);
            _liveControl.Initialize();
#endif
            LoggerInstance.Msg($"{Constants.ModName} v{Constants.ModVersion} initialized. ActiveProfile={Config.GetActiveProfile()}.");
        }

        public override void OnUpdate()
        {
#if BARS_GRAPHICS_DEVELOPMENT
            _liveControl?.Update();
            _automation?.Update();
#endif
            _menu?.Update();
            _optimizer?.Update();
            _visualEnhancements?.Update();
        }

        public override void OnGUI()
        {
            _menu?.Draw();
        }

        public override void OnApplicationQuit()
        {
            _optimizer?.Restore();
            _visualEnhancements?.Dispose();
            _menu?.Dispose();
#if BARS_GRAPHICS_DEVELOPMENT
            _baseGameThrottles?.Dispose();
            _liveControl?.Dispose();
            _automation = null;
            _liveControl = null;
            _baseGameThrottles = null;
#endif
            _optimizer = null;
            _visualEnhancements = null;
            _menu = null;
            Instance = null;
        }
    }
}


