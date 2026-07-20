# BarsGraphics measurement notes

## Optional visual styles

- `Natural`, `Vibrant`, `Warm Film`, and `Cool Film` use a high-priority global URP Volume containing only `ColorAdjustments` and `WhiteBalance` overrides.
- The styles do not add a renderer feature, custom shader, LUT asset, bloom pyramid, depth texture requirement, or extra full-screen copy.
- They are independent from optimization profiles and default to `Off`. If the active optimizer settings disable post processing, the optimizer wins and the style Volume is disabled.
- Runtime cost still needs same-scene GPU-frame-time validation on Mono and IL2CPP. Treat differences below the existing 3-5 FPS noise threshold as neutral, and verify phone/TAB UI readability in every retained style.

## Measurement rules

- Use the game's native `showfps` HUD label through live control.
- Baseline requires a fresh game process or a verified `ActiveProfile=Off` state before any custom settings are applied.
- Test setup: `SaveGame_2`, Townhall teleport, `04:00`, clear weather, HUD restored.
- Default probe length is 30 seconds for iteration.
- Run a second 30 second confirmation only when a candidate shows a meaningful gain beyond normal noise.
- Track total FPS separately from incremental FPS. Do not call a candidate a 2x improvement unless total FPS is at least 2x the matching baseline in the same scenario.
- Screenshot every retained or plausible candidate and reject candidates that hide nearby gameplay geometry or important world affordances.
- Keep local toggle-impact data in `Models/OptimizationImpactCatalog.cs`. This catalog and the live `optimizationImpacts` endpoint are compiled only in `MonoDevelopment` and `Il2cppDevelopment` builds.
- Record deltas as measured FPS change against the stated context. Do not mix total-vs-baseline gains with incremental-on-stack gains.

## Baseline

| Date | Scenario | Profile | Seconds | Avg FPS | Min | Max | Screenshot | Notes |
| --- | --- | --- | ---: | ---: | ---: | ---: | --- | --- |
| 2026-06-24 | Townhall, 04:00, clear | Off | 60 | 83.99 | 79 | 92 | `baseline-ready-townhall-0400-clear.png` | Valid baseline screenshot with HUD, building, fountain, and nearby objects visible. |
| 2026-06-24 | Townhall, 04:00, clear | Off | screenshot only | n/a | n/a | n/a | `fresh-baseline-off-townhall-0400-before-custom.png` | Fresh post-restart baseline screenshot after deploying live-control fixes. HUD, Townhall, fountain, and nearby geometry visible. |

## Current stack

This is the current best safe stack before any extra feature probe:

- `shadowDistance=0`
- `shadowCascades=0`
- `shadowResolution=Low`
- `lodBias=0.5`
- `maximumLODLevel=0`
- `vSyncCount=0`
- `targetFrameRate=200`
- `disableCameraStacks=true` removed from automatic profiles after validation showed it can hide the player's phone UI. Keep it as a manual Custom/live-control diagnostic only.
- `disablePostProcessing=true`
- `additionalLightsRenderingMode=Disabled`
- `maxAdditionalLightsCount=0`
- `layerCullDistance=100`
- No render-scale reduction
- No renderer hiding
- `intermediateTextureMode=Auto` removed after runtime profile changes were confirmed to hide the phone and character content in the TAB screen until restart.
- `URPOutlineFeature=false` retained as a low-impact renderer-feature setting, pending multi-location interaction/readability validation.

| Date | Scenario | Total stack | Seconds | Avg FPS | Total vs baseline | Screenshot | Notes |
| --- | --- | --- | ---: | ---: | ---: | --- | --- |
| 2026-06-24 | Townhall, 04:00, clear | Current safe stack | 60 | 129.28 | 1.54x | `best-current-townhall-0400-clear-after-valid-baseline.png` | Valid optimized screenshot; HUD and nearby geometry visible. |
| 2026-06-24 | Townhall, 04:00, clear | Current safe stack | 30 | 135.82 | 1.62x | `fresh-current-safe-stack-townhall-0400.png` | Fresh post-restart comparator. Screenshot preserves Townhall, fountain, HUD, and nearby geometry. |

## Candidate probes

These are incremental probes on top of the current safe stack unless stated otherwise.

