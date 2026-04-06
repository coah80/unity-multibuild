# Multi Build

`Multi Build` is a Unity editor package for building several targets in one pass from a single window.

It keeps its settings in the host project's `ProjectSettings` folder, so the package stays installable and reusable across projects.

## Install

In Unity:

1. Open `Window > Package Manager`
2. Click `+`
3. Choose `Add package from git URL...`
4. Paste this repo URL

## Use

1. Open `Tools > Multi Build`
2. Pick an output root
3. Toggle the targets you want
4. Click `Build Selected`

The package will switch targets for you and build them one after another.

## Targets

- Windows 64-bit
- macOS
- Linux 64-bit
- Linux Server
- WebGL
- Android
- iOS

Only targets installed in the current Unity editor show up as available.
