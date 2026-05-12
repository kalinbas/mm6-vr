#!/usr/bin/env python3

import argparse
import re
import sys
from pathlib import Path

from PIL import Image

import convert_mm_bitmaps_lod_to_png as bitmaps
import convert_mm_sprites_lod_to_png as sprites


BITMAPS_PALETTE_RE = re.compile(r"pal\d{3}$", re.IGNORECASE)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Extract mod-exclusive MM bitmap and sprite images to PNG."
    )
    parser.add_argument(
        "--output-root",
        type=Path,
        default=Path("converted/mod-additional"),
        help="Root output directory for extracted PNG folders",
    )
    return parser.parse_args()


def read_entries(archive_path: Path):
    data = archive_path.read_bytes()
    entries = bitmaps.parse_entries(data)
    return data, entries


def bitmap_png(blob: bytes) -> Image.Image:
    header = bitmaps.parse_image_header(blob)
    payload = blob[bitmaps.IMAGE_HEADER_SIZE : bitmaps.IMAGE_HEADER_SIZE + header.data_size]
    pixels = bitmaps.zlib.decompress(payload) if header.decompressed_size else payload
    palette_start = bitmaps.IMAGE_HEADER_SIZE + header.data_size
    expected_rgb_size = header.width * header.height * 3

    # Some mod-added hd.bitmaps.lwd entries are truecolor RGB payloads with
    # extra trailing archive data after the compressed image. Prefer the
    # explicit RGB payload size over "palette bytes exist later in the blob".
    if header.decompressed_size == expected_rgb_size and len(pixels) >= expected_rgb_size:
        return Image.frombytes("RGB", (header.width, header.height), pixels[:expected_rgb_size])

    if len(blob) >= palette_start + bitmaps.PALETTE_SIZE:
        image = Image.frombytes("P", (header.width, header.height), pixels[: header.width * header.height])
        image.putpalette(list(blob[palette_start : palette_start + bitmaps.PALETTE_SIZE]))
        if header.flags & 0x0200:
            image.info["transparency"] = 0
        return image

    width = header.width_minus_1 + 1 if header.width_minus_1 > 0 else header.width
    height = header.height_minus_1 + 1 if header.height_minus_1 > 0 else header.height
    expected_rgb_size = width * height * 3
    if len(pixels) < expected_rgb_size:
        raise ValueError(
            f"{header.name}: unsupported bitmap payload, expected palette or {expected_rgb_size} bytes of RGB data, got {len(pixels)}"
        )
    return Image.frombytes("RGB", (width, height), pixels[:expected_rgb_size])


def extract_bitmaps(archive_path: Path, output_dir: Path, known_base_names: set[str]) -> int:
    data, entries = read_entries(archive_path)
    output_dir.mkdir(parents=True, exist_ok=True)
    written = 0

    for index, entry in enumerate(entries):
        lower_name = entry.name.lower()
        if lower_name in known_base_names or BITMAPS_PALETTE_RE.fullmatch(lower_name):
            continue

        end = entries[index + 1].address if index + 1 < len(entries) else len(data)
        image = bitmap_png(data[entry.address:end])
        image.save(output_dir / f"{entry.name}.png", format="PNG")
        written += 1

    return written


def extract_sprites(
    archive_path: Path, output_dir: Path, known_base_names: set[str], palette_archive: Path
) -> int:
    data, entries = read_entries(archive_path)
    palettes = sprites.load_palettes(palette_archive)
    output_dir.mkdir(parents=True, exist_ok=True)
    written = 0

    for index, entry in enumerate(entries):
        lower_name = entry.name.lower()
        if lower_name in known_base_names:
            continue

        end = entries[index + 1].address if index + 1 < len(entries) else len(data)
        sprites.convert_entry(entry.name, data[entry.address:end], output_dir, palettes)
        written += 1

    return written


def entry_names(archive_path: Path) -> set[str]:
    _, entries = read_entries(archive_path)
    return {entry.name.lower() for entry in entries}


def main() -> int:
    args = parse_args()

    base_bitmaps = entry_names(Path("data/hd.bitmaps.lod"))
    base_sprites = entry_names(Path("data/hd.sprites.lod"))
    palette_archive = Path("mods/tccr/BITMAPS.LOD")

    jobs = [
        (
            "mods/tccr/BITMAPS.LOD",
            args.output_root / "bitmaps_lod",
            base_bitmaps,
            extract_bitmaps,
        ),
        (
            "mods/tccr/31 redone hd.bitmaps.lwd",
            args.output_root / "hd_bitmaps_lwd",
            base_bitmaps,
            extract_bitmaps,
        ),
        (
            "mods/tccr/SPRITES.LOD",
            args.output_root / "sprites_lod",
            base_sprites,
            lambda archive, out, known: extract_sprites(archive, out, known, palette_archive),
        ),
        (
            "mods/tccr/31 redone hd.sprites.lod",
            args.output_root / "hd_sprites_lod",
            base_sprites,
            lambda archive, out, known: extract_sprites(archive, out, known, palette_archive),
        ),
    ]

    total_written = 0
    for archive_str, output_dir, known_names, extractor in jobs:
        archive_path = Path(archive_str)
        written = extractor(archive_path, output_dir, known_names)
        total_written += written
        print(f"{archive_path} -> {output_dir}: {written} PNGs")

    print(f"Total additional mod images extracted: {total_written}")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(exc, file=sys.stderr)
        raise SystemExit(1)
