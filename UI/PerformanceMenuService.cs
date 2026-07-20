using System;
using System.Collections.Generic;
using BarsGraphics.Config;
using BarsGraphics.Models;
using BarsGraphics.Utils;
using bGUI.Components;
using bGUI.Components.Builders;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace BarsGraphics.UI
{
    internal sealed class PerformanceMenuService : IDisposable
    {
        private static readonly Color RootColor = new Color(0.07f, 0.08f, 0.09f, 0.96f);
        private static readonly Color HeaderColor = new Color(0.12f, 0.14f, 0.16f, 1f);
        private static readonly Color SectionColor = new Color(0.10f, 0.115f, 0.13f, 1f);
        private static readonly Color RowColor = new Color(0.09f, 0.10f, 0.115f, 0.86f);
        private static readonly Color ButtonColor = new Color(0.19f, 0.22f, 0.25f, 1f);
        private static readonly Color ActiveButtonColor = new Color(0.20f, 0.38f, 0.62f, 1f);
        private static readonly Color TextColor = new Color(0.94f, 0.96f, 0.98f, 1f);
        private static readonly Color MutedTextColor = new Color(0.72f, 0.76f, 0.78f, 1f);

        private readonly ModConfig _config;
        private readonly Dictionary<object, bool> _pendingBools = new Dictionary<object, bool>();
        private readonly Dictionary<object, float> _pendingFloats = new Dictionary<object, float>();
        private readonly Dictionary<object, int> _pendingInts = new Dictionary<object, int>();
        private readonly Dictionary<object, string> _pendingStrings = new Dictionary<object, string>();
        private CanvasWrapper? _canvas;
        private PanelWrapper? _root;
        private RectTransform? _content;
        private Sprite? _toggleMarkSprite;
        private string? _pendingProfile;
        private bool _visible;
        private bool _dirty;
        private bool _cursorStateCaptured;
        private bool _cursorWasVisible;
        private CursorLockMode _cursorLockState;

        public PerformanceMenuService(ModConfig config)
        {
            _config = config;
        }

        public bool IsVisible => _visible;

        public void Update()
        {
            if (!_config.IsUiMenuEnabled())
            {
                Hide();
                return;
            }

            if (_config.AreHotkeysEnabled() && Input.GetKeyDown(KeyCode.F5))
            {
                Toggle();
            }

            if (_visible && _dirty)
            {
                RebuildContent();
            }
        }

        public void Draw()
        {
            // bGUI uses retained uGUI components, so there is no IMGUI draw step.
        }

        public void Show()
        {
            EnsureUi();
            _visible = true;
            if (_canvas != null)
            {
                _canvas.IsActive = true;
            }

            RebuildContent();
            CaptureCursorState();
            ShowCursorForMenu();
        }

        public void Hide()
        {
            if (!_visible && _canvas == null)
            {
                return;
            }

            _visible = false;
            if (_canvas != null)
            {
                _canvas.IsActive = false;
            }

            RestoreCursorState();
        }

        public void Toggle()
        {
            if (_visible)
            {
                Hide();
            }
            else
            {
                Show();
            }
        }

        public void Dispose()
        {
            Hide();
            if (_canvas != null)
            {
                _canvas.Destroy();
                _canvas = null;
                _root = null;
                _content = null;
            }
        }

        private void EnsureUi()
        {
            if (_canvas != null)
            {
                return;
            }

            _canvas = new CanvasBuilder("BarsGraphics.Canvas")
                .SetRenderMode(RenderMode.ScreenSpaceOverlay)
                .SetSortingOrder(6500)
                .SetReferenceResolution(1920f, 1080f)
                .SetMatchWidthOrHeight(0.5f)
                .Build();

            if (_canvas == null)
            {
                return;
            }

            _root = new PanelBuilder(_canvas.RectTransform)
                .SetBackgroundColor(RootColor)
                .SetSize(980f, 820f)
                .SetAnchor(0f, 1f)
                .SetPivot(0f, 1f)
                .SetPosition(72f, -56f)
                .Build();
            _root.GameObject.name = "BarsGraphics.Root";
            _root.GameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.72f);

            AddHeader(_root.RectTransform);

            var scroll = new ScrollViewBuilder(_root.RectTransform)
                .WithName("BarsGraphics.Scroll")
                .WithVerticalScrolling(true)
                .WithHorizontalScrolling(false)
                .Build() as ScrollViewWrapper;
            if (scroll == null)
            {
                return;
            }

            scroll.RectTransform.anchorMin = new Vector2(0f, 0f);
            scroll.RectTransform.anchorMax = new Vector2(1f, 1f);
            scroll.RectTransform.offsetMin = new Vector2(14f, 72f);
            scroll.RectTransform.offsetMax = new Vector2(-14f, -106f);
            scroll.ScrollRect.movementType = ScrollRect.MovementType.Clamped;
            scroll.ScrollRect.inertia = true;

            _content = scroll.Content;
            var contentLayout = _content.gameObject.AddComponent<VerticalLayoutGroup>();
            contentLayout.padding = new RectOffset(2, 10, 2, 2);
            contentLayout.spacing = 8f;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = false;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;

            var fitter = _content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            AddFooter(_root.RectTransform);
            _canvas.IsActive = false;
        }

        private void AddHeader(Transform parent)
        {
            var header = new PanelBuilder(parent)
                .SetBackgroundColor(HeaderColor)
                .Build();
            header.GameObject.name = "Header";
            header.RectTransform.anchorMin = new Vector2(0f, 1f);
            header.RectTransform.anchorMax = new Vector2(1f, 1f);
            header.RectTransform.pivot = new Vector2(0.5f, 1f);
            header.RectTransform.offsetMin = new Vector2(14f, -96f);
            header.RectTransform.offsetMax = new Vector2(-14f, -14f);

            var layout = header.GameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 8, 8);
            layout.spacing = 4f;
            layout.childControlWidth = true;
            layout.childControlHeight = false;

            AddText(header.RectTransform, $"{Constants.ModName} v{Constants.ModVersion}", 24, FontStyle.Bold, TextColor, TextAnchor.MiddleLeft, 32f);
            AddText(header.RectTransform, $"Profile: {_config.GetActiveProfile()}    Optimizer: {OnOff(_config.IsOptimizerEnabled())}    Menu: F5    Toggle optimizer: F6", 14, FontStyle.Normal, MutedTextColor, TextAnchor.MiddleLeft, 24f);
        }

        private void AddFooter(Transform parent)
        {
            var footer = new PanelBuilder(parent)
                .SetBackgroundColor(HeaderColor)
                .Build();
            footer.GameObject.name = "Footer";
            footer.RectTransform.anchorMin = new Vector2(0f, 0f);
            footer.RectTransform.anchorMax = new Vector2(1f, 0f);
            footer.RectTransform.pivot = new Vector2(0.5f, 0f);
            footer.RectTransform.offsetMin = new Vector2(14f, 14f);
            footer.RectTransform.offsetMax = new Vector2(-14f, 62f);

            var layout = footer.GameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 8, 8);
            layout.spacing = 8f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;

            AddButton(footer.RectTransform, "Apply", 100f, ApplyPendingChanges, HasPendingChanges());
            AddButton(footer.RectTransform, "Revert", 100f, RevertPendingChanges);
            AddButton(footer.RectTransform, "Hide", 100f, Hide);
            AddSpacer(footer.RectTransform);
            AddText(footer.RectTransform, HasPendingChanges() ? "Pending changes. Apply to update graphics settings." : "Changes are staged until Apply.", 13, FontStyle.Normal, MutedTextColor, TextAnchor.MiddleRight, 30f, true);
        }

        private void RebuildContent()
        {
            _dirty = false;
            EnsureUi();
            if (_content == null)
            {
                return;
            }

            ClearChildren(_content);
            AddProfiles();
            AddGeneralOptions();
            AddVisualEnhancementOptions();
            AddResolutionOptions();
            AddShadowOptions();
            AddLodOptions();
            AddPostAndReflectionOptions();
            AddLightingOptions();
            AddCameraAndCullingOptions();
            AddUrpFeatureOptions();
#if BARS_GRAPHICS_DEVELOPMENT
            AddBaseGameThrottleOptions();
#endif
            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
            if (_root != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(_root.RectTransform);
            }
        }

        private void AddProfiles()
        {
            Section("Profiles");
            var row = Row(48f);
            ProfileButton(row, "Off");
            ProfileButton(row, "Conservative", 122f);
            ProfileButton(row, "Balanced");
            ProfileButton(row, "Aggressive", 116f);
            ProfileButton(row, "Custom");
            AddButton(row, "Copy to Custom", 150f, CopyActiveProfileToCustom);

            OptimizationProfileDefinition profile = OptimizationProfileCatalog.Get(GetStagedProfile());
            AddText(_content!, $"{profile.Label}: Performance: {profile.PerformanceEffect}. Visuals: {profile.QualityImpact}. {profile.Notes}", 13, FontStyle.Normal, MutedTextColor, TextAnchor.MiddleLeft, 44f, true);
        }

        private void AddGeneralOptions()
        {
            Section("General");
            Toggle("Optimizer enabled", "Master switch. Turning it off restores captured render settings.", _config.EnableOptimizer, false);
            Toggle("Hotkeys", "Enables F5 menu toggle and F6 optimizer toggle.", _config.EnableHotkeys, false);
            Toggle("UI menu", "Allows this menu to open. Turning it off hides the menu until re-enabled in preferences.", _config.EnableUiMenu, false);
            Toggle("Diagnostics logging", "Logs applied settings and scene counts on profile changes.", _config.LogDiagnostics, false);
        }

        private void AddVisualEnhancementOptions()
        {
            Section("Visual Enhancements (Optional)");
            Choice("Visual style", _config.VisualStyle, VisualStyleCatalog.RuntimeStyleIds, false);
            Slider("Style intensity", "Blends from the original image at 0 to the full style at 1.", _config.VisualStyleIntensity, 0f, 1f, 0.05f, false);

            VisualStyleDefinition style = VisualStyleCatalog.Get(GetValue(_config.VisualStyle, "Off"));
            AddText(_content!, $"{style.Description} Color-only styles reuse URP's existing grading path. Aggressive or Custom settings that disable post-processing take priority.", 13, FontStyle.Normal, MutedTextColor, TextAnchor.MiddleLeft, 44f, true);
        }

        private void AddResolutionOptions()
        {
            Section("Resolution, Textures, And Frame Pacing");
            Toggle("Use FSR upscaling", "Preferred internal resolution scaler. Uses URP FSR so lower scale is less soft than plain render scale.", _config.UseFsrUpscaling);
            Slider("FSR render scale", "0.8 was the measured Balanced candidate; lower values are more aggressive.", _config.FsrRenderScale, 0.5f, 1f, 0.01f);
            Slider("FSR sharpness", "Too high can shimmer or over-sharpen edges.", _config.FsrSharpness, 0f, 1f, 0.05f);
            Toggle("Use legacy render scale", "Custom-only fallback internal scaling without FSR reconstruction.", _config.UseRenderScale);
            Slider("Legacy render scale", "Plain render scale. Quality drops everywhere, so keep this as an opt-in fallback.", _config.RenderScale, 0.35f, 1f, 0.01f);
            Toggle("Use texture mip limit", "Usually saves VRAM more than FPS.", _config.UseGlobalTextureMipmapLimit);
            Slider("Texture mip limit", "0 full, 1 half, 2 quarter, 3 eighth.", _config.GlobalTextureMipmapLimit, 0, 3);
            Toggle("Use anisotropic filtering", "Applies Unity anisotropic filtering mode. The measured candidate kept this enabled.", _config.UseAnisotropicFiltering);
            Choice("Anisotropic mode", _config.AnisotropicFilteringMode, new[] { "Disable", "Enable", "Force" }, new[] { 0, 1, 2 });
            Toggle("Use frame settings", "Enables VSync and target-frame-rate overrides for uncapping or controlled testing.", _config.UseFrameRateSettings);
            Slider("VSync count", "0 disables VSync; higher values sync to display intervals.", _config.VSyncCount, 0, 4);
            Slider("Target FPS", "-1 leaves Unity default; higher values uncap when VSync is disabled.", _config.TargetFrameRate, -1, 240);
        }

        private void AddShadowOptions()
        {
            Section("Shadows");
            Toggle("Use shadow settings", "Enables custom shadow distance, cascades, and resolution.", _config.UseShadowSettings);
            Slider("Shadow distance", "Shorter distances draw fewer realtime shadows.", _config.ShadowDistance, 0f, 80f, 1f);
            Slider("Shadow cascades", "Fewer cascades reduce shadow work.", _config.ShadowCascades, 0, 4);
            Choice("Shadow resolution", _config.ShadowResolution, new[] { "Low", "Medium", "High", "Very High" }, new[] { 0, 1, 2, 3 });
        }

        private void AddLodOptions()
        {
            Section("LOD");
            Toggle("Use LOD settings", "Enables custom LOD bias and maximum LOD.", _config.UseLodSettings);
            Slider("LOD bias", "Lower values make far objects use simpler models sooner.", _config.LodBias, 0.35f, 1f, 0.01f);
            Slider("Maximum LOD level", "Keep 0 unless testing missing or invisible LOD risk.", _config.MaximumLodLevel, 0, 3);
            Toggle("Use shader LOD cap", "Caps shader detail level.", _config.UseShaderMaximumLod);
            Slider("Shader LOD cap", "Measured 400 candidate was visually acceptable in Townhall.", _config.ShaderMaximumLod, 100, 1000, 50);
            Toggle("Protect near LODs", "Forces nearby LOD groups to full detail.", _config.ProtectNearLods);
            Slider("Near LOD protection", "Distance around the active camera that stays at full-detail LOD.", _config.NearLodProtectionDistance, 5f, 80f, 1f);
        }

        private void AddPostAndReflectionOptions()
        {
            Section("Post Processing And Reflections");
            Toggle("Disable post processing", "Disables camera post effects where accessible.", _config.DisablePostProcessing);
            Toggle("Disable realtime reflection probes", "Stops realtime reflection probe updates.", _config.DisableRealtimeReflectionProbes);
            Toggle("Disable reflection probes", "Can flatten shiny surfaces; more aggressive than realtime-only.", _config.DisableReflectionProbes);
            Toggle("Disable volumetric light beams", "Turns off volumetric beam behaviours.", _config.DisableVolumetricLightBeams);
        }

        private void AddLightingOptions()
        {
            Section("Lighting");
            Toggle("Use pixel light count", "Enables a cap on legacy per-pixel lights.", _config.UsePixelLightCount);
            Slider("Pixel light count", "Caps how many lights render per pixel in supported paths.", _config.PixelLightCount, 0, 8);
            Toggle("Use additional lights mode", "Enables URP additional-light mode changes.", _config.UseAdditionalLightsMode);
            Choice("Additional lights mode", _config.AdditionalLightsMode, new[] { "PerVertex", "Disabled" });
            Slider("Max additional lights", "Caps URP additional lights where exposed.", _config.MaxAdditionalLightsCount, 0, 8);
            Toggle("Disable far light shadows", "Removes non-directional light shadows past the distance below.", _config.DisableFarLightShadows);
            Slider("Far light shadow distance", "Lights farther than this from the main camera lose shadows.", _config.FarLightShadowDistance, 0f, 120f, 1f);
        }

        private void AddCameraAndCullingOptions()
        {
            Section("Camera, Terrain, And Culling");
            Toggle("Use camera far clip", "Lower values reduce distant rendering but can visibly cut skyline/world.", _config.UseCameraFarClip);
            Slider("Camera far clip", "Maximum camera draw distance when far-clip override is enabled.", _config.CameraFarClipDistance, 20f, 800f, 5f);
            Toggle("Disable camera stacks", "Clears URP camera stacks. Can hide the player's phone UI; keep this as a manual diagnostic only.", _config.DisableCameraStacks);
            Toggle("Disable terrain foliage", "Aggressive/custom only. Removes terrain trees and grass.", _config.DisableTerrainFoliage);
            Slider("Terrain detail distance", "Detail distance used when terrain foliage is disabled.", _config.TerrainDetailObjectDistance, 0f, 150f, 5f);
            Toggle("Use layer cull distances", "Can save render cost but risks hiding important world geometry.", _config.UseLayerCullDistances);
            Slider("Layer cull distance", "Distance cap applied to camera layers.", _config.LayerCullDistance, 20f, 500f, 5f);
            Toggle("Disable occlusion culling", "Only useful if culling CPU cost outweighs saved rendering.", _config.DisableCameraOcclusionCulling);
            Toggle("Visibility-safe renderer culling", "Experimental renderer hiding outside the camera frustum.", _config.UseVisibilitySafeRendererCulling);
            Slider("Renderer cull min distance", "Never hides renderers closer than this distance.", _config.RendererCullMinDistance, 5f, 120f, 1f);
        }

        private void AddUrpFeatureOptions()
        {
            Section("URP Features");
            Toggle("Disable outline feature", "Removes an extra outline render pass.", _config.DisableOutlineFeature);
            Toggle("Use anti-aliasing", "Enables custom quality-level MSAA.", _config.UseAntiAliasing);
            Slider("Anti-aliasing", "0 disables quality-level MSAA where honored.", _config.AntiAliasing, 0, 8);
        }

