#!/usr/bin/env python3

import argparse
import struct
import sys
import zlib
from dataclasses import dataclass
from pathlib import Path

from PIL import Image


MM_HEADER_SIZE = 288
ENTRY_SIZE = 32
IMAGE_HEADER_SIZE = 48
PALETTE_SIZE = 256 * 3
TRANSPARENT_WATER_EDGE_PREFIXES = ("wtrdr", "hwtrdr")


@dataclass(frozen=True)
class LodEntry:
    name: str
    address: int


@dataclass(frozen=True)
class LodImageHeader:
    name: str
    size: int
    data_size: int
    width: int
    height: int
    width_ln2: int
    height_ln2: int
    width_minus_1: int
    height_minus_1: int
    palette_id: int
    another_palette_id: int
    decompressed_size: int
    flags: int


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Convert Might and Magic bitmaps LOD textures to standard PNG files."
    )
    parser.add_argument("archive", type=Path, help="Path to a *.bitmaps.lod archive")
    parser.add_argument("output", type=Path, help="Directory to write PNG files into")
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


def parse_image_header(blob: bytes) -> LodImageHeader:
    values = struct.unpack_from("<16sIIHHhhhhhhII", blob, 0)
    name = values[0].split(b"\0", 1)[0].decode("latin1")
    return LodImageHeader(
        name=name,
        size=values[1],
        data_size=values[2],
        width=values[3],
        height=values[4],
        width_ln2=values[5],
        height_ln2=values[6],
        width_minus_1=values[7],
        height_minus_1=values[8],
        palette_id=values[9],
        another_palette_id=values[10],
        decompressed_size=values[11],
        flags=values[12],
    )


def decode_pixels(header: LodImageHeader, blob: bytes) -> bytes:
    payload_start = IMAGE_HEADER_SIZE
    payload_end = payload_start + header.data_size
    payload = blob[payload_start:payload_end]
    if len(payload) != header.data_size:
        raise ValueError(
            f"{header.name}: expected {header.data_size} bytes of pixel payload, got {len(payload)}"
        )

    pixels = zlib.decompress(payload) if header.decompressed_size else payload
    expected = header.width * header.height
    if len(pixels) < expected:
        raise ValueError(
            f"{header.name}: expected at least {expected} pixel bytes, got {len(pixels)}"
        )
    return pixels[:expected]


def decode_palette(header: LodImageHeader, blob: bytes) -> list[int]:
    palette_start = IMAGE_HEADER_SIZE + header.data_size
    palette_end = palette_start + PALETTE_SIZE
    palette = blob[palette_start:palette_end]
    if len(palette) != PALETTE_SIZE:
        raise ValueError(
            f"{header.name}: expected {PALETTE_SIZE} bytes of palette data, got {len(palette)}"
        )
    return list(palette)


def palette_rgb(palette: list[int], index: int) -> tuple[int, int, int]:
    offset = index * 3
    return (
        palette[offset],
        palette[offset + 1],
        palette[offset + 2],
    )


def is_processed_transparent_bitmap(name: str) -> bool:
    lower_name = name.lower()
    return lower_name.startswith(TRANSPARENT_WATER_EDGE_PREFIXES)


def process_transparent_pixel(
    pixels: bytes,
    width: int,
    height: int,
    palette: list[int],
    x: int,
    y: int,
) -> tuple[int, int, int, int]:
    count = 0
    r = 0
    g = 0
    b = 0

    for sample_y in range(max(0, y - 1), min(height, y + 2)):
        for sample_x in range(max(0, x - 1), min(width, x + 2)):
            if sample_x == x and sample_y == y:
                continue

            palette_index = pixels[sample_y * width + sample_x]
            if palette_index == 0:
                continue

            sample_r, sample_g, sample_b = palette_rgb(palette, palette_index)
            count += 1
            r += sample_r
            g += sample_g
            b += sample_b

    if count:
        r //= count
        g //= count
        b //= count
    else:
        r = g = b = 0

    return (r, g, b, 0)


def build_processed_transparent_rgba(
    header: LodImageHeader,
    pixels: bytes,
    palette: list[int],
) -> Image.Image:
    rgba = bytearray(header.width * header.height * 4)
    for y in range(header.height):
        for x in range(header.width):
            palette_index = pixels[y * header.width + x]
            dest = (y * header.width + x) * 4
            if palette_index == 0:
                r, g, b, a = process_transparent_pixel(pixels, header.width, header.height, palette, x, y)
            else:
                r, g, b = palette_rgb(palette, palette_index)
                a = 255

            rgba[dest] = r
            rgba[dest + 1] = g
            rgba[dest + 2] = b
            rgba[dest + 3] = a

    return Image.frombytes("RGBA", (header.width, header.height), bytes(rgba))


def convert_entry(name: str, blob: bytes, output_dir: Path) -> None:
    header = parse_image_header(blob)

    if header.size == 0 and header.data_size == 0 and header.width == 0 and header.height == 0:
        image = Image.new("P", (256, 1))
        image.putpalette(decode_palette(header, blob))
    else:
        pixels = decode_pixels(header, blob)
        palette = decode_palette(header, blob)
        if is_processed_transparent_bitmap(name):
            image = build_processed_transparent_rgba(header, pixels, palette)
        else:
            image = Image.frombytes("P", (header.width, header.height), pixels)
            image.putpalette(palette)
            if header.flags & 0x0200:
                image.info["transparency"] = 0

    image.save(output_dir / f"{name}.png", format="PNG")


def main() -> int:
    args = parse_args()
    data = args.archive.read_bytes()
    if len(data) < MM_HEADER_SIZE:
        raise ValueError(f"{args.archive} is too small to be a valid MM LOD archive")

    entries = parse_entries(data)
    args.output.mkdir(parents=True, exist_ok=True)

    converted = 0
    for index, entry in enumerate(entries):
        end = entries[index + 1].address if index + 1 < len(entries) else len(data)
        blob = data[entry.address:end]
        convert_entry(entry.name, blob, args.output)
        converted += 1

    print(f"Converted {converted} entries from {args.archive} to {args.output}")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(exc, file=sys.stderr)
        raise SystemExit(1)