| Date | Candidate | Seconds | Avg FPS | Incremental vs current stack | Total vs baseline | Screenshot | Decision | Quality notes |
| --- | --- | ---: | ---: | ---: | ---: | --- | --- | --- |
| 2026-06-24 | `VFXRainFeature=false` | 60 | 124.71 | -4.57 | 1.48x | `townhall-0400-best-vfxrain-off-rerun.png` | Reject | No useful FPS gain. |
| 2026-06-24 | `Beautify=false` | 60 | 130.46 | +1.18 | 1.55x | `best-plus-beautify-off-townhall-0400-clear.png` | Reject for now | Too small to retain as a proven tweak. |
| 2026-06-24 | `GrabScreenFeature=false` | 60 | 130.11 | +0.83 | 1.55x | `best-plus-grabscreen-off-townhall-0400-clear.png` | Reject for now | Too small to retain as a proven tweak. |
| 2026-06-24 | `ScheduleOneFog=false` | 60 | 129.95 | +0.67 | 1.55x | `best-plus-fog-off-townhall-0400-clear.png` | Reject | No useful gain; visual tradeoff. |
| 2026-06-24 | `URPOutlineFeature=false` | 60 | 131.36 | +2.08 | 1.56x | `best-plus-outline-off-townhall-0400-clear.png` | Unconfirmed | Small gain; may affect interactable outlines. Needs 30s repeat only if stacking later. |
| 2026-06-24 | `Stylized Water 2=false` | 60 | 136.71 | +7.43 | 1.63x | `best-plus-stylized-water-off-townhall-0400-clear.png` | Likely reject | Total FPS includes current safe stack. This may remove/flatten important water, including map-edge water that teleports players back to the map. Needs safer alternative before retaining. |
| 2026-06-24 | `supportsHDR=false` | 30 | 128.85 | -6.97 | 1.53x | `fresh-current-plus-hdr-off-townhall-0400.png` | Reject | Direct URP asset setter worked, but FPS regressed. |
| 2026-06-24 | `intermediateTextureMode=Auto` | 30 | 135.20 | -0.62 | 1.61x | `fresh-current-plus-intermediate-auto-townhall-0400.png` | Removed after UI regression | Applied to all 3 renderer data assets. The original sample missed a later-confirmed regression where applying a profile at runtime hides TAB screen content until restart. |
| 2026-06-24 | `supportsCameraDepthTexture=false` | 30 | 135.62 | -0.20 | 1.61x | `fresh-current-plus-depth-texture-off-townhall-0400.png` | Reject | Neutral. Earlier combined depth+opaque test is invalid because the offence notice was on screen. |
| 2026-06-24 | `supportsCameraOpaqueTexture=false` | 30 | 134.68 | -1.14 | 1.60x | `fresh-current-plus-opaque-texture-off-townhall-0400.png` | Reject | Slightly worse. |
| 2026-06-24 | `globalTextureMipmapLimit=2` on Balanced | 20s x3 A/B | 104.65 | +1.50 vs limit 0 | n/a | `masterTextureLimit-daylight-20260624-230349-Balanced-limit2.png` | Advanced/niche only | Follow-up after inspecting `FPSBoostMod.dll`; Unity 2022 exposes the modern `globalTextureMipmapLimit` API for the same global texture mip cap. Tight Balanced A/B at Townhall 04:00 averaged 103.15 FPS at limit 0 vs 104.65 FPS at limit 2. Later passes were nearly flat, so treat as a small scene-dependent gain, not a Balanced default. Daylight Townhall screenshots remained broadly playable, but close-up UI/product/sign texture readability still needs validation before broad recommendation. Aggressive uses a milder limit 1 as a low-VRAM fallback. |
| 2026-06-24 | `globalTextureMipmapLimit` direct sample on Balanced | 15s x2 each | 103.66 at limit 2 | +1.42 vs limit 0 | n/a | `globalTextureMipmapLimit-20260624-231953-Balanced-limit2.png` | Retain as Custom + Aggressive mild VRAM fallback | Modern API validation after replacing deprecated `masterTextureLimit`. Same Townhall setup averaged 102.24 FPS at limit 0, 102.83 FPS at limit 1, and 103.66 FPS at limit 2. Aggressive profile runtime state confirmed `globalTextureMipmapLimit=1`. This supports UI wording that the setting usually saves VRAM more than FPS. |
| 2026-06-24 | `Material.enableInstancing=true` broad scene pass | 15s x3 A/B | 100.73 on Balanced | +0.56 vs instancing off | n/a | `materialInstancing-20260624-233621-Balanced-on.png` | Do not retain as profile default | Debug live-control probe toggled 1,386-1,443 unique materials and 43k material references. Off profile was flat: 86.39 FPS off vs 86.35 on. Balanced was effectively neutral: 100.17 off vs 100.73 on. Initial stats already showed many high-repeat world materials had instancing enabled, and 20k of 34.8k renderers were static-batched. Keep the probe for targeted foliage/prop tests, but do not add this to Conservative/Balanced/Aggressive without a scene-specific repeated win. |
| 2026-06-24 | `renderScale=0.8` | 30 | 136.43 | +0.61 | 1.62x | `fresh-current-plus-renderscale-080-townhall-0400.png` | Reject | Runtime render scale applied after the optimizer frame, but FPS stayed flat. |
| 2026-06-24 | `renderScale=0.65` | 30 | 136.02 | +0.20 | 1.62x | `fresh-current-plus-renderscale-065-townhall-0400.png` | Reject | More visual softness without meaningful FPS gain. |
| 2026-06-24 | `cameraFarClipDistance=400` | 30 | 134.82 | -1.00 | 1.61x | `fresh-current-plus-farclip400-townhall-0400.png` | Reject | No gain. |
| 2026-06-24 | `disableVolumetricLightBeams=true` | 30 | 135.54 | -0.28 | 1.61x | `fresh-current-plus-vlb-off-townhall-0400.png` | Reject | Neutral, as expected from the small VLB trace cost. |
| 2026-06-24 | `lodBias=0.45` | 30 | 147.02 | +11.20 | 1.75x | `fresh-current-lodbias045-townhall-0400.png` | Confirm candidate | Screenshot keeps Townhall, fountain, HUD, and nearby objects visible. |
| 2026-06-24 | `lodBias=0.45` repeat | 30 | 147.12 | +11.30 | 1.75x | `fresh-current-lodbias045-townhall-0400.png` | Confirm candidate | Repeated because it showed a real gain. |
| 2026-06-24 | `lodBias=0.40` | 30 | 148.57 | +12.75 | 1.77x | `fresh-current-lodbias040-townhall-0400.png` | Quality-review candidate | Screenshot is still playable in Townhall, but the gain over `0.45` is only about 1.5 FPS. |
| 2026-06-24 | `lodBias=0.40` + `URPOutlineFeature=false` | 30 | 149.12 | +13.30 | 1.78x | `fresh-lod040-plus-outline-off-townhall-0400.png` | Retain as low-impact/niche | Adds only about 0.5 FPS on top of `lodBias=0.40` in Townhall, but no observed visual regression in screenshots. Needs interaction/readability validation because outlines can affect affordances. |
| 2026-06-24 | `lodBias=0.40` + `layerCullDistance=75` | 30 | 150.15 | +14.33 | 1.79x | `fresh-lod040-layercull75-townhall-0400.png` | Quality-review candidate | Townhall view remains visible, but gain over `0.40` alone is only about 1.6 FPS. Needs broader walking visual review before retaining. |
| 2026-06-24 | `lodBias=0.40` + `layerCullDistance=50` | 30 | 151.77 | +15.95 | 1.81x | `fresh-lod040-layercull50-townhall-0400.png` | Risky candidate | Townhall view remains visible in this one shot, but global layer cull has already caused hidden-building failures in other views. |
| 2026-06-24 | `lodBias=0.40` + `layerCullDistance=35` | screenshot only | n/a | n/a | n/a | `fresh-lod040-layercull35-townhall-0400-boundary.png` | Reject | Visible background/sky composition breaks; do not sample or retain. |
| 2026-06-24 | `setLights enabled=false minDistance=25` | 30 | 141.58 | -7.00 vs `lodBias=0.40` | 1.69x | `fresh-lod040-plus-farlights25-off-townhall-0400.png` | Reject | Regressed FPS and darkens distant street lighting. |
| 2026-06-24 | `useVisibilitySafeRendererCulling=true` on `lodBias=0.45` | 30 | 18.17 | -128.95 vs `lodBias=0.45` | 0.22x | `fresh-lod045-visibilitysafe-townhall-0400.png` | Reject | Severe regression, likely per-frame renderer bookkeeping/re-enable churn. Restored immediately and FPS recovered. |

