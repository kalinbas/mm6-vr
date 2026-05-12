#!/usr/bin/env python3

from __future__ import annotations

import argparse
import shutil
import sys
from pathlib import Path

from build_mm6_outdoor_world_packages import ensure_cache, run_python
from mm6_install_data import decode_lod_maybe_compressed, list_lod_entries, load_install, read_lod_entry


MAP_FILE_NAME = "oute3.odm"
MAP_KEY = "oute3"
MAP_DISPLAY_NAME = "New Sorpigal"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Extract, export, and bundle only the original MM6 New Sorpigal outdoor map for Unity testing."
    )
    parser.add_argument(
        "--install-dir",
        type=Path,
        help="Path to the original MM6 install directory. Defaults to auto-detection.",
    )
    parser.add_argument(
        "--generated-root",
        type=Path,
        default=Path("generated/mm6-install"),
        help="Generated cache root used for the extracted map and exported JSON.",
    )
    parser.add_argument(
        "--packages-root",
        type=Path,
        default=Path("unity/MM6OutdoorImporter/MM6SinglePackages"),
        help="Destination folder for the bundled New Sorpigal Unity package.",
    )
    return parser.parse_args()


def extract_new_sorpigal_map(install_dir: Path, generated_root: Path) -> Path:
    install = load_install(install_dir)
    if install is None:
        raise FileNotFoundError("Could not resolve a complete MM6 install.")

    games_entries = list_lod_entries(install.games_lod)
    entry = games_entries.get(MAP_FILE_NAME)
    if entry is None:
        raise FileNotFoundError(f"Games.lod is missing {MAP_FILE_NAME}.")

    maps_dir = generated_root / "maps"
    maps_dir.mkdir(parents=True, exist_ok=True)

    output_path = maps_dir / MAP_FILE_NAME
    output_path.write_bytes(decode_lod_maybe_compressed(read_lod_entry(install.games_lod, entry[2])))
    return output_path


def export_and_bundle_map(
    tools_dir: Path,
    install_dir: Path,
    generated_root: Path,
    packages_root: Path,
    map_path: Path,
) -> Path:
    exports_root = generated_root / "exports"
    exports_root.mkdir(parents=True, exist_ok=True)
    packages_root.mkdir(parents=True, exist_ok=True)

    export_dir = exports_root / MAP_KEY
    bundle_dir = packages_root / MAP_KEY

    shutil.rmtree(export_dir, ignore_errors=True)
    shutil.rmtree(bundle_dir, ignore_errors=True)

    run_python(
        tools_dir / "export_mm6_outdoor_to_unity.py",
        "--map",
        str(map_path),
        "--output",
        str(export_dir),
        "--install-dir",
        str(install_dir),
    )
    run_python(
        tools_dir / "bundle_mm6_unity_package.py",
        "--input-json",
        str(export_dir / "map.json"),
        "--output-dir",
        str(bundle_dir),
    )
    return bundle_dir


def main() -> int:
    args = parse_args()
    tools_dir = Path(__file__).resolve().parent

    install = load_install(args.install_dir)
    if install is None:
        raise FileNotFoundError(
            "Could not find a complete MM6 install. Pass --install-dir pointing at the game root."
        )

    generated_root = args.generated_root.resolve()
    packages_root = args.packages_root.resolve()

    ensure_cache(tools_dir, install.install_dir, generated_root)
    map_path = extract_new_sorpigal_map(install.install_dir, generated_root)
    bundle_dir = export_and_bundle_map(
        tools_dir,
        install.install_dir,
        generated_root,
        packages_root,
        map_path,
    )

    print(f"Prepared {MAP_DISPLAY_NAME} package at {bundle_dir}")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(exc, file=sys.stderr)
        raise SystemExit(1)
