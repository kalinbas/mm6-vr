#!/usr/bin/env python3

from __future__ import annotations

import argparse
import shutil
import subprocess
import sys
from pathlib import Path

from mm6_install_data import decode_lod_maybe_compressed, list_lod_entries, load_install, read_lod_entry


EXPECTED_OUTDOOR_MAPS = [
    "outa1.odm",
    "outa2.odm",
    "outa3.odm",
    "outb1.odm",
    "outb2.odm",
    "outb3.odm",
    "outc1.odm",
    "outc2.odm",
    "outc3.odm",
    "outd1.odm",
    "outd2.odm",
    "outd3.odm",
    "oute1.odm",
    "oute2.odm",
    "oute3.odm",
]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Extract, export, and bundle all 15 original MM6 outdoor maps for the Unity importer."
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
        default=Path("unity/MM6OutdoorImporter/MM6Packages"),
        help="Destination folder for bundled Unity outdoor packages.",
    )
    return parser.parse_args()


def run_python(script: Path, *args: str) -> None:
    subprocess.run([sys.executable, str(script), *args], check=True)


def ensure_cache(tools_dir: Path, install_dir: Path, generated_root: Path) -> None:
    bitmaps_dir = generated_root / "bitmaps"
    sprites_dir = generated_root / "sprites"
    tables_dir = generated_root / "tables"
    if bitmaps_dir.exists() and sprites_dir.exists() and tables_dir.exists():
        return

    run_python(
        tools_dir / "prepare_mm6_install_cache.py",
        "--install-dir",
        str(install_dir),
        "--output-root",
        str(generated_root),
    )


def extract_outdoor_maps(install_dir: Path, generated_root: Path) -> list[Path]:
    install = load_install(install_dir)
    if install is None:
        raise FileNotFoundError("Could not resolve a complete MM6 install.")

    games_entries = list_lod_entries(install.games_lod)
    maps_dir = generated_root / "maps"
    maps_dir.mkdir(parents=True, exist_ok=True)

    extracted: list[Path] = []
    missing = []
    for expected_name in EXPECTED_OUTDOOR_MAPS:
        entry = games_entries.get(expected_name)
        if entry is None:
            missing.append(expected_name)
            continue

        original_name = entry[2]
        output_path = maps_dir / expected_name
        output_path.write_bytes(decode_lod_maybe_compressed(read_lod_entry(install.games_lod, original_name)))
        extracted.append(output_path)

    if missing:
        raise FileNotFoundError(
            "Games.lod is missing expected outdoor maps: " + ", ".join(missing)
        )

    return extracted


def export_and_bundle_maps(
    tools_dir: Path,
    install_dir: Path,
    generated_root: Path,
    packages_root: Path,
    extracted_maps: list[Path],
) -> None:
    exports_root = generated_root / "exports"
    exports_root.mkdir(parents=True, exist_ok=True)
    packages_root.mkdir(parents=True, exist_ok=True)

    export_script = tools_dir / "export_mm6_outdoor_to_unity.py"
    bundle_script = tools_dir / "bundle_mm6_unity_package.py"

    for map_path in extracted_maps:
        map_key = map_path.stem.lower()
        export_dir = exports_root / map_key
        bundle_dir = packages_root / map_key

        shutil.rmtree(export_dir, ignore_errors=True)
        shutil.rmtree(bundle_dir, ignore_errors=True)

        run_python(
            export_script,
            "--map",
            str(map_path),
            "--output",
            str(export_dir),
            "--install-dir",
            str(install_dir),
        )
        run_python(
            bundle_script,
            "--input-json",
            str(export_dir / "map.json"),
            "--output-dir",
            str(bundle_dir),
        )


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
    extracted_maps = extract_outdoor_maps(install.install_dir, generated_root)
    export_and_bundle_maps(
        tools_dir,
        install.install_dir,
        generated_root,
        packages_root,
        extracted_maps,
    )

    print(
        f"Prepared {len(extracted_maps)} outdoor packages in {packages_root}"
    )
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(exc, file=sys.stderr)
        raise SystemExit(1)
