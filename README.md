# MM6 VR

A Unity 6 / Quest 2 viewer for exploring Might and Magic VI maps in VR.

<video src="media/demo.mp4" controls width="100%"></video>

[Open the demo video](media/demo.mp4)

> This repository does not include Might and Magic VI game assets. You need a valid local MM6 installation to generate maps, textures, sprites, and the APK.

## What It Does

- Extracts MM6 outdoor and indoor map data from your local installation.
- Converts textures, sprites, terrain, buildings, monsters, villagers, and decorations into Unity assets.
- Builds a Quest 2 VR viewer with teleport, left-stick movement, snap turn, and jump.
- Directly opens New Sorpigal for the current Quest test flow.

## Demo Video

The demo file is tracked in this repo:

```text
media/demo.mp4
```

If GitHub does not render the video inline, open it from the link above.

## Requirements

- Unity `6000.4.0f1`
- Unity Android Build Support with SDK, NDK, and OpenJDK
- Python 3
- Meta Quest 2 in Developer Mode
- A valid local Might and Magic VI installation

Expected game-data layout:

```text
originaldata/
  Might and Magic 6/
    data/
      Games.lod
      BITMAPS.LOD
      SPRITES.LOD
      icons.lod
      new.lod
    Sounds/
      2.mp3
      ...
```

## Build New Sorpigal

Building New Sorpigal, any other outdoor map, indoor dungeons, or the final Quest APK requires the original MM6 game data from your own installation. The scripts read the `.LOD` archives locally and generate Unity-ready assets on your machine; those generated assets are not included in this repository.

Run from the repo root:

```bash
python3 tools/build_mm6_new_sorpigal_package.py --install-dir "originaldata/Might and Magic 6"
```

For more maps, use the other build scripts in `tools/` after placing the same original game data under `originaldata/Might and Magic 6`, for example:

```bash
python3 tools/build_mm6_outdoor_world_packages.py --install-dir "originaldata/Might and Magic 6"
python3 tools/build_mm6_indoor_test_packages.py --install-dir "originaldata/Might and Magic 6"
```

Open the Unity project:

```text
unity/MM6OutdoorImporter
```

In Unity, run:

```text
Tools/MM6/Configure Quest 2 Project Settings
Tools/MM6/Prepare New Sorpigal Quest Test
Tools/MM6/Build Quest 2 APK
```

The APK is written locally to:

```text
unity/MM6OutdoorImporter/Builds/Quest2/MM6Viewer-Quest2.apk
```

## Install On Quest 2

The APK is not committed to this public repo. Build it locally first from your own MM6 installation:

```text
unity/MM6OutdoorImporter/Builds/Quest2/MM6Viewer-Quest2.apk
```

Then install it with `adb`:

1. Enable Developer Mode for the headset in the Meta Horizon mobile app.
2. Connect the Quest 2 over USB.
3. Put on the headset and accept the USB debugging prompt.
4. Verify the device is visible:

```bash
adb devices
```

5. Install or update the app:

```bash
adb install -r unity/MM6OutdoorImporter/Builds/Quest2/MM6Viewer-Quest2.apk
```

If `adb` says `device unauthorized`, keep the headset awake, disconnect and reconnect USB if needed, then accept the USB debugging dialog inside the headset before retrying.

If the APK exists at the absolute local path used during development, this is equivalent:

```bash
adb install -r /Users/kalinbas/Downloads/mm6hd/unity/MM6OutdoorImporter/Builds/Quest2/MM6Viewer-Quest2.apk
```

## Controls

- Left stick: move
- Left secondary button: jump
- Left primary button: toggle direct movement
- Right stick: snap turn
- Right trigger: teleport

## Project Layout

- `tools/`: Python extraction and packaging scripts.
- `unity/MM6OutdoorImporter/`: Unity project.
- `Scripts/`: optional MMExtension helper script.
- `media/demo.mp4`: demo video.

Ignored local/generated content includes `originaldata/`, `generated/`, Unity `Library/`, generated imported maps, generated packages, APKs, logs, and extracted MM6 assets.
