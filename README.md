# Paperwalls for Windows

Browse and set wallpapers from [Paper](https://burkeholland.github.io/paper/). WinUI 3 + C#, packaged as MSIX with [winapp](https://github.com/microsoft/WinAppCli).

![Paperwalls](docs/screenshot.png)

## Run

```powershell
winget install Microsoft.WinAppCli
winapp run .            # build, register with identity, launch
```

## Package

```powershell
winapp cert generate                                   # once, dev only
winapp package .\Paperwalls.csproj --cert devcert.pfx --arch arm64 --arch x64
winapp package .\Paperwalls.csproj --no-sign --arch arm64 --arch x64   # Store upload
```

Installing the dev-signed bundle needs `winapp cert install devcert.pfx` (admin) first.

## Tray and startup

Closing the window keeps Paperwalls running in the system tray, including automatic rotation. The tray menu has **Next wallpaper** (using the rotation scope), **Open**, and **Quit**. Launching the app again opens the existing window instead of starting another rotation timer.

**Settings > Launch at login** is optional and off by default. Windows can disable it under **Settings > Apps > Startup**. Login launches start in the tray. Unlike the Mac menu-bar app, theme and favorite browsing stay in the Windows gallery.

## Microsoft Store checklist

Reserve the app name in Partner Center, then copy its package identity values into `Package.appxmanifest`:

| Manifest field | Value |
|---|---|
| `Identity/@Name` | Partner Center package identity name |
| `Identity/@Publisher` | Partner Center publisher ID, including `CN=` |
| `Properties/PublisherDisplayName` | Partner Center publisher display name |
| `Properties/DisplayName` and `Application/uap:VisualElements/@DisplayName` | Reserved display name |
| `desktop:StartupTask/@DisplayName` | Same display name |
| `Identity/@Version` | Increase for each submission; keep the fourth component `0` for Store submissions |

Identity and publisher are deliberately still development placeholders. For upload, build the unsigned architecture bundle:

```powershell
winapp package .\Paperwalls.csproj --no-sign --arch arm64 --arch x64
```

Submit the resulting `.msixbundle` in Partner Center. Complete the listing, screenshots, age rating, and the justification for `runFullTrust` (the desktop app uses Windows APIs to change wallpaper and provide a tray menu). Run the Windows App Certification Kit before submission. An x64 package built on ARM64 still needs an x64-machine smoke test.

Use the public [privacy policy](https://github.com/burkeholland/paperwalls-windows/blob/main/PRIVACY.md) as the Store privacy URL once this change is merged.

## Visual assets

The icon is shared with [Paperwalls for Mac](https://github.com/burkeholland/paperwalls-mac/blob/main/assets/app-icon.png). `Assets\AppIconSource.png` is the source; run `.\scripts\Generate-Assets.ps1` to regenerate the multi-size Windows icon, taskbar icons, tiles, and splash images.

## Notes

- Catalog: `https://burkeholland.github.io/paper/api.json`, cached for offline use.
- Sets the wallpaper on every monitor through `IDesktopWallpaper` (Fill).
- Data lives in the package's LocalState: catalog, favorites, rotation settings and a 750 MB image cache.
- WindowsAppSDK is pinned to 2.6.5 because the internal NuGet feed lacks 2.7's AI package.
