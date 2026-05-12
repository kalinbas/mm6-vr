#!/usr/bin/env python3

from __future__ import annotations

import argparse
import json
import subprocess
import sys
from pathlib import Path

from mm6_install_data import (
    load_install,
    parse_dtft_bin_bytes,
    parse_dtile_bin_bytes,
    parse_mapstats_text,
    parse_monlist_bin,
    parse_monsters_text,
    parse_sft_bin,
    read_mm6_icons_table,
    read_mm6_icons_text_table,
)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Build a reusable cache of original MM6 textures and decoded icons.lod tables."
    )
    parser.add_argument(
        "--install-dir",
        type=Path,
        help="Path to the original MM6 install directory. Defaults to auto-detection.",
    )
    parser.add_argument(
        "--output-root",
        type=Path,
        default=Path("generated/mm6-install"),
        help="Directory where the generated cache will be written.",
    )
    parser.add_argument(
        "--skip-textures",
        action="store_true",
        help="Skip BITMAPS.LOD and SPRITES.LOD PNG conversion.",
    )
    parser.add_argument(
        "--skip-tables",
        action="store_true",
        help="Skip decoded icons.lod table extraction.",
    )
    return parser.parse_args()


def run_converter(script: Path, *args: str) -> None:
    subprocess.run(
        [sys.executable, str(script), *args],
        check=True,
    )


def write_text(path: Path, content: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="latin1")


def write_bytes(path: Path, content: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(content)


def main() -> int:
    args = parse_args()
    install = load_install(args.install_dir)
    if install is None:
        raise FileNotFoundError(
            "Could not find a complete MM6 install. Pass --install-dir pointing at the game root."
        )

    output_root = args.output_root.resolve()
    tables_dir = output_root / "tables"
    bitmaps_dir = output_root / "bitmaps"
    sprites_dir = output_root / "sprites"

    if not args.skip_tables:
        table_names = [
            "DSFT.BIN",
            "DDECLIST.BIN",
            "DMONLIST.BIN",
            "DTILE.BIN",
            "DTFT.BIN",
            "MapStats.txt",
            "MONSTERS.TXT",
        ]
        for name in table_names:
            if name.lower().endswith(".txt"):
                write_text(tables_dir / name, read_mm6_icons_text_table(install.icons_lod, name))
            else:
                write_bytes(tables_dir / name, read_mm6_icons_table(install.icons_lod, name))

        sft_groups, _frame_group_names = parse_sft_bin(read_mm6_icons_table(install.icons_lod, "DSFT.BIN"))
        dtile = parse_dtile_bin_bytes(read_mm6_icons_table(install.icons_lod, "DTILE.BIN"))
        dtft = parse_dtft_bin_bytes(read_mm6_icons_table(install.icons_lod, "DTFT.BIN"))
        monlist = parse_monlist_bin(read_mm6_icons_table(install.icons_lod, "DMONLIST.BIN"))
        mapstats = parse_mapstats_text(read_mm6_icons_text_table(install.icons_lod, "MapStats.txt"))
        monsters = parse_monsters_text(read_mm6_icons_text_table(install.icons_lod, "MONSTERS.TXT"))

        summary = {
            "installDir": str(install.install_dir),
            "tables": {
                "sftGroups": len(sft_groups),
                "monstersInMonList": len(monlist),
                "tileRecords": len(dtile),
                "textureAnimations": len(dtft),
                "outdoorMapStats": len(mapstats),
                "monsterRows": len(monsters),
            },
        }
        write_text(tables_dir / "summary.json", json.dumps(summary, indent=2))

    if not args.skip_textures:
        bitmaps_dir.mkdir(parents=True, exist_ok=True)
        sprites_dir.mkdir(parents=True, exist_ok=True)

        tools_dir = Path(__file__).resolve().parent
        run_converter(
            tools_dir / "convert_mm_bitmaps_lod_to_png.py",
            str(install.bitmaps_lod),
            str(bitmaps_dir),
        )
        run_converter(
            tools_dir / "convert_mm_sprites_lod_to_png.py",
            str(install.sprites_lod),
            str(sprites_dir),
            "--palettes-lod",
            str(install.bitmaps_lod),
        )

    print(f"Prepared MM6 install cache in {output_root}")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(exc, file=sys.stderr)
        raise SystemExit(1)
