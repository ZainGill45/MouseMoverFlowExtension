# AGENTS.md — Mouse Mover (Flow Launcher plugin)

Context and conventions for working on this repo. Read this before changing build
config, the settings panel, or anything publishing-related — several non-obvious
constraints below were learned the hard way.

## What this is

A Flow Launcher plugin that toggles automatic, randomized mouse movement
(anti-idle). Type the action keyword `mouse` → **Enable** / **Disable**. All
tuning is in the standard plugin settings panel, not the search box.

- **Language:** C# (`net9.0-windows`, WPF)
- **Action keyword:** `mouse`
- **Plugin ID (GUID):** `fd5e5017-66f2-4d75-8911-f387ce788efd` — **never change it**;
  it must stay identical in `plugin.json` and the store manifest.
- **GitHub repo:** `ZainGill45/MouseMoverFlowExtension` — note the casing
  (`ZainGill45`, not the lowercase email prefix). jsdelivr/release URLs are
  case-sensitive.

## How it works (architecture)

Flow Launcher C# plugins are compiled DLLs **loaded into the launcher process and
kept resident**. That is the whole design:

- **Enable** = start a `System.Threading.Timer`; **Disable** = stop/dispose it.
  Movement continues after the launcher window closes because the FL process stays
  alive. There is **no separate background process** and no PID tracking.
- Each tick: read cursor (`GetCursorPos`), pick a uniform-random target inside a
  disc of `Radius` px, clamp it to the full virtual desktop (multi-monitor via
  `GetSystemMetrics` `SM_*VIRTUALSCREEN`), then glide there with ease-in/out over
  `GlideMs` in `StepMs` steps (`SetCursorPos`). `GlideMs == 0` => instant jump.
- An `Interlocked` guard (`_gliding`) skips a tick if the previous glide is still
  running; a running glide bails immediately when disabled.
- `Main` implements `IDisposable` (`Dispose` stops/disposes the timer) so a plugin
  reload can't orphan a running timer that would keep moving the cursor.

## File map

| File | Role |
|------|------|
| `plugin.json` | FL manifest. `Version` drives release tag + store update check. |
| `Main.cs` | `IPlugin` + `ISettingProvider`. Query (toggle only), engine, Win32 P/Invoke. |
| `Settings.cs` | `MouseMoverSettings`: `INotifyPropertyChanged`, **clamping setters**, bounds consts. |
| `SettingsControl.xaml(.cs)` | WPF settings panel returned by `CreateSettingPanel()`. |
| `Images/icon.png` | Plugin icon (also served via jsdelivr for the store). |
| `build.ps1` | Local build + deploy to the FL Plugins folder. |
| `.github/workflows/Publish Release.yml` | CI: build on Windows, zip, GitHub release. |
| `publish/MouseMover-<guid>.json` | Store manifest entry for the one-time PluginsManifest PR. |
| `PUBLISHING.md` | Full release + store-submission process. |

## Hard constraints (don't break these)

1. **Target `net9.0-windows`.** The installed Flow Launcher (2.1.x) runs on .NET 9
   (WindowsDesktop). A plugin built for net10+ won't load into the net9 host.
   Re-check the host TFM in
   `%LOCALAPPDATA%\FlowLauncher\app-*\Flow.Launcher.runtimeconfig.json` if FL updates.
2. **SDK via NuGet `Flow.Launcher.Plugin` with `<ExcludeAssets>runtime</ExcludeAssets>`.**
   The host provides `Flow.Launcher.Plugin.dll` at runtime; compile against it but do
   **not** copy it into the plugin output. Verify the build output never contains
   `Flow.Launcher.Plugin.dll` or `Presentation*`/`WindowsBase`.
3. **Settings-panel theming.** FL's settings window uses **iNKORE.UI.WPF.Modern**,
   which themes the *standard* WPF control types (e.g. `TextBox`) via app-wide
   **implicit** styles. Applying *any* full `Style` to an input — implicit, or keyed
   without `BasedOn` the ambient style — **detaches it from the theme** and it renders
   as a plain unstyled box. So: style inputs with **inline attributes only**
   (Width/Margin/alignment); let them inherit the host template, colors, and accent.
   **Do not** add the iNKORE package to use `ui:` controls (e.g. `NumberBox`): bundling
   that assembly gives the control a different type identity from the host's copy across
   load contexts, and the host's implicit style won't apply. TextBlocks inherit
   foreground/font from the parent — leave them styleless too.
4. **Settings persistence** goes through `context.API.LoadSettingJsonStorage<T>()` /
   `SaveSettingJsonStorage<T>()`. The panel saves on each input `LostFocus`. Validation
   lives in the property setters (`Math.Clamp`), so binding writes are always in-range;
   `INotifyPropertyChanged` makes the UI reflect any clamping.

## Build, deploy, test (local)

Windows + .NET SDK 9 or newer (WPF only builds on Windows).

```powershell
pwsh ./build.ps1            # build -> bin/plugin, copy to %APPDATA%\FlowLauncher\Plugins\MouseMover-1.0.0
pwsh ./build.ps1 -NoDeploy  # build only
```

- **The plugin DLL is locked while Flow Launcher is running**, so a redeploy can't
  overwrite it. Stop FL (`Get-Process Flow.Launcher | Stop-Process -Force`), copy, then
  relaunch `%LOCALAPPDATA%\FlowLauncher\app-2.1.2\Flow.Launcher.exe`. Restarting FL is
  also how new code/settings-panel changes get loaded.
- There are **no automated tests**; verify by `dotnet build` (compile) + manual check in
  Flow Launcher (toggle behaviour and the themed settings panel).

## Publishing (summary; see PUBLISHING.md)

- Bump `Version` in `plugin.json`, commit, push to `main`. The **Publish Release**
  workflow (must run on `windows-latest` — WPF) builds, zips the output with
  `plugin.json` at the zip root as `MouseMover.zip`, and creates/updates a `v<version>`
  GitHub release.
- First listing only: PR the `publish/MouseMover-<guid>.json` file into a fork of
  `Flow-Launcher/Flow.Launcher.PluginsManifest` (branch `plugin_api_v2`, `plugins/`
  folder). After approval, the manifest CI auto-updates the store from new releases —
  no further submissions.
- Manifest `IcoPath` uses jsdelivr `@main`, which caches hard. If the icon changes,
  reference a tagged ref (`@v1.0.x`) instead.

## Conventions

- Keep the search-box surface minimal: the `Query` returns only the Enable/Disable
  toggle. New tunables belong in the **settings panel**, not as query subcommands.
- Mirror the existing comment density and naming; engine helpers stay private in
  `Main.cs`, configuration in `Settings.cs`.
- **No `var`** — declare locals with explicit types. **No abbreviations** in names;
  use full descriptive identifiers (e.g. `intervalMilliseconds`, `virtualScreenWidth`,
  not `period`/`vw`). Win32 interop names (`POINT.X`, `SM_*`, P/Invoke signatures)
  keep their conventional spellings.
- Don't commit `.claude/settings.local.json` (personal, gitignored); the shared
  `.claude/settings.json` allowlist is committed.
