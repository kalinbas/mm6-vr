#!/usr/bin/env python3

from __future__ import annotations

import argparse
import json
import re
import shutil
from pathlib import Path


def sanitize_file_name(name: str) -> str:
    name = name.strip() or "unnamed"
    return re.sub(r'[<>:"/\\\\|?*]', "_", name)


def rewrite_group(entries: list[dict], package_dir: Path, subdir: str) -> tuple[list[dict], int]:
    copied = 0
    target_dir = package_dir / "Textures" / subdir
    target_dir.mkdir(parents=True, exist_ok=True)

    for entry in entries:
        source_path = entry.get("sourcePath") or ""
        found = bool(entry.get("found"))
        resolved_source = Path(source_path).expanduser()

        if found and resolved_source.exists():
            ext = resolved_source.suffix or ".png"
            dest_name = sanitize_file_name(entry.get("name") or "unnamed") + ext
            dest_path = target_dir / dest_name
            shutil.copy2(resolved_source, dest_path)
            entry["sourcePath"] = str(dest_path.relative_to(package_dir)).replace("\\", "/")
            copied += 1
        else:
            entry["found"] = False
            entry["sourcePath"] = ""

    return entries, copied


def bundle_package(input_json: Path, output_dir: Path) -> None:
    package = json.loads(input_json.read_text())
    output_dir.mkdir(parents=True, exist_ok=True)

    textures = package.get("textures") or {}
    bitmaps, copied_bitmaps = rewrite_group(
        list(textures.get("bitmaps") or []),
        output_dir,
        "Bitmaps",
    )
    sprites, copied_sprites = rewrite_group(
        list(textures.get("sprites") or []),
        output_dir,
        "Sprites",
    )

    package.setdefault("textures", {})
    package["textures"]["bitmaps"] = bitmaps
    package["textures"]["sprites"] = sprites

    output_json = output_dir / "map.json"
    output_json.write_text(json.dumps(package, indent=2))

    print(f"Wrote {output_json}")
    print(f"Copied {copied_bitmaps} bitmap textures")
    print(f"Copied {copied_sprites} sprite textures")


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Bundle an exported MM6 Unity package into a self-contained folder."
    )
    parser.add_argument(
        "--input-json",
        required=True,
        type=Path,
        help="Path to the exported map.json file",
    )
    parser.add_argument(
        "--output-dir",
        required=True,
        type=Path,
        help="Destination folder for the bundled package",
    )
    args = parser.parse_args()

    bundle_package(args.input_json.resolve(), args.output_dir.resolve())


if __name__ == "__main__":
    main()