#if BARS_GRAPHICS_DEVELOPMENT
        private void AddBaseGameThrottleOptions()
        {
            Section("Base-Game CPU Throttles");
            Toggle("Interaction hover throttle", "Runs hover checks at a capped cadence instead of every frame.", _config.EnableInteractionHoverThrottle, false);
            Slider("Hover throttle Hz", "Lower values reduce CPU work but can make prompts less immediate.", _config.InteractionHoverThrottleHz, 10f, 60f, 1f, false);
            Toggle("Weather entity throttle", "Updates weather entities less often.", _config.EnableWeatherEntityThrottle, false);
            Slider("Weather throttle Hz", "Lower values reduce CPU work; weather-volume changes may react later.", _config.WeatherEntityThrottleHz, 1f, 30f, 1f, false);
        }
#endif

        private void ProfileButton(Transform parent, string profile, float width = 104f)
        {
            bool active = string.Equals(GetStagedProfile(), profile, StringComparison.OrdinalIgnoreCase);
            AddButton(parent, profile, width, () =>
            {
                _pendingProfile = OptimizationProfileCatalog.Normalize(profile);
                MarkDirty();
            }, active);
        }

        private void Section(string title)
        {
            var panel = new PanelBuilder(_content)
                .SetBackgroundColor(SectionColor)
                .SetSize(0f, 34f)
                .Build();
            panel.GameObject.name = $"Section.{title}";
            AddLayoutElement(panel.GameObject, -1f, 34f, 0f);
            AddText(panel.RectTransform, title, 15, FontStyle.Bold, TextColor, TextAnchor.MiddleLeft, 30f, true, 10f);
        }

        private Transform Row(float height = 38f)
        {
            var panel = new PanelBuilder(_content)
                .SetBackgroundColor(RowColor)
                .SetSize(0f, height)
                .Build();
            panel.GameObject.name = "Row";
            AddLayoutElement(panel.GameObject, -1f, height, 0f);

            var layout = panel.GameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 5, 5);
            layout.spacing = 8f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;
            return panel.RectTransform;
        }

        private void Toggle(string label, string description, MelonPreferences_Entry<bool>? entry, bool makesCustom = true)
        {
            bool current = GetValue(entry, false);
            var row = Row();
            var toggle = new ToggleBuilder(row)
                .SetLabel(label)
                .SetIsOn(current)
                .SetSize(260f, 28f)
                .SetLabelColor(TextColor)
                .SetCheckmarkImage(GetToggleMarkSprite())
                .OnValueChanged(next =>
                {
                    if (entry == null || next == GetValue(entry, false))
                    {
                        return;
                    }

                    if (makesCustom)
                    {
                        SetCustom();
                    }

                    _pendingBools[entry] = next;
                    MarkFooterDirty();
                })
                .Build();
            AddLayoutElement(toggle.GameObject, 260f, 28f, 0f);
            AddText(row, description, 13, FontStyle.Normal, MutedTextColor, TextAnchor.MiddleLeft, 28f, true);
        }

        private void Slider(string label, string description, MelonPreferences_Entry<float>? entry, float min, float max, float step, bool makesCustom = true)
        {
            float current = GetValue(entry, min);
            var row = Row(42f);
            TextWrapper valueText = AddText(row, $"{label}: {FormatNumber(current)}", 13, FontStyle.Normal, TextColor, TextAnchor.MiddleLeft, 30f, false, 0f, 250f);
            var slider = new SliderBuilder(row)
                .SetRange(min, max)
                .SetValue(current)
                .SetSize(220f, 24f)
                .OnValueChanged(raw =>
                {
                    if (entry == null)
                    {
                        return;
                    }

                    float next = RoundToStep(raw, step);
                    if (Math.Abs(next - GetValue(entry, min)) <= 0.0001f)
                    {
                        return;
                    }

                    if (makesCustom)
                    {
                        SetCustom();
                    }

                    _pendingFloats[entry] = next;
                    valueText.Content = $"{label}: {FormatNumber(next)}";
                    MarkFooterDirty();
                })
                .Build();
            AddLayoutElement(slider.GameObject, 220f, 24f, 0f);
            AddText(row, description, 13, FontStyle.Normal, MutedTextColor, TextAnchor.MiddleLeft, 30f, true);
        }

        private void Slider(string label, string description, MelonPreferences_Entry<int>? entry, int min, int max, int step = 1, bool makesCustom = true)
        {
            int current = GetValue(entry, min);
            var row = Row(42f);
            TextWrapper valueText = AddText(row, $"{label}: {current}", 13, FontStyle.Normal, TextColor, TextAnchor.MiddleLeft, 30f, false, 0f, 250f);
            var slider = new SliderBuilder(row)
                .SetRange(min, max)
                .SetWholeNumbers(true)
                .SetValue(current)
                .SetSize(220f, 24f)
                .OnValueChanged(raw =>
                {
                    if (entry == null)
                    {
                        return;
                    }

                    int next = Mathf.RoundToInt(RoundToStep(raw, step));
                    if (next == GetValue(entry, min))
                    {
                        return;
                    }

                    if (makesCustom)
                    {
                        SetCustom();
                    }

                    _pendingInts[entry] = next;
                    valueText.Content = $"{label}: {next}";
                    MarkFooterDirty();
                })
                .Build();
            AddLayoutElement(slider.GameObject, 220f, 24f, 0f);
            AddText(row, description, 13, FontStyle.Normal, MutedTextColor, TextAnchor.MiddleLeft, 30f, true);
        }

        private void Choice(string label, MelonPreferences_Entry<int>? entry, string[] labels, int[] values)
        {
            int current = GetValue(entry, values[0]);
            var row = Row(42f);
            int safeIndex = Math.Max(0, Array.IndexOf(values, current));
            AddText(row, $"{label}: {labels[safeIndex]}", 13, FontStyle.Normal, TextColor, TextAnchor.MiddleLeft, 30f, false, 0f, 250f);
            for (int index = 0; index < labels.Length; index++)
            {
                int value = values[index];
                AddButton(row, labels[index], 96f, () =>
                {
                    if (entry == null)
                    {
                        return;
                    }

                    SetCustom();
                    _pendingInts[entry] = value;
                    MarkDirty();
                }, value == current);
            }
        }

        private void Choice(string label, MelonPreferences_Entry<string>? entry, string[] values, bool makesCustom = true)
        {
            string current = GetValue(entry, values[0]);
            var row = Row(42f);
            AddText(row, $"{label}: {current}", 13, FontStyle.Normal, TextColor, TextAnchor.MiddleLeft, 30f, false, 0f, 250f);
            foreach (string value in values)
            {
                AddButton(row, value, 112f, () =>
                {
                    if (entry == null)
                    {
                        return;
                    }

                    if (makesCustom)
                    {
                        SetCustom();
                    }

                    _pendingStrings[entry] = value;
                    MarkDirty();
                }, string.Equals(value, current, StringComparison.OrdinalIgnoreCase));
            }
        }

        private TextWrapper AddText(Transform parent, string content, int fontSize, FontStyle style, Color color, TextAnchor alignment, float height, bool flexible = false, float leftPadding = 0f, float width = -1f)
        {
            var text = new TextBuilder(parent)
                .SetContent(content)
                .SetFontSize(fontSize)
                .SetFontStyle(style)
                .SetColor(color)
                .SetAlignment(alignment)
                .SetSize(Mathf.Max(1f, width), height)
                .Build();
            text.TextComponent.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.TextComponent.verticalOverflow = VerticalWrapMode.Truncate;
            text.TextComponent.raycastTarget = false;
            if (leftPadding > 0f)
            {
                text.RectTransform.offsetMin = new Vector2(leftPadding, 0f);
            }

            AddLayoutElement(text.GameObject, width, height, flexible ? 1f : 0f);
            return text;
        }

        private ButtonWrapper AddButton(Transform parent, string text, float width, Action action, bool active = false)
        {
            var button = new ButtonBuilder(parent)
                .SetText(text)
                .SetSize(width, 30f)
                .SetBackgroundColor(active ? ActiveButtonColor : ButtonColor)
                .OnClick(action)
                .Build();
            AddLayoutElement(button.GameObject, width, 30f, 0f);
            var label = button.GameObject.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.color = TextColor;
                label.fontSize = 13;
                label.fontStyle = active ? FontStyle.Bold : FontStyle.Normal;
            }

            return button;
        }

        private static void AddSpacer(Transform parent)
        {
            var spacer = new GameObject("Spacer");
            var rect = spacer.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            AddLayoutElement(spacer, 1f, 1f, 1f);
        }

        private Sprite GetToggleMarkSprite()
        {
            if (_toggleMarkSprite != null)
            {
                return _toggleMarkSprite;
            }

            const int size = 16;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color transparent = new Color(0f, 0f, 0f, 0f);
            Color mark = new Color(0.30f, 0.78f, 0.36f, 1f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    texture.SetPixel(x, y, transparent);
                }
            }

            for (int i = 0; i < 6; i++)
            {
                texture.SetPixel(3 + i, 7 - i / 2, mark);
                texture.SetPixel(4 + i, 7 - i / 2, mark);
                texture.SetPixel(8 + i, 5 + i, mark);
                texture.SetPixel(9 + i, 5 + i, mark);
            }

            texture.Apply();
            _toggleMarkSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
            return _toggleMarkSprite;
        }

        private static void AddLayoutElement(GameObject gameObject, float preferredWidth, float preferredHeight, float flexibleWidth)
        {
            var layout = gameObject.GetComponent<LayoutElement>() ?? gameObject.AddComponent<LayoutElement>();
            if (preferredWidth >= 0f)
            {
                layout.preferredWidth = preferredWidth;
            }

            if (preferredHeight >= 0f)
            {
                layout.preferredHeight = preferredHeight;
                layout.minHeight = preferredHeight;
            }

            layout.flexibleWidth = flexibleWidth;
        }

        private static void ClearChildren(RectTransform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(parent.GetChild(i).gameObject);
            }
        }

        private void MarkDirty()
        {
            _dirty = true;
        }

        private void MarkFooterDirty()
        {
            // Avoid rebuilding the whole scroll tree while sliders are being dragged.
        }

        private bool HasPendingChanges()
        {
            return _pendingProfile != null ||
                   _pendingBools.Count > 0 ||
                   _pendingFloats.Count > 0 ||
                   _pendingInts.Count > 0 ||
                   _pendingStrings.Count > 0;
        }

        private string GetStagedProfile()
        {
            return _pendingProfile ?? _config.GetActiveProfile();
        }

        private bool GetValue(MelonPreferences_Entry<bool>? entry, bool fallback)
        {
            if (entry == null)
            {
                return fallback;
            }

            return _pendingBools.TryGetValue(entry, out bool value) ? value : entry.Value;
        }

        private float GetValue(MelonPreferences_Entry<float>? entry, float fallback)
        {
            if (entry == null)
            {
                return fallback;
            }

            return _pendingFloats.TryGetValue(entry, out float value) ? value : entry.Value;
        }

        private int GetValue(MelonPreferences_Entry<int>? entry, int fallback)
        {
            if (entry == null)
            {
                return fallback;
            }

            return _pendingInts.TryGetValue(entry, out int value) ? value : entry.Value;
        }

        private string GetValue(MelonPreferences_Entry<string>? entry, string fallback)
        {
            if (entry == null)
            {
                return fallback;
            }

            return _pendingStrings.TryGetValue(entry, out var value) ? value ?? fallback : entry.Value ?? fallback;
        }

        private void ApplyPendingChanges()
        {
            if (_pendingProfile != null)
            {
                _config.SetActiveProfile(_pendingProfile, false);
            }

            foreach (KeyValuePair<object, bool> pending in _pendingBools)
            {
                if (pending.Key is MelonPreferences_Entry<bool> entry)
                {
                    entry.Value = pending.Value;
                }
            }

            foreach (KeyValuePair<object, float> pending in _pendingFloats)
            {
                if (pending.Key is MelonPreferences_Entry<float> entry)
                {
                    entry.Value = pending.Value;
                }
            }

            foreach (KeyValuePair<object, int> pending in _pendingInts)
            {
                if (pending.Key is MelonPreferences_Entry<int> entry)
                {
                    entry.Value = pending.Value;
                }
            }

            foreach (KeyValuePair<object, string> pending in _pendingStrings)
            {
                if (pending.Key is MelonPreferences_Entry<string> entry)
                {
                    entry.Value = pending.Value;
                }
            }

            ClearPendingChanges();
            _config.Save();
            MarkDirty();
        }

        private void RevertPendingChanges()
        {
            ClearPendingChanges();
            MarkDirty();
        }

        private void ClearPendingChanges()
        {
            _pendingProfile = null;
            _pendingBools.Clear();
            _pendingFloats.Clear();
            _pendingInts.Clear();
            _pendingStrings.Clear();
        }

        private void CopyActiveProfileToCustom()
        {
            EffectiveRenderSettings settings = EffectiveRenderSettings.FromProfile(_config, GetStagedProfile());

            SetValue(_config.UseRenderScale, settings.UseRenderScale);
            SetValue(_config.RenderScale, settings.RenderScale);
            SetValue(_config.UseFsrUpscaling, settings.UseFsrUpscaling);
            SetValue(_config.FsrRenderScale, settings.FsrRenderScale);
            SetValue(_config.FsrSharpness, settings.FsrSharpness);
            SetValue(_config.UseShadowSettings, settings.UseShadowSettings);
            SetValue(_config.ShadowDistance, settings.ShadowDistance);
            SetValue(_config.ShadowCascades, settings.ShadowCascades);
            SetValue(_config.ShadowResolution, ShadowResolutionToInt(settings.ShadowResolution));
            SetValue(_config.UseLodSettings, settings.UseLodSettings);
            SetValue(_config.LodBias, settings.LodBias);
            SetValue(_config.MaximumLodLevel, settings.MaximumLodLevel);
            SetValue(_config.UseShaderMaximumLod, settings.UseShaderMaximumLod);
            SetValue(_config.ShaderMaximumLod, settings.ShaderMaximumLod);
            SetValue(_config.ProtectNearLods, settings.ProtectNearLods);
            SetValue(_config.NearLodProtectionDistance, settings.NearLodProtectionDistance);
            SetValue(_config.DisablePostProcessing, settings.DisablePostProcessing);
            SetValue(_config.DisableRealtimeReflectionProbes, settings.DisableRealtimeReflectionProbes);
            SetValue(_config.DisableReflectionProbes, settings.DisableReflectionProbes);
            SetValue(_config.UseFrameRateSettings, settings.UseFrameRateSettings);
            SetValue(_config.VSyncCount, settings.VSyncCount);
            SetValue(_config.TargetFrameRate, settings.TargetFrameRate);
            SetValue(_config.UsePixelLightCount, settings.UsePixelLightCount);
            SetValue(_config.PixelLightCount, settings.PixelLightCount);
            SetValue(_config.UseAntiAliasing, settings.UseAntiAliasing);
            SetValue(_config.AntiAliasing, settings.AntiAliasing);
            SetValue(_config.UseGlobalTextureMipmapLimit, settings.UseGlobalTextureMipmapLimit);
            SetValue(_config.GlobalTextureMipmapLimit, settings.GlobalTextureMipmapLimit);
            SetValue(_config.UseAnisotropicFiltering, settings.UseAnisotropicFiltering);
            SetValue(_config.AnisotropicFilteringMode, AnisotropicFilteringToInt(settings.AnisotropicFilteringMode));
            SetValue(_config.UseAdditionalLightsMode, settings.UseAdditionalLightsMode);
            SetValue(_config.AdditionalLightsMode, settings.AdditionalLightsMode);
            SetValue(_config.MaxAdditionalLightsCount, settings.MaxAdditionalLightsCount ?? 0);
            SetValue(_config.DisableFarLightShadows, settings.DisableFarLightShadows);
            SetValue(_config.FarLightShadowDistance, settings.FarLightShadowDistance);
            SetValue(_config.UseCameraFarClip, settings.UseCameraFarClip);
            SetValue(_config.CameraFarClipDistance, settings.CameraFarClipDistance);
            SetValue(_config.DisableCameraStacks, settings.DisableCameraStacks);
            SetValue(_config.UseLayerCullDistances, settings.UseLayerCullDistances);
            SetValue(_config.LayerCullDistance, settings.LayerCullDistance);
            SetValue(_config.DisableCameraOcclusionCulling, settings.DisableCameraOcclusionCulling);
            SetValue(_config.DisableVolumetricLightBeams, settings.DisableVolumetricLightBeams);
            SetValue(_config.UseVisibilitySafeRendererCulling, settings.UseVisibilitySafeRendererCulling);
            SetValue(_config.RendererCullMinDistance, settings.RendererCullMinDistance);
            SetValue(_config.DisableTerrainFoliage, settings.DisableTerrainFoliage);
            SetValue(_config.TerrainDetailObjectDistance, settings.TerrainDetailObjectDistance);
            SetValue(_config.DisableOutlineFeature, settings.DisableOutlineFeature);

            SetCustom();
            MarkDirty();
        }

        private void SetCustom()
        {
            _pendingProfile = "Custom";
        }

        private void SetValue(MelonPreferences_Entry<bool>? entry, bool value)
        {
            if (entry != null)
            {
                _pendingBools[entry] = value;
            }
        }

        private void SetValue(MelonPreferences_Entry<float>? entry, float value)
        {
            if (entry != null)
            {
                _pendingFloats[entry] = value;
            }
        }

        private void SetValue(MelonPreferences_Entry<int>? entry, int value)
        {
            if (entry != null)
            {
                _pendingInts[entry] = value;
            }
        }

        private void SetValue(MelonPreferences_Entry<string>? entry, string value)
        {
            if (entry != null)
            {
                _pendingStrings[entry] = value;
            }
        }

        private static int ShadowResolutionToInt(ShadowResolution resolution)
        {
            switch (resolution)
            {
                case ShadowResolution.Low:
                    return 0;
                case ShadowResolution.High:
                    return 2;
                case ShadowResolution.VeryHigh:
                    return 3;
                default:
                    return 1;
            }
        }

        private static int AnisotropicFilteringToInt(AnisotropicFiltering mode)
        {
            switch (mode)
            {
                case AnisotropicFiltering.Disable:
                    return 0;
                case AnisotropicFiltering.ForceEnable:
                    return 2;
                default:
                    return 1;
            }
        }

        private static float RoundToStep(float value, float step)
        {
            if (step <= 0f)
            {
                return value;
            }

            return Mathf.Round(value / step) * step;
        }

        private static string FormatNumber(float value)
        {
            return value.ToString(value >= 10f ? "0" : "0.##", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string OnOff(bool value)
        {
            return value ? "On" : "Off";
        }

        private void CaptureCursorState()
        {
            if (_cursorStateCaptured)
            {
                return;
            }

            _cursorWasVisible = Cursor.visible;
            _cursorLockState = Cursor.lockState;
            _cursorStateCaptured = true;
        }

        private static void ShowCursorForMenu()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void RestoreCursorState()
        {
            if (!_cursorStateCaptured)
            {
                return;
            }

            Cursor.lockState = _cursorLockState;
            Cursor.visible = _cursorWasVisible;
            _cursorStateCaptured = false;
        }
    }
}
