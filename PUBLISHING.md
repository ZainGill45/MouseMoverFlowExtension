# Publishing Mouse Mover to the Flow Launcher Plugin Store

This follows the official process from the
[Flow.Launcher.PluginsManifest](https://github.com/Flow-Launcher/Flow.Launcher.PluginsManifest)
repository.

## One-time setup

1. **Create the GitHub repo** `ZainGill45/MouseMoverFlowExtension` (public) and push
   this project to the `main` branch (see [README](README.md) → *Build & deploy*).
   The repo must be **public** so jsdelivr can serve the icon and users can download
   releases.

## Each release

1. **Bump the version** in [`plugin.json`](plugin.json) (e.g. `1.0.0` → `1.0.1`).
   This single field drives the release tag and the store's update check.
2. **Commit & push to `main`.** The
   [`Publish Release`](.github/workflows/Publish%20Release.yml) GitHub Actions workflow
   then:
   - builds the plugin on a Windows runner (WPF needs Windows),
   - zips the build output to `MouseMover.zip` (with `plugin.json` at the zip root),
   - creates/updates a GitHub Release tagged `v<version>` with that zip attached.

   You can also trigger it manually from the **Actions** tab (*workflow_dispatch*).

## First-time store listing (do this once)

After your **v1.0.0** release exists:

1. Fork [`Flow.Launcher.PluginsManifest`](https://github.com/Flow-Launcher/Flow.Launcher.PluginsManifest)
   and switch to its default branch (`main`).
2. Copy [`publish/Mouse Mover-fd5e5017-66f2-4d75-8911-f387ce788efd.json`](publish/Mouse%20Mover-fd5e5017-66f2-4d75-8911-f387ce788efd.json)
   into that repo's `plugins/` directory (keep the `${Name}-${uuid}.json` filename —
   the manifest CI's `test_file_name_construct` requires it to match `Name` exactly,
   including the space in "Mouse Mover").
3. Open a pull request. Once the Flow Launcher team approves it, the plugin appears
   in the in-app store and via `pm install Mouse Mover`.

After the listing is approved, **you never submit again**: the manifest CI checks your
repo's releases every ~3 hours and auto-updates the store entry whenever you publish a
new `v<version>` release. Just keep bumping `plugin.json` and pushing to `main`.

## Before it's listed

You and your testers can install straight from the release without waiting for the store:

```
pm install https://github.com/ZainGill45/MouseMoverFlowExtension/releases/download/v1.0.0/MouseMover.zip
```

## Notes / standards checklist

- `ID` in `plugin.json` and the manifest file **must match** and never change.
- `Version` must match the release tag (`v<version>`); the workflow guarantees this by
  reading `plugin.json`.
- `IcoPath` in the manifest is a **CDN URL** (jsdelivr → your repo), not a local path.
- The plugin must comply with the
  [Plugin Store policy](https://github.com/Flow-Launcher/Flow.Launcher.PluginsManifest#plugin-store-policy)
  (no malicious/deceptive use, etc.).
