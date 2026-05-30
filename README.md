# Mouse Mover — Flow Launcher plugin

A Flow Launcher plugin that toggles automatic, randomized **mouse movement**.
Enable it and the cursor smoothly glides to random nearby points on its own
(useful as an anti-idle / keep-awake utility); disable it and you get full
control back instantly.

Because Flow Launcher C# plugins load **into the launcher process and stay
resident**, "enable" simply starts a background timer that keeps moving the
cursor after the launcher window closes. No separate process, no runtime
dependencies beyond .NET (which Flow Launcher already ships).

## Usage

The action keyword is `mouse`.

| Type | What it does |
|------|--------------|
| `mouse` → **Enable mouse mover** | Starts automatic movement |
| `mouse` → **Disable mouse mover** | Stops it and returns control |

All configuration lives in **Settings → Plugins → Mouse Mover** (the standard
Flow Launcher settings panel), not in the search box:

| Setting | Meaning | Default | Range |
|---------|---------|---------|-------|
| Interval | ms between new random targets | 5000 | 250 – 3,600,000 |
| Radius | px max distance of a target from the cursor | 250 | 5 – 4000 |
| Glide | ms glide duration (`0` = instant jump) | 600 | 0 – 10,000 |
| Step | ms per glide step (lower = smoother) | 10 | 1 – 200 |

Settings persist across restarts, save automatically as you edit, are
range-clamped (a typo can't strand the cursor), and apply to a running mover on
its next move. Targets are clamped to the full virtual desktop, so it behaves on
multi-monitor setups; movement is uniform within a disc of the configured radius
with ease-in/ease-out for a natural feel.

## Build & deploy (local)

Requires the .NET SDK (9 or newer) on **Windows** (the WPF settings panel only
builds on Windows). The Flow Launcher SDK is pulled from NuGet
(`Flow.Launcher.Plugin`), so a normal restore is all that's needed.

```powershell
pwsh ./build.ps1          # build + copy into %APPDATA%\FlowLauncher\Plugins
pwsh ./build.ps1 -NoDeploy
```

After deploying, **restart Flow Launcher** (or run its *Restart* command) to load
the changes. (The plugin DLL is locked while Flow Launcher is running, so a
rebuild can't overwrite it until the launcher restarts.)

## Publishing

To release and list this plugin in the Flow Launcher store, see
[PUBLISHING.md](PUBLISHING.md). In short: bump `Version` in `plugin.json`, push to
`main`, and the [Publish Release](.github/workflows/Publish%20Release.yml) workflow
builds and tags a GitHub release; a one-time PR to the plugins manifest repo lists
it in the store, after which updates are picked up automatically.

## Project layout

```
plugin.json            Flow Launcher plugin manifest
MouseMover.csproj       net9.0-windows WPF class library
Main.cs                 Plugin entry point + movement engine (Win32 SetCursorPos)
Settings.cs             Persisted, self-clamping settings model
SettingsControl.xaml    WPF settings panel (ISettingProvider)
Images/icon.png         Plugin icon
build.ps1               Build + deploy helper
.github/workflows/      Publish Release workflow
publish/                Plugin-store manifest entry (for the one-time PR)
```