## Important corrections

- 2026-06-24 profile rebuild: shipped runtime profiles are now `Off`, `Conservative`, `Balanced`, `Aggressive`, and `Custom`. Old exploratory one-off profiles are no longer first-class UI/hotkey profiles.
- `Conservative` is an estimated low-risk profile: lower shadow cost while preserving LOD, camera stacks, and interaction outlines. It needs a standalone sample before any measured FPS claim.
- `Balanced` is the broadly playable measured profile: Townhall 108.98 FPS vs 83.99 FPS baseline, about +24.99 FPS / 1.30x in that scenario.
- `Aggressive` is opt-in: the heavy explicit stack is around 135.5 FPS / 1.61x in Townhall, but broader checks showed sky/large-environment quality degradation. It intentionally avoids unsafe global layer culling.
- `Custom` is for copying a shipped profile and editing individual levers in the F5 menu. Custom choices must be validated from their own screenshots/FPS samples.
- 2026-06-26 candidate sampling after the FPSBoostMod comparison moved profile resolution scaling to URP FSR instead of plain render scale. Townhall Balanced 15s live samples measured baseline 101.73 FPS, `renderScale=0.8` 103.53 FPS, and FSR at 0.8 scale 105.40 FPS; FSR is therefore the profile default path, while plain render scale remains Custom-only fallback.
- The same pass measured `Shader.globalMaximumLOD=400` at 104.47 FPS and anisotropic filtering enabled at 104.80 FPS with acceptable Townhall screenshots, so the three shipped profiles now carry recommended shader/aniso values. `Terrain.drawTreesAndFoliage=false` measured 111.00 FPS but visibly removed trees/grass, so it is Aggressive and Custom only.
- 2026-06-25 validation verdict: the current evidence does not prove a 2x in-game FPS improvement while keeping quality playable. The best comparable safe/default data is below 2x, and aggressive samples that approach 1.8x introduce visible quality risk in broader locations.
- `Stylized Water 2=false` is not a 1.63x optimization by itself. It was measured on top of the current safe stack.
- The confirmed current safe stack is around 1.5x over baseline in the Townhall scenario, not 2x.
- The fresh post-restart current stack sample is 135.82 FPS, around 1.62x over the 83.99 FPS baseline.
- The best repeated candidate so far is `lodBias=0.45`, around 147.1 FPS / 1.75x. `lodBias=0.40` reached 148.57 FPS / 1.77x once, but needs wider visual review before replacing balanced `0.5`.
- `URPOutlineFeature=false` remains a neutral/niche toggle rather than a confirmed major win. `intermediateTextureMode=Auto` is no longer retained because runtime application breaks TAB screen content.
- Global layer cull below 75m is risky. 50m measured fastest in one Townhall shot, but 35m visibly broke the scene and prior profile screenshots showed hidden buildings, so cull reductions need multi-location screenshots before retention.
- Visibility-safe renderer culling is not viable in its current implementation; it caused a severe FPS collapse and should stay disabled.
- Candidate gains below about 3-5 FPS should be treated as noise unless repeated and visually harmless.
- The 2026-06-24 multi-location rerun showed plain `Balanced` is visually safe but much weaker than the earlier heavy Townhall stack: Townhall 108.98 FPS, Motel 130.98 FPS, Docks 114.48 FPS. These are same-process 30 second samples and should not be compared as a 1.5x stack.
- `lodBias=0.45` is not globally safe. It preserved the Townhall view but caused obvious sky/large-environment LOD degradation around Motel. Keep `lodBias=0.5` for a broadly playable Balanced profile unless a location-specific/aggressive option is explicitly accepted.
- The heavy explicit stack with `shadowDistance=0`, `shadowCascades=0`, `lodBias=0.5`, no render-scale reduction, `disableCameraStacks=true`, `intermediateTextureMode=Auto`, and `URPOutlineFeature=false` reached 135.53 FPS at Townhall and 143.02 FPS at Motel, but Motel sky quality was visibly degraded. Nearby buildings/HUD stayed visible.
- `DecalRendererFeature=false` on the heavy Townhall stack repeated at 147.98 FPS then 146.62 FPS with no obvious Townhall screenshot regression. It did not help the safer Balanced stack and should be classified as a heavy/aggressive-only niche probe until multi-location quality is reviewed.
- `Liquid Volume Depth PrePass=false`, `GrabScreenFeature=false` in the rerun, `shadowTransparentReceive=false`, and layer-22 distant renderer disabling did not provide useful repeatable gains. Layer-22 disabling changed 608 renderers and still regressed to 140.53 FPS, so targeted renderer hiding remains rejected.
- `supportsLightCookies=false` did not change through the live URP setter in the rerun (`changed=0`), so do not count it as tested or retained.

