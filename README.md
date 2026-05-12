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

Run from the repo root:

```bash
python3 tools/build_mm6_new_sorpigal_package.py --install-dir "originaldata/Might and Magic 6"
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

Connect the headset over USB, accept the authorization prompt, then run:

```bash
adb install -r unity/MM6OutdoorImporter/Builds/Quest2/MM6Viewer-Quest2.apk
```

If `adb` says `device unauthorized`, keep the headset awake and accept the USB debugging dialog inside the headset.

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
