#!/usr/bin/env python3

from __future__ import annotations

import argparse
import shutil
import sys
from pathlib import Path

from build_mm6_outdoor_world_packages import ensure_cache, run_python
from mm6_install_data import decode_lod_maybe_compressed, list_lod_entries, load_install, read_lod_entry


TEST_MAPS: tuple[tuple[str, str, str], ...] = (
    ("d01.blv", "d01", "Goblinwatch"),
    ("d02.blv", "d02", "Abandoned Temple"),
    ("d03.blv", "d03", "Shadow Guild Hideout"),
    ("d05.blv", "d05", "Snergle's Caverns"),
    ("d11.blv", "d11", "Corlagon's Estate"),
    ("sewer.blv", "sewer", "Free Haven Sewers"),
)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Extract, export, and bundle a curated set of original MM6 indoor maps for Unity testing."
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
        help="Generated cache root used for extracted maps and exported JSON.",
    )
    parser.add_argument(
        "--packages-root",
        type=Path,
        default=Path("unity/MM6OutdoorImporter/MM6SingleIndoorPackages"),
        help="Destination folder for the bundled indoor Unity test packages.",
    )
    return parser.parse_args()


def extract_map(install_dir: Path, generated_root: Path, map_file_name: str) -> Path:
    install = load_install(install_dir)
    if install is None:
        raise FileNotFoundError("Could not resolve a complete MM6 install.")

    games_entries = list_lod_entries(install.games_lod)
    entry = games_entries.get(map_file_name)
    if entry is None:
        raise FileNotFoundError(f"Games.lod is missing {map_file_name}.")

    maps_dir = generated_root / "maps"
    maps_dir.mkdir(parents=True, exist_ok=True)

    output_path = maps_dir / map_file_name
    output_path.write_bytes(decode_lod_maybe_compressed(read_lod_entry(install.games_lod, entry[2])))
    return output_path


def export_and_bundle_map(
    tools_dir: Path,
    install_dir: Path,
    generated_root: Path,
    packages_root: Path,
    map_key: str,
    map_path: Path,
) -> Path:
    exports_root = generated_root / "exports"
    exports_root.mkdir(parents=True, exist_ok=True)
    packages_root.mkdir(parents=True, exist_ok=True)

    export_dir = exports_root / map_key
    bundle_dir = packages_root / map_key

    shutil.rmtree(export_dir, ignore_errors=True)
    shutil.rmtree(bundle_dir, ignore_errors=True)

    run_python(
        tools_dir / "export_mm6_indoor_to_unity.py",
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

    built: list[tuple[str, Path]] = []
    for map_file_name, map_key, display_name in TEST_MAPS:
        map_path = extract_map(install.install_dir, generated_root, map_file_name)
        bundle_dir = export_and_bundle_map(
            tools_dir,
            install.install_dir,
            generated_root,
            packages_root,
            map_key,
            map_path,
        )
        built.append((display_name, bundle_dir))

    print("Prepared indoor test packages:")
    for display_name, bundle_dir in built:
        print(f"- {display_name}: {bundle_dir}")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(exc, file=sys.stderr)
        raise SystemExit(1)