## Profiler follow-up

The 2026-06-25 profiler screenshots showed optimizer overhead inside periodic apply work:

- Stable active profiles re-ran the full apply pass on an interval, causing recurring broad scene/global scans even when the selected profile and settings had not changed. `RenderOptimizerService` now reapplies only when the effective settings signature changes or the active scene changes, so profile edits and scene loads still take effect without a steady timer hitch.

- `ApplyUrpRendererData` called `Resources.FindObjectsOfTypeAll<UniversalRendererData>()`.
- `SetRendererFeatureActive` also called `Resources.FindObjectsOfTypeAll<UniversalRendererData>()`.

The dotTrace report view for `profile-this.dtp` confirmed the same issue in a 37.952s capture:

- `ApplyUrpRendererData`: 1.209s total / 1.207s own + system under `BarsGraphics.Services.ApplyUrpRendererData(...)`.
- `SetRendererFeatureActive`: 1.103s own + system under `BarsGraphics.Services.SetRendererFeatureActive(string,bool)`.
- The `ApplyUrpRendererData` call tree resolves through `FindObjectsOfTypeAll` -> `UnityEngine.FindObjectsOfTypeAll(...)`.

This repeated a global asset search during active profiles. `RenderOptimizerService` caches URP renderer data assets per active render-pipeline asset for renderer-feature changes. The intermediate-texture apply path was later removed entirely after the TAB screen regression. Rerun the same baseline/profile samples before updating the 2x verdict.

