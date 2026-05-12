#!/usr/bin/env python3

import argparse
import struct
import sys
import zlib
from dataclasses import dataclass
from pathlib import Path
from typing import Dict, List, Optional

from PIL import Image


ENTRY_SIZE = 32
IMAGE_HEADER_SIZE = 48
PALETTE_SIZE = 256 * 3
SPRITE_HEADER_SIZE = 32
SPRITE_LINE_SIZE = 8
GRAYSCALE_PALETTE = [component for value in range(256) for component in (value, value, value)]


@dataclass(frozen=True)
class LodEntry:
    name: str
    address: int


@dataclass(frozen=True)
class SpriteHeader:
    name: str
    data_size: int
    width: int
    height: int
    palette_id: int
    empty_bottom_lines: int
    flags: int
    decompressed_size: int


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Convert Might and Magic sprites LOD entries to standard PNG files."
    )
    parser.add_argument("archive", type=Path, help="Path to a *.sprites.lod archive")
    parser.add_argument("output", type=Path, help="Directory to write PNG files into")
    parser.add_argument(
        "--palettes-lod",
        type=Path,
        help="Optional path to a BITMAPS.LOD archive containing pal### entries for sprite coloring",
    )
    return parser.parse_args()


def parse_entries(data: bytes) -> list[LodEntry]:
    archive_start = struct.unpack_from("<I", data, 272)[0]
    count = struct.unpack_from("<H", data, 284)[0]
    entries: list[LodEntry] = []

    for index in range(count):
        offset = archive_start + index * ENTRY_SIZE
        name = data[offset : offset + 16].split(b"\0", 1)[0].decode("latin1")
        relative_address = struct.unpack_from("<I", data, offset + 16)[0]
        entries.append(LodEntry(name=name, address=archive_start + relative_address))

    entries.sort(key=lambda entry: entry.address)
    return entries


def parse_sprite_header(blob: bytes) -> SpriteHeader:
    values = struct.unpack_from("<12sIHHHHHHI", blob, 0)
    return SpriteHeader(
        name=values[0].split(b"\0", 1)[0].decode("latin1"),
        data_size=values[1],
        width=values[2],
        height=values[3],
        palette_id=values[4],
        empty_bottom_lines=values[6],
        flags=values[7],
        decompressed_size=values[8],
    )


def parse_palette_entry(blob: bytes) -> List[int]:
    if len(blob) < IMAGE_HEADER_SIZE + PALETTE_SIZE:
        raise ValueError(f"Palette entry is too short: expected at least {IMAGE_HEADER_SIZE + PALETTE_SIZE} bytes")

    payload_size = struct.unpack_from("<I", blob, 20)[0]
    palette_start = IMAGE_HEADER_SIZE + payload_size
    palette_end = palette_start + PALETTE_SIZE
    palette = blob[palette_start:palette_end]
    if len(palette) != PALETTE_SIZE:
        raise ValueError(f"Palette entry is missing bytes: expected {PALETTE_SIZE}, got {len(palette)}")
    return list(palette)


def load_palettes(palette_archive: Optional[Path]) -> Dict[int, List[int]]:
    if palette_archive is None:
        return {}

    data = palette_archive.read_bytes()
    entries = parse_entries(data)
    palettes: Dict[int, List[int]] = {}

    for index, entry in enumerate(entries):
        lower_name = entry.name.lower()
        if not (lower_name.startswith("pal") and len(lower_name) == 6 and lower_name[3:].isdigit()):
            continue

        end = entries[index + 1].address if index + 1 < len(entries) else len(data)
        blob = data[entry.address:end]
        palettes[int(lower_name[3:])] = parse_palette_entry(blob)

    return palettes


def decode_sprite(header: SpriteHeader, blob: bytes) -> bytes:
    lines_offset = SPRITE_HEADER_SIZE
    lines_size = header.height * SPRITE_LINE_SIZE
    pixels_offset = lines_offset + lines_size

    if len(blob) < pixels_offset + header.data_size:
        raise ValueError(
            f"{header.name}: entry is too short for {header.height} sprite lines and {header.data_size} bytes of payload"
        )

    pixels_data = blob[pixels_offset : pixels_offset + header.data_size]
    if header.decompressed_size:
        pixels_data = zlib.decompress(pixels_data)

    image = bytearray(header.width * header.height)
    for row in range(header.height):
        begin, end, offset = struct.unpack_from("<hhI", blob, lines_offset + row * SPRITE_LINE_SIZE)
        if begin == end:
            continue
        if begin < 0 or end < 0 or begin > header.width or end > header.width or begin > end:
            raise ValueError(f"{header.name}: invalid sprite line bounds at row {row}")
        count = end - begin
        if offset < 0 or offset + count > len(pixels_data):
            raise ValueError(f"{header.name}: invalid sprite pixel offset at row {row}")
        row_start = row * header.width + begin
        image[row_start : row_start + count] = pixels_data[offset : offset + count]

    return bytes(image)


def convert_entry(name: str, blob: bytes, output_dir: Path, palettes: Dict[int, List[int]]) -> None:
    header = parse_sprite_header(blob)
    pixels = decode_sprite(header, blob)
    image = Image.frombytes("P", (header.width, header.height), pixels)
    image.putpalette(palettes.get(header.palette_id, GRAYSCALE_PALETTE))
    image.info["transparency"] = 0
    image.save(output_dir / f"{name}.png", format="PNG")


def main() -> int:
    args = parse_args()
    data = args.archive.read_bytes()
    entries = parse_entries(data)
    palettes = load_palettes(args.palettes_lod)
    args.output.mkdir(parents=True, exist_ok=True)

    converted = 0
    for index, entry in enumerate(entries):
        end = entries[index + 1].address if index + 1 < len(entries) else len(data)
        blob = data[entry.address:end]
        convert_entry(entry.name, blob, args.output, palettes)
        converted += 1

    print(
        f"Converted {converted} entries from {args.archive} to {args.output}"
        + (f" using {len(palettes)} palettes from {args.palettes_lod}" if args.palettes_lod else "")
    )
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(exc, file=sys.stderr)
        raise SystemExit(1)
