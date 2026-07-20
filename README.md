# Bars Graphics

Bars Graphics is a MelonLoader mod for Schedule I that adds graphics presets, optional visual styles, and an in-game tuning menu.

The goal is simple: make the game easier to run without turning every setting into a blind guess. The built-in profiles cover the usual cases, and the Custom profile exposes the same knobs with short notes about what each one can cost visually.

## What it changes

- Conservative, Balanced, Aggressive, and Custom graphics profiles.
- Optional Natural, Vibrant, Warm Film, and Cool Film color styles. They are off by default and independent from the optimizer profile.
- FSR upscaling support, with plain render scale left as a Custom-only fallback.
- Shadow distance, shadow cascades, LOD bias, shader LOD, texture mip limits, anisotropic filtering, extra lights, camera stacks, terrain foliage, culling, post processing, and frame pacing options.
- A bGUI/uGUI menu that works on Mono and Il2Cpp.
- Staged UI changes: adjust a few settings, then press Apply when you are ready. This avoids the game hitching every time a slider moves.

The visual styles only adjust URP color grading and white balance. They do not add a custom shader, renderer feature, LUT asset, bloom pyramid, or extra full-screen copy. Optimizer settings that disable post processing take priority.

## Install

Release zips include both required files:

- `Mods/BarsGraphics.dll`
- `UserLibs/bGUI.dll`

Install with Vortex/FOMOD, or copy those folders into the Schedule I game folder manually.

Do not put `bGUI.dll` in `Mods`. It belongs in `UserLibs`.

## Controls

- `F5`: open or close the Bars Graphics menu.
- `F6`: toggle the optimizer on or off.

Menu edits are staged. Use Apply to save and apply them, or Revert to throw away pending changes.

## Runtime support

- Il2Cpp builds target the public Schedule I branch.
- Mono builds target the alternate Schedule I branch.
- The Nexus package is intended for the public Il2Cpp branch unless a separate Mono file is provided.

## Build notes

Build bGUI for the matching runtime first:

```powershell
dotnet build ..\bGUI\bGUI.csproj -c Il2cppRelease -p:AutomateLocalDeployment=false
dotnet build ..\bGUI\bGUI.csproj -c MonoRelease -p:AutomateLocalDeployment=false
```

Then build Bars Graphics:

```powershell
dotnet build BarsGraphics.csproj -c Il2cppStable -p:AutomateLocalDeployment=false
dotnet build BarsGraphics.csproj -c MonoStable -p:AutomateLocalDeployment=false
```

Stable builds are the release builds. Development builds include local profiling and screenshot automation and should not be uploaded.

## Automated releases

Pushing a `vMAJOR.MINOR.PATCH` tag publishes the verified FOMOD archive to GitHub Releases and, when configured, Nexus Mods. The workflow builds the matching Mono and Il2Cpp bGUI dependencies from source and packages the Il2Cpp release DLL with the bGUI MIT license.

Repository configuration required before the first automated release:

- `NEXUSMODS_FILE_GROUP_ID` repository variable: `7583798`.
- `NEXUSMODS_API_KEY` repository secret.
- `GAME_ASSEMBLIES_REPO` and `GAME_ASSEMBLIES_TOKEN` repository secrets for Mono build inputs.
- `IL2CPP_ASSEMBLIES_REPO` and `IL2CPP_ASSEMBLIES_TOKEN` repository secrets for Il2Cpp build inputs.

Use **Run workflow** to republish an existing tag or to build a release without uploading it to Nexus Mods.

## Included dependency

This package includes `bGUI.dll` in `UserLibs`. bGUI is MIT licensed; include the bGUI MIT license text when redistributing the DLL.

## Notes

See [PERF_NOTES.md](PERF_NOTES.md) for measurement notes and validation details.