## Base-game hotspot probe backlog

dotTrace also showed base-game/user-code hotspots that may be worth sampling after the renderer-data cache fix. These are candidate probes, not retained optimizations:

| Hotspot | Time in `profile-this.dtp` | Local evidence | Candidate probe | Risk |
| --- | ---: | --- | --- | --- |
| `UnityEngine.Rendering.Universal.RenderSingleCamera(...)` | 8.645s total / 5.202s own+system | Main render cost remains dominant after mod-owned overhead. | Continue render-first probes: camera stack, renderer features, decals, shadow settings, additional lights, URP intermediate texture, depth/opaque/HDR toggles. | Visual quality and interaction readability. |
| `ScheduleOne.AvatarFramework.Animation.PopulateBoneTransforms(...)` | 1.433s own+system | `AvatarAnimation.LateUpdate()` repopulates bone transform snapshots every frame; avatar animation already has distance/frustum culling. | Sample more aggressive avatar animation culling distances or skip bone snapshotting for culled/distant avatars only. | Ragdoll recovery, stand-up animation, seated/skateboard transitions, impostors. |
| `ScheduleOne.PlayerScripts.GetSurfaceAngle()` | 812ms own+system | Called from `PlayerMovement.Move()` through a downward physics raycast when moving. | Low-priority sample: cache surface angle for a short interval while player movement input/ground state is stable. | Player physics feel, slope handling, movement correctness. |
| `ScheduleOne.Interaction.CheckHover()` | 403ms total / 402ms own+system | `InteractionManager.LateUpdate()` runs sphere-cast-all + raycast-all, sorting, list/dictionary allocation, and component lookups. | Strong non-render sample: throttle hover checks to 20-30 Hz, or only when camera/player moved beyond a tiny threshold. | Interaction prompt latency and object highlighting responsiveness. |
| `ScheduleOne.Weather.UpdateWeatherEntities()` | 776ms total / 371ms own+system | Iterates registered weather entities, checks cover, and resolves weather profile by position each `EnvironmentManager.Update()`. | Strong world-system sample: update weather entities at 5-10 Hz or round-robin entities per frame. | Wetness/umbrella/weather-volume changes lag slightly. |
| `ScheduleOne.Weather.GetWeatherProfileFromPosition(...)` | 204ms total / 195ms own+system | Called per weather entity; transforms positions through active weather volumes. | Cache last weather profile per entity until entity/anchor moves meaningfully or volume changes. | Incorrect weather state at volume boundaries. |
| `ScheduleOne.AvatarFramework.LookAt(...)` | 309ms own+system | Avatar look targets update visibly but are cosmetic outside conversations/nearby NPCs. | Sample distance-gated or frustum-gated look-at updates for non-nearby avatars. | NPC/player gaze quality, dialogue presentation. |
| `ScheduleOne.NPCs.LateUpdate()` | 286ms total / 266ms own+system | Broad NPC late-update cost; NPC visibility already gates some behavior. | Sample distance/frustum gating only for cosmetic subcomponents, not whole NPC logic. | AI, relationship, awareness, visibility/network correctness. |
| `UnityEngine.InputSystem.UI.PerformRaycast(...)` | 159ms own+system | UI raycast cost appears under player movement path in the report view. | Sample only if UI state shows unnecessary raycasters/canvases active in gameplay. | UI click/hover behavior. |
| `ScheduleOne.Vehicles.AI.UpdateKinematic(float)` | 224ms total / 148ms own+system | Vehicle agent uses fixed 0.033s infrequent updates and kinematic path sampling/raycasts. | Sample distance-based lower update cadence for non-nearby autopiloted vehicles. | Traffic behavior, collision avoidance, pursuit/mission vehicles. |
| `VolumetricFogAndMist2.UpdateNoise()` | 127ms own+system | Small compared with render and update hotspots; existing volumetric-light-beam probe was neutral. | Low-priority visual probe: disable or slow fog noise updates if accessible. | Weather/atmosphere quality. |

Recommended next sample order:

1. Rerun `Off`, `Balanced`, and aggressive Townhall samples after the renderer-data cache fix, because it removes about 2.3s of mod-owned overhead from the profiled capture.
2. Add an experimental live-control/Harmony probe for `InteractionManager.CheckHover` throttling. Start at 30 Hz, then 20 Hz if interaction prompts still feel responsive.
3. Add an experimental weather-entity update throttle around `EnvironmentManager.UpdateWeatherEntities`, starting at 10 Hz. Check rain/wetness/umbrella/weather-volume transitions.
4. Only then test avatar/NPC/vehicle update throttles, and keep them distance/frustum gated rather than globally disabled.

## Multi-location confirmation

Added but not yet run:

```powershell
BarsGraphics\tools\confirm-visual-profiles.ps1 -Locations townhall,motel,docks,suburbia,uptown -Profiles Off,Balanced,Aggressive -SampleSeconds 10
```

The confirmation loop teleports each location, captures screenshots for each profile, optionally samples native `showfps`, and writes CSV/JSON artifacts under `BarsGraphics\artifacts`. Review screenshots for nearby object visibility, readable/interactable affordances, and acceptable distant LOD before treating a profile as retained.

## UI-facing optimization catalog

`BarsGraphics.Models.OptimizationImpactCatalog` is the structured source for impact/risk data during local validation. It is development-only and is not compiled into stable production assemblies. The live-control endpoint is:

```json
{"method":"optimizationImpacts"}
```

Each item includes:

- `measuredFpsDelta`: measured FPS gained or lost in the stated context.
- `measuredTotalFps`: total FPS observed for that sample.
- `measurementContext`: scenario, sample length, and comparison baseline/stack.
- `confidence`: `Unknown`, `SingleSample`, `Repeated`, or `Rejected`.
- `visualRisk`: `None`, `Low`, `Medium`, or `High`.
- `recommended`: whether the UI should present it as a reasonable user-facing toggle.
- `qualityNotes`: concise visual-quality risk for tooltips.

Update the catalog whenever a 30 second sample or screenshot validation changes a toggle's status. Prefer conservative wording: repeated gains and low visual risk can be highlighted; high-risk or rejected toggles should stay out of stable UI until they are validated as shippable controls.

## Research notes

Sources consulted:

- Unity Manual, URP performance configuration.
- Unity Manual, graphics performance.
- Unity Manual, texture compression by platform.
- Unity mobile performance guidance.

Runtime-mod-friendly levers:

- URP render scale: high leverage, but visible softness. Best used as a profile option or dynamic fallback rather than default if nearby quality matters.
- Shadow distance, cascade count, shadow resolution, soft shadows: high leverage and already part of the current stack.
- Additional lights mode/count/shadows: high leverage and already part of the current stack.
- Camera count and camera stacks: Unity calls out camera minimization as important, but disabling camera stacks can hide the player's phone UI and must remain a manual diagnostic only.
- Renderer features/render passes: Unity calls out unnecessary renderer features and decals as extra passes. We should continue testing feature toggles, but only retain meaningful measured wins.
- Depth texture, opaque texture, HDR, MSAA, intermediate texture, depth priming, native render pass: likely high-value URP asset/renderer settings if accessible through IL2CPP reflection. These are better next probes than more broad object hiding.
- LOD and layer cull distances: valid, but must preserve near gameplay geometry. Current `lodBias=0.5` is acceptable; aggressive LOD is rejected.

Mostly build/import-time levers:

- Texture compression and texture max size can reduce memory bandwidth and load time, but Unity applies this through asset import/build settings. A runtime mod cannot cheaply recompress all game textures in-place without replacing assets or building alternative bundles.
- Mesh compression is also mostly import-time and reduces disk/memory footprint more than runtime vertex cost. Runtime mesh decimation/replacement is possible, but high risk and likely only worth it after `meshStats` identifies a very expensive visible mesh and the render trace confirms geometry cost.
- Shader variant stripping, SRP Batcher compatibility, material batching, atlasing, and baked/static-lighting changes are project/build pipeline work, not easy runtime mod changes.

Next probe priorities:

1. URP asset/renderer booleans: depth texture, opaque texture, HDR, MSAA, soft shadows, intermediate texture, depth priming, native render pass where accessible.
2. Decal renderer feature, outline feature, and water features only if repeated 30 second samples show a real gain and screenshots remain playable.
3. Base-game update throttles for non-render hotspots only when the skipped frame can safely keep the previous result. These probes are development-only and are not compiled into stable production assemblies. Implemented experimental profiling defaults:
   - `EnableInteractionHoverThrottle=true`, `InteractionHoverThrottleHz=30`
   - `EnableWeatherEntityThrottle=true`, `WeatherEntityThrottleHz=10`
   These are intended for dotTrace validation, with interaction prompts and weather transitions checked manually in-game before treating them as shippable defaults.
4. Texture/mesh replacement only after collecting evidence from `meshStats`, renderer/material stats, and trace/frame-debug-like pass evidence.

## ILSpy URP evidence

Local-only inspection was performed with `ilspycmd` against both:

- `D:\SteamLibrary\steamapps\common\Schedule I_public\MelonLoader\Il2CppAssemblies\Unity.RenderPipelines.Universal.Runtime.dll`
- `D:\SteamLibrary\steamapps\common\Schedule I_alternate\Schedule I_Data\Managed\Unity.RenderPipelines.Universal.Runtime.dll`

Relevant `UniversalRenderPipelineAsset` runtime members exist in both runtimes:

- `supportsCameraDepthTexture` backed by `m_RequireDepthTexture`
- `supportsCameraOpaqueTexture` backed by `m_RequireOpaqueTexture`
- `supportsHDR` backed by `m_SupportsHDR`
- `msaaSampleCount` backed by `m_MSAA`
- `supportsSoftShadows` backed by `m_SoftShadowsSupported`
- `useSRPBatcher` backed by `m_UseSRPBatcher`
- `supportsDynamicBatching` backed by `m_SupportsDynamicBatching`
- `supportsLightCookies` backed by `m_SupportsLightCookies`

Relevant `UniversalRendererData` runtime members exist in both runtimes:

- `depthPrimingMode` backed by `m_DepthPrimingMode`
- `copyDepthMode` backed by `m_CopyDepthMode`
- `intermediateTextureMode` backed by `m_IntermediateTextureMode`
- `m_ShadowTransparentReceive`
- `accurateGbufferNormals` backed by `m_AccurateGbufferNormals`

Live `urpStats` in the Townhall scene reported:

- URP asset: depth texture on, opaque texture on, HDR on, MSAA sample count 1, soft shadows off, SRP batcher on, dynamic batching on, light cookies on.
- Renderer data assets: `VolumetricFogDepthRenderer`, `SkyStudio-WeatherDepthForwardRenderer`, and `UniversalRenderPipelineAsset_Renderer`, all with `depthPrimingMode=Disabled`, `copyDepthMode=AfterOpaques`, `intermediateTextureMode=Always`.


