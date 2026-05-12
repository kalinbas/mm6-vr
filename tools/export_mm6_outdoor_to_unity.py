#!/usr/bin/env python3

from __future__ import annotations

import argparse
import csv
import json
import math
import struct
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Optional

from PIL import Image
from mm6_install_data import (
    build_default_bitmap_dirs,
    build_default_sprite_dirs,
    build_monster_records,
    load_install,
    parse_dec_list_bin,
    parse_dtile_bin_bytes,
    parse_mapstats_text,
    parse_monlist_bin,
    parse_monsters_text,
    parse_sft_bin,
    read_mm6_outdoor_actors,
    read_mm6_icons_table,
    read_mm6_icons_text_table,
)


GRID_SIZE = 128
CELL_SIZE = 512
HEIGHT_SCALE = 32
MODEL_SIZE = 0xBC
MODEL_FACE_SIZE = 0x134
SPRITE_SIZE = 0x1C
SPAWN_SIZE = 20
MM6_OUTDOOR_VERSION = "MM6 Outdoor v1.11"
MM6_HORIZONTAL_VFLIP_NORMAL_Z = 0xE6CA
DEFAULT_BITMAP_UV_SCALE = 2.0
SFT_TIME_TO_SECONDS = 1.0 / 8.0

DEFAULT_BITMAP_DIRS = build_default_bitmap_dirs()
DEFAULT_SPRITE_DIRS = build_default_sprite_dirs()
DEFAULT_DEC_LIST: Optional[Path] = None
DEFAULT_SFT: Optional[Path] = None
DEFAULT_INSTALL = load_install()
DEFAULT_INSTALL_DIR = DEFAULT_INSTALL.install_dir if DEFAULT_INSTALL else None

TILESET_GROUP_NAMES = {
    0: "grass",
    1: "snow",
    2: "desert",
    3: "cooled_lava",
    4: "dirt",
    5: "water",
    6: "badlands",
    7: "swamp",
    8: "tropical",
    9: "city",
    10: "road_grass_cobble",
    11: "road_grass_dirt",
    12: "road_snow_cobble",
    13: "road_snow_dirt",
    14: "road_sand_cobble",
    15: "road_sand_dirt",
    16: "road_volcano_cobble",
    17: "road_volcano_dirt",
    22: "road_cracked_cobble",
    23: "road_cracked_dirt",
    24: "road_swamp_cobble",
    25: "road_swamp_dirt",
    26: "road_tropical_cobble",
    27: "road_tropical_dirt",
    28: "road_city_stone",
}

BASE_TEXTURE_BY_TILESET_GROUP = {
    0: "Grastyl",
    1: "Snotyl",
    2: "Sandtyl",
    3: "DirtTyl",
    4: "DirtTyl",
    5: "WtrTyl",
    6: "CrkTyl",
    7: "swmtyl",
    8: "Troptyl",
    9: "Sandtyl",
    10: "GrsrCROS",
    11: "drsrCROS",
    12: "drsrCROS",
    13: "drsrCROS",
    14: "drsrCROS",
    15: "drsrCROS",
    16: "drsrCROS",
    17: "drsrCROS",
    22: "drsrCROS",
    23: "drsrCROS",
    24: "drsrCROS",
    25: "drsrCROS",
    26: "drsrCROS",
    27: "drsrCROS",
    28: "csTYL",
}

SPAWN_SLOT_NAMES = {
    1: "m1",
    2: "m2",
    3: "m3",
    4: "m1a",
    5: "m2a",
    6: "m3a",
    7: "m1b",
    8: "m2b",
    9: "m3b",
    10: "m1c",
    11: "m2c",
    12: "m3c",
}

MONSTER_DIFFICULTY_WEIGHTS = {
    0: (100, 0, 0),
    1: (90, 8, 2),
    2: (70, 20, 10),
    3: (50, 30, 20),
    4: (30, 40, 30),
    5: (10, 50, 40),
}

DEFAULT_DAY_SKY_TEXTURES = ("PLANSKY1", "PLANSKY2")
DEFAULT_SNOW_SKY_TEXTURE = "sky19"
DEFAULT_START_HOUR = 9.0
DEFAULT_TIME_SCALE_HOURS_PER_REAL_SECOND = 1.0 / 120.0
DEFAULT_START_DAY_OF_MONTH = 1
DEFAULT_START_MONTH = 6

# Fallback used only when original MM6 `icons.lod` table data is unavailable.
MAP_MONSTER_FALLBACKS: dict[str, dict[str, Any]] = {
    "oute3": {
        "source": (
            "Encounter families use New Sorpigal reference data. "
            "Exact MapStats.txt group counts were not available locally, so "
            "group sizes are approximated for Unity preview."
        ),
        "countsAreApproximate": True,
        "slots": {
            "m1": {
                "difficulty": 3,
                "countRange": [2, 3],
                "activationDistance": 5120.0,
                "loseInterestDistance": 7680.0,
                "stopDistance": 320.0,
                "moveSpeed": 480.0,
                "variants": {
                    "A": {
                        "name": "Goblin",
                        "standingSftGroup": "gobsta",
                        "standingTextureName": "gobsta0",
                        "walkingSftGroup": "gobwaa",
                        "walkingTextureName": "gobwaa0",
                    },
                    "B": {
                        "name": "Goblin Shaman",
                        "standingSftGroup": "gbbsta",
                        "standingTextureName": "gobsta0",
                        "walkingSftGroup": "gbbwaa",
                        "walkingTextureName": "gobwaa0",
                    },
                    "C": {
                        "name": "Goblin King",
                        "standingSftGroup": "gCbsta",
                        "standingTextureName": "gobsta0",
                        "walkingSftGroup": "gCbwaa",
                        "walkingTextureName": "gobwaa0",
                    },
                },
            },
            "m2": {
                "difficulty": 3,
                "countRange": [1, 2],
                "activationDistance": 5120.0,
                "loseInterestDistance": 7680.0,
                "stopDistance": 896.0,
                "moveSpeed": 384.0,
                "variants": {
                    "A": {
                        "name": "Apprentice Mage",
                        "standingSftGroup": "wd1fly",
                        "standingTextureName": "wdmflya0",
                        "walkingSftGroup": "wd1fly",
                        "walkingTextureName": "wdmflya0",
                    },
                    "B": {
                        "name": "Journeyman Mage",
                        "standingSftGroup": "wd2fly",
                        "standingTextureName": "wdmflya0",
                        "walkingSftGroup": "wd2fly",
                        "walkingTextureName": "wdmflya0",
                    },
                    "C": {
                        "name": "Mage",
                        "standingSftGroup": "wd3fly",
                        "standingTextureName": "wdmflya0",
                        "walkingSftGroup": "wd3fly",
                        "walkingTextureName": "wdmflya0",
                    },
                },
            },
        },
    }
}


@dataclass(frozen=True)
class TilesetDef:
    group: int
    offset: int


@dataclass(frozen=True)
class TextureInfo:
    name: str
    source_path: Optional[Path]
    width: int
    height: int
    uv_width: int
    uv_height: int
    kind: str


class Reader:
    def __init__(self, data: bytes):
        self.data = data
        self.offset = 0

    def read(self, size: int) -> bytes:
        blob = self.data[self.offset : self.offset + size]
        if len(blob) != size:
            raise EOFError(
                f"Unexpected end of file at {self.offset}, expected {size} bytes, got {len(blob)}"
            )
        self.offset += size
        return blob

    def cstr(self, size: int) -> str:
        return self.read(size).split(b"\0", 1)[0].decode("latin1")

    def u32(self) -> int:
        return struct.unpack("<I", self.read(4))[0]

    def i16(self) -> int:
        return struct.unpack("<h", self.read(2))[0]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Export an MM6 outdoor ODM map into a Unity-friendly JSON package."
    )
    parser.add_argument(
        "--map",
        type=Path,
        required=True,
        help="Path to a decompressed MM6 outdoor .odm file",
    )
    parser.add_argument(
        "--output",
        type=Path,
        required=True,
        help="Output directory for the exported package",
    )
    parser.add_argument(
        "--install-dir",
        type=Path,
        default=DEFAULT_INSTALL_DIR,
        help="Path to the original MM6 install directory. When provided, icons.lod tables are used automatically.",
    )
    parser.add_argument(
        "--bitmap-dir",
        action="append",
        dest="bitmap_dirs",
        type=Path,
        help="Directory containing bitmap PNGs. Can be supplied more than once.",
    )
    parser.add_argument(
        "--sprite-dir",
        action="append",
        dest="sprite_dirs",
        type=Path,
        help="Directory containing sprite PNGs. Can be supplied more than once.",
    )
    parser.add_argument(
        "--dec-list",
        type=Path,
        default=DEFAULT_DEC_LIST,
        help="Optional path to DecList.txt. If omitted, DecList is loaded from the original install's icons.lod.",
    )
    parser.add_argument(
        "--sft",
        type=Path,
        default=DEFAULT_SFT,
        help="Optional path to SFT.txt. If omitted, SFT is loaded from the original install's icons.lod.",
    )
    parser.add_argument(
        "--dtile-bin",
        type=Path,
        help="Optional path to dtile.bin for exact terrain tile names. If omitted, dtile is loaded from icons.lod.",
    )
    parser.add_argument(
        "--bitmap-uv-scale",
        type=float,
        default=DEFAULT_BITMAP_UV_SCALE,
        help="Divisor used to convert bitmap PNG dimensions into MM6 logical UV dimensions",
    )
    return parser.parse_args()


def read_tsv(path: Path) -> list[dict[str, str]]:
    with path.open("r", encoding="latin1", newline="") as handle:
        reader = csv.DictReader(handle, delimiter="\t")
        return [{(key or "").strip(): (value or "").strip() for key, value in row.items()} for row in reader]


def parse_dec_list(path: Path) -> dict[int, dict[str, Any]]:
    result: dict[int, dict[str, Any]] = {}
    next_identifier = 0
    for row in read_tsv(path):
        if not row:
            continue
        identifier = row.get("#", "")
        if not identifier:
            identifier = str(next_identifier)

        identifier_int = int(identifier)
        next_identifier = identifier_int + 1

        result[identifier_int] = {
            "id": identifier_int,
            "name": row.get("Name", ""),
            "game_name": row.get("GameName", ""),
            "type": int(row.get("Type", "0") or 0),
            "height": int(row.get("Height", "0") or 0),
            "radius": int(row.get("Radius", "0") or 0),
            "light_radius": int(row.get("LightRadius", "0") or 0),
            "sft_group": row.get("SFTGroup", ""),
            "no_draw": row.get("NoDraw", "").lower() == "x",
        }
    return result


def parse_sft(path: Path) -> dict[str, dict[str, Any]]:
    groups: dict[str, dict[str, Any]] = {}
    current_group = ""
    for row in read_tsv(path):
        if not row:
            continue
        group_name = row.get("GroupName", "")
        if group_name:
            current_group = group_name
        if not current_group:
            continue

        group = groups.setdefault(current_group, {"group_name": current_group, "frames": []})
        sprite_name = row.get("SpriteName", "")
        if not sprite_name or sprite_name.lower() == "null":
            continue

        group["frames"].append(
            {
                "sprite_name": sprite_name,
                "scale": int(row.get("Scale", "0") or 0),
                "time": int(row.get("Time", "0") or 0),
                "palette_id": int(row.get("PaletteId", "0") or 0),
                "transparent": row.get("Transparent", "").lower() == "x",
                "image1": row.get("Image1", "").lower() == "x",
                "images3": row.get("Images3", "").lower() == "x",
                "fidget": row.get("Fidget", "").lower() == "x",
            }
        )
    return groups


def load_decoration_tables(
    dec_list_path: Optional[Path],
    sft_path: Optional[Path],
    install_dir: Optional[Path],
) -> tuple[dict[int, dict[str, Any]], dict[str, dict[str, Any]]]:
    if dec_list_path and dec_list_path.exists() and sft_path and sft_path.exists():
        return parse_dec_list(dec_list_path), parse_sft(sft_path)

    install = load_install(install_dir)
    if install is not None:
        sft_groups, frame_group_names = parse_sft_bin(read_mm6_icons_table(install.icons_lod, "DSFT.BIN"))
        dec_list = parse_dec_list_bin(
            read_mm6_icons_table(install.icons_lod, "DDECLIST.BIN"),
            frame_group_names,
        )
        return dec_list, sft_groups

    missing = []
    if not dec_list_path or not dec_list_path.exists():
        missing.append("DecList")
    if not sft_path or not sft_path.exists():
        missing.append("SFT")
    raise FileNotFoundError(
        "Could not load MM6 decoration tables. Missing: "
        + ", ".join(missing)
        + ". Provide --install-dir with a real MM6 install or explicit --dec-list/--sft paths."
    )


def build_image_lookup(directories: list[Path]) -> dict[str, Path]:
    lookup: dict[str, Path] = {}
    for directory in directories:
        if not directory.exists():
            continue
        for image_path in sorted(directory.glob("*.png")):
            lookup.setdefault(image_path.stem.lower(), image_path.resolve())
    return lookup


def resolve_image(name: str, lookup: dict[str, Path]) -> Optional[Path]:
    return lookup.get(name.lower())


def image_size(path: Path) -> tuple[int, int]:
    with Image.open(path) as image:
        return image.size


def parse_dtile_bin(path: Path) -> dict[int, str]:
    data = path.read_bytes()
    if len(data) % 26 != 0:
        raise ValueError(f"{path} is not a multiple of 26 bytes, cannot parse as TileItem table")

    textures: dict[int, str] = {}
    for index in range(len(data) // 26):
        offset = index * 26
        name = data[offset : offset + 16].split(b"\0", 1)[0].decode("latin1")
        textures[index] = name
    return textures


def load_dtile_textures(dtile_path: Optional[Path], install_dir: Optional[Path]) -> Optional[dict[int, str]]:
    if dtile_path and dtile_path.exists():
        return parse_dtile_bin(dtile_path)

    install = load_install(install_dir)
    if install is None:
        return None

    return parse_dtile_bin_bytes(read_mm6_icons_table(install.icons_lod, "DTILE.BIN"))


def mm6_tileset_name(group: int) -> str:
    return TILESET_GROUP_NAMES.get(group, f"unknown_{group}")


def fallback_terrain_texture(local_tile_id: int, tilesets: list[TilesetDef]) -> str:
    if 1 <= local_tile_id <= 12:
        return "DirtTyl"

    if 90 <= local_tile_id <= 233:
        tileset_index = (local_tile_id - 90) // 36
        if 0 <= tileset_index < len(tilesets):
            return BASE_TEXTURE_BY_TILESET_GROUP.get(tilesets[tileset_index].group, "DirtTyl")

    return "DirtTyl"


def terrain_texture_name(
    local_tile_id: int,
    tilesets: list[TilesetDef],
    dtile_textures: Optional[dict[int, str]],
) -> str:
    if dtile_textures is None:
        return fallback_terrain_texture(local_tile_id, tilesets)

    if 90 <= local_tile_id <= 233:
        tileset_index = (local_tile_id - 90) // 36
        tile_offset = (local_tile_id - 90) % 36
        if 0 <= tileset_index < len(tilesets):
            global_tile_id = tilesets[tileset_index].offset + tile_offset
            return dtile_textures.get(global_tile_id, fallback_terrain_texture(local_tile_id, tilesets))

    return dtile_textures.get(local_tile_id, fallback_terrain_texture(local_tile_id, tilesets))


def register_texture(
    name: str,
    kind: str,
    lookup: dict[str, Path],
    manifests: dict[str, TextureInfo],
    bitmap_uv_scale: float,
) -> TextureInfo:
    key = f"{kind}:{name.lower()}"
    if key in manifests:
        return manifests[key]

    source_path = resolve_image(name, lookup)
    if source_path is not None:
        width, height = image_size(source_path)
    else:
        width, height = 0, 0

    effective_bitmap_uv_scale = bitmap_uv_scale
    if source_path is not None:
        source_path_text = source_path.as_posix().lower()
        # Vanilla MM6 bitmaps already use their final logical size. Only the
        # MM6HD replacement packs need their logical UV size halved back down.
        if "/generated/mm6-install/bitmaps/" in source_path_text:
            effective_bitmap_uv_scale = 1.0
        # The mod's extra hd.bitmaps.lwd replacements already use their final
        # logical size, unlike the main hd.bitmaps.lod pack that MM6HD halves
        # back to classic UV space at runtime.
        elif "/converted/mod-additional/hd_bitmaps_lwd/" in source_path_text:
            effective_bitmap_uv_scale = 1.0

    if kind == "bitmap" and width > 0 and height > 0 and effective_bitmap_uv_scale > 0:
        uv_width = max(1, int(round(width / effective_bitmap_uv_scale)))
        uv_height = max(1, int(round(height / effective_bitmap_uv_scale)))
    else:
        uv_width = width
        uv_height = height

    info = TextureInfo(
        name=name,
        source_path=source_path,
        width=width,
        height=height,
        uv_width=uv_width,
        uv_height=uv_height,
        kind=kind,
    )
    manifests[key] = info
    return info


def resolve_existing_texture_name(candidates: list[str], lookup: dict[str, Path]) -> str:
    seen: set[str] = set()
    fallback = ""

    for candidate in candidates:
        if not candidate:
            continue

        lowered = candidate.lower()
        if lowered in seen:
            continue
        seen.add(lowered)

        if not fallback:
            fallback = candidate
        if resolve_image(candidate, lookup) is not None:
            return candidate

    return fallback


def build_environment_section(
    map_name: str,
    sky_texture: str,
    bitmap_lookup: dict[str, Path],
    texture_manifests: dict[str, TextureInfo],
    bitmap_uv_scale: float,
) -> dict[str, Any]:
    primary_sky_name = resolve_existing_texture_name(
        [sky_texture] + list(DEFAULT_DAY_SKY_TEXTURES),
        bitmap_lookup,
    )

    alternate_candidates = [name for name in DEFAULT_DAY_SKY_TEXTURES if name.lower() != primary_sky_name.lower()]
    alternate_sky_name = resolve_existing_texture_name(list(alternate_candidates), bitmap_lookup)
    snow_sky_name = resolve_existing_texture_name([DEFAULT_SNOW_SKY_TEXTURE], bitmap_lookup)

    if primary_sky_name:
        register_texture(primary_sky_name, "bitmap", bitmap_lookup, texture_manifests, bitmap_uv_scale)
    if alternate_sky_name:
        register_texture(alternate_sky_name, "bitmap", bitmap_lookup, texture_manifests, bitmap_uv_scale)
    if snow_sky_name:
        register_texture(snow_sky_name, "bitmap", bitmap_lookup, texture_manifests, bitmap_uv_scale)

    return {
        "mapKey": map_name.lower(),
        "daySkyTextureName": primary_sky_name,
        "alternateDaySkyTextureName": alternate_sky_name,
        "snowSkyTextureName": snow_sky_name,
        "startHour": 12.0,
        "timeScaleHoursPerRealSecond": DEFAULT_TIME_SCALE_HOURS_PER_REAL_SECOND,
        "startDayOfMonth": DEFAULT_START_DAY_OF_MONTH,
        "startMonth": DEFAULT_START_MONTH,
        "fogEnabled": False,
        "fogWeakDistance": 0.0,
        "fogStrongDistance": 0.0,
        "allowSnow": False,
        "sunPathYawDegrees": -32.0,
        "weatherSeed": deterministic_hash(map_name, "weather") & 0x7FFFFFFF,
        "weatherMode": "clear",
    }


def mm6_normal_to_unity(normal: list[int]) -> list[float]:
    if len(normal) < 3:
        return [0.0, 0.0, 1.0]

    x = -normal[0] / 65536.0
    z = -normal[1] / 65536.0
    length = math.hypot(x, z)
    if length <= 0.0001:
        return [0.0, 0.0, 1.0]
    return [x / length, 0.0, z / length]


def outdoor_map_code(map_name: str) -> str:
    lowered = map_name.lower()
    if len(lowered) >= 5 and lowered.startswith("out"):
        return f"{lowered[3].upper()}{lowered[4]}"
    return map_name.upper()


def is_water_texture_name(texture_name: str) -> bool:
    lowered = (texture_name or "").lower()
    return "wtr" in lowered


def terrain_cell_center_unity(cell_x: int, cell_y: int, heights: list[int]) -> list[float]:
    indices = (
        cell_y * GRID_SIZE + cell_x,
        cell_y * GRID_SIZE + cell_x + 1,
        (cell_y + 1) * GRID_SIZE + cell_x,
        (cell_y + 1) * GRID_SIZE + cell_x + 1,
    )
    average_height = sum(heights[index] for index in indices) / 4.0
    return [
        (64.0 - (cell_x + 0.5)) * CELL_SIZE,
        average_height * HEIGHT_SCALE,
        (cell_y + 0.5 - 64.0) * CELL_SIZE,
    ]


def build_water_cell_centers(
    heights: list[int],
    tile_values: list[int],
    tilesets: list[TilesetDef],
    dtile_textures: Optional[dict[int, str]],
) -> list[list[float]]:
    centers: list[list[float]] = []
    for cell_y in range(GRID_SIZE - 1):
        for cell_x in range(GRID_SIZE - 1):
            texture_name = terrain_texture_name(
                tile_values[cell_y * GRID_SIZE + cell_x],
                tilesets,
                dtile_textures,
            )
            if not is_water_texture_name(texture_name):
                continue
            centers.append(terrain_cell_center_unity(cell_x, cell_y, heights))
    return centers


def find_nearest_water_cell(target_position: list[float], water_cells: list[list[float]]) -> Optional[list[float]]:
    best_cell: Optional[list[float]] = None
    best_distance_sq: Optional[float] = None

    for water_cell in water_cells:
        distance_sq = (water_cell[0] - target_position[0]) ** 2 + (water_cell[2] - target_position[2]) ** 2
        if best_distance_sq is None or distance_sq < best_distance_sq:
            best_distance_sq = distance_sq
            best_cell = water_cell

    return best_cell


def vector_xz(origin: list[float], target: list[float]) -> list[float]:
    return [target[0] - origin[0], 0.0, target[2] - origin[2]]


def normalize_xz(vector: list[float]) -> list[float]:
    length = math.hypot(vector[0], vector[2])
    if length <= 0.0001:
        return [0.0, 0.0, 1.0]
    return [vector[0] / length, 0.0, vector[2] / length]


def average_face_center(vertices_unity: list[dict[str, float]], vertex_indices: list[int]) -> Optional[list[float]]:
    points: list[dict[str, float]] = []
    for vertex_index in vertex_indices:
        if 0 <= vertex_index < len(vertices_unity):
            points.append(vertices_unity[vertex_index])
    if not points:
        return None

    return [
        sum(point["x"] for point in points) / len(points),
        sum(point["y"] for point in points) / len(points),
        sum(point["z"] for point in points) / len(points),
    ]


def model_center_unity(model: dict[str, Any]) -> Optional[list[float]]:
    vertices_unity = model.get("verticesUnity") or []
    if not vertices_unity:
        position_unity = model.get("positionUnity")
        if isinstance(position_unity, dict):
            return [float(position_unity["x"]), float(position_unity["y"]), float(position_unity["z"])]
        return None

    return [
        (min(vertex["x"] for vertex in vertices_unity) + max(vertex["x"] for vertex in vertices_unity)) / 2.0,
        (min(vertex["y"] for vertex in vertices_unity) + max(vertex["y"] for vertex in vertices_unity)) / 2.0,
        (min(vertex["z"] for vertex in vertices_unity) + max(vertex["z"] for vertex in vertices_unity)) / 2.0,
    ]


def parse_model(blob: bytes) -> dict[str, Any]:
    return {
        "name": blob[:32].split(b"\0", 1)[0].decode("latin1"),
        "name2": blob[32:64].split(b"\0", 1)[0].decode("latin1"),
        "bits": struct.unpack_from("<I", blob, 0x40)[0],
        "vertex_count": struct.unpack_from("<I", blob, 0x44)[0],
        "face_count": struct.unpack_from("<I", blob, 0x4C)[0],
        "convex_face_count": struct.unpack_from("<h", blob, 0x50)[0],
        "node_count": struct.unpack_from("<I", blob, 0x5C)[0],
        "position": list(struct.unpack_from("<iii", blob, 0x70)),
        "bounds": list(struct.unpack_from("<iiiiii", blob, 0x7C)),
        "bounding_center": list(struct.unpack_from("<iii", blob, 0xAC)),
        "bounding_radius": struct.unpack_from("<i", blob, 0xB8)[0],
    }


def parse_face(blob: bytes, texture_name: str) -> dict[str, Any]:
    normal_x, normal_y, normal_z, normal_distance, z1, z2, z3, bits = struct.unpack_from(
        "<iiiiiiiI", blob, 0
    )
    vertex_count = blob[0x12E]
    polygon_type = blob[0x12F]
    vertex_ids = list(struct.unpack_from("<20h", blob, 0x20))[:vertex_count]
    u_list = list(struct.unpack_from("<20h", blob, 0x48))[:vertex_count]
    v_list = list(struct.unpack_from("<20h", blob, 0x70))[:vertex_count]
    bitmap_id, bitmap_u, bitmap_v = struct.unpack_from("<hhh", blob, 0x110)
    return {
        "textureName": texture_name,
        "normal": [normal_x, normal_y, normal_z],
        "normalDistance": normal_distance,
        "zCalc": [z1, z2, z3],
        "bits": bits,
        "vertexIndices": vertex_ids,
        "uList": u_list,
        "vList": v_list,
        "bitmapId": bitmap_id,
        "bitmapU": bitmap_u,
        "bitmapV": bitmap_v,
        "polygonType": polygon_type,
        "vertexCount": vertex_count,
        "eventId": struct.unpack_from("<h", blob, 0x124)[0],
        "cogId": struct.unpack_from("<h", blob, 0x122)[0],
    }


def parse_sprite(blob: bytes) -> dict[str, Any]:
    dec_list_id, bits = struct.unpack_from("<hH", blob, 0)
    x, y, z, direction = struct.unpack_from("<iiii", blob, 4)
    event_variable, event_id, trigger_radius, direction_degrees = struct.unpack_from("<hhhh", blob, 20)
    return {
        "dec_list_id": dec_list_id,
        "bits": bits,
        "position": [x, y, z],
        "direction": direction,
        "event_variable": event_variable,
        "event_id": event_id,
        "trigger_radius": trigger_radius,
        "direction_degrees": direction_degrees,
    }


def mm6_to_unity(position: list[int]) -> list[float]:
    x, y, z = position
    return [float(-x), float(z), float(-y)]


def vec3_object(values: list[float] | tuple[float, float, float]) -> dict[str, float]:
    return {"x": values[0], "y": values[1], "z": values[2]}


def vec4_object(values: list[int] | tuple[int, int, int, int]) -> dict[str, int]:
    return {
        "a": values[0],
        "b": values[1],
        "c": values[2],
        "d": values[3],
    }


def build_decoration_entry(
    sprite: dict[str, Any],
    sprite_name: str,
    dec_list: dict[int, dict[str, Any]],
    sft_groups: dict[str, dict[str, Any]],
    sprite_lookup: dict[str, Path],
    texture_manifests: dict[str, TextureInfo],
) -> Optional[dict[str, Any]]:
    if sprite["bits"] & 0x20:
        return None

    dec_entry = dec_list.get(sprite["dec_list_id"], {})
    if dec_entry.get("no_draw"):
        return None

    sft_group = dec_entry.get("sft_group", "")
    sft_group = "" if sft_group.lower() == "null" else sft_group
    sft_group_info = sft_groups.get(sft_group, {})
    first_frame = (sft_group_info.get("frames") or [{}])[0]

    candidates = [
        sprite_name,
        dec_entry.get("name", ""),
        dec_entry.get("game_name", ""),
        sft_group,
        first_frame.get("sprite_name", ""),
    ]

    texture_name = ""
    for candidate in candidates:
        if candidate and resolve_image(candidate, sprite_lookup):
            texture_name = candidate
            break

    if not texture_name and candidates:
        texture_name = next((candidate for candidate in candidates if candidate), "")
    if not texture_name:
        return None

    texture_info = register_texture(texture_name, "sprite", sprite_lookup, texture_manifests, 1.0)
    (
        animation_texture_names,
        animation_texture_info_names,
        animation_frame_durations_seconds,
        animation_frame_units,
    ) = build_sprite_animation(
        sft_group,
        sft_groups,
        sprite_lookup,
        texture_manifests,
    )

    height = dec_entry.get("height", 0)
    radius = dec_entry.get("radius", 0)
    scale = first_frame.get("scale", 0) / 65536.0 if first_frame else 1.0
    if scale <= 0:
        scale = 1.0

    render_width = int(round(texture_info.width * scale))
    if render_width <= 0:
        render_width = texture_info.width or 64

    render_height = int(round(texture_info.height * scale))
    if render_height <= 0:
        render_height = texture_info.height or render_width

    return {
        "name": sprite_name or dec_entry.get("name", ""),
        "decListId": sprite["dec_list_id"],
        "textureName": texture_name,
        "textureInfoName": texture_info.name,
        "sftGroup": sft_group,
        "positionMm": vec3_object(sprite["position"]),
        "positionUnity": vec3_object(mm6_to_unity(sprite["position"])),
        "directionDegrees": sprite["direction_degrees"],
        "bits": sprite["bits"],
        "eventId": sprite["event_id"],
        "triggerRadius": sprite["trigger_radius"],
        "width": render_width,
        "height": render_height,
        "radius": radius,
        "collisionHeight": height,
        "renderScale": scale,
        "animationTextureNames": animation_texture_names,
        "animationTextureInfoNames": animation_texture_info_names,
        "animationFrameDurationsSeconds": animation_frame_durations_seconds,
        "animationStartOffsetSeconds": animation_phase_offset_seconds(
            sprite["position"],
            animation_frame_units,
        ),
    }


def deterministic_hash(*values: Any) -> int:
    hash_value = 2166136261
    for value in values:
        text = str(value)
        for byte in text.encode("utf-8", "ignore"):
            hash_value ^= byte
            hash_value = (hash_value * 16777619) & 0xFFFFFFFF
    return hash_value


def idle_standing_duration_seconds(seed: int) -> float:
    return 2.0 + ((seed >> 10) & 1023) / 1023.0 * 2.0


def resolve_sprite_texture_name(
    preferred_name: str,
    sft_group: str,
    sft_groups: dict[str, dict[str, Any]],
    sprite_lookup: dict[str, Path],
) -> str:
    candidates: list[str] = []
    if preferred_name:
        candidates.extend([preferred_name, preferred_name + "0"])

    group_info = sft_groups.get(sft_group, {})
    for frame in group_info.get("frames") or []:
        sprite_name = frame.get("sprite_name", "")
        if sprite_name:
            candidates.extend([sprite_name, sprite_name + "0"])

    seen: set[str] = set()
    for candidate in candidates:
        if not candidate:
            continue
        lowered = candidate.lower()
        if lowered in seen:
            continue
        seen.add(lowered)
        if resolve_image(candidate, sprite_lookup):
            return candidate

    return next((candidate for candidate in candidates if candidate), preferred_name or sft_group)


def resolve_sprite_frame_texture_name(frame: dict[str, Any], sprite_lookup: dict[str, Path]) -> str:
    sprite_name = str(frame.get("sprite_name", "") or "")
    if not sprite_name:
        return ""

    if frame.get("image1"):
        candidates = [sprite_name]
    elif frame.get("images3") or frame.get("fidget"):
        candidates = [sprite_name + "0", sprite_name]
    elif len(sprite_name) < 7:
        candidates = [sprite_name + "0", sprite_name]
    else:
        candidates = [sprite_name, sprite_name + "0"]

    seen: set[str] = set()
    for candidate in candidates:
        lowered = candidate.lower()
        if lowered in seen:
            continue
        seen.add(lowered)
        if resolve_image(candidate, sprite_lookup):
            return candidate

    return next((candidate for candidate in candidates if candidate), sprite_name)


def sprite_frame_duration_seconds(frame: dict[str, Any]) -> float:
    time_units = int(frame.get("time", 0) or 0)
    return max(1, time_units) * SFT_TIME_TO_SECONDS


def build_sprite_animation(
    sft_group: str,
    sft_groups: dict[str, dict[str, Any]],
    sprite_lookup: dict[str, Path],
    texture_manifests: dict[str, TextureInfo],
) -> tuple[list[str], list[str], list[float], list[int]]:
    group_info = sft_groups.get(sft_group, {})
    frames = list(group_info.get("frames") or [])
    if len(frames) <= 1:
        return [], [], [], []

    animation_texture_names: list[str] = []
    animation_texture_info_names: list[str] = []
    animation_frame_durations_seconds: list[float] = []
    animation_frame_units: list[int] = []

    for frame in frames:
        texture_name = resolve_sprite_frame_texture_name(frame, sprite_lookup)
        if not texture_name:
            continue

        texture_info = register_texture(texture_name, "sprite", sprite_lookup, texture_manifests, 1.0)
        animation_texture_names.append(texture_name)
        animation_texture_info_names.append(texture_info.name)
        animation_frame_durations_seconds.append(sprite_frame_duration_seconds(frame))
        animation_frame_units.append(max(1, int(frame.get("time", 0) or 0)))

    if len(animation_texture_info_names) <= 1:
        return [], [], [], []

    return (
        animation_texture_names,
        animation_texture_info_names,
        animation_frame_durations_seconds,
        animation_frame_units,
    )


def animation_phase_offset_seconds(position_mm: list[int], frame_units: list[int]) -> float:
    if not position_mm or not frame_units:
        return 0.0

    total_units = sum(max(1, int(unit)) for unit in frame_units)
    if total_units <= 0:
        return 0.0

    phase_units = abs(int(position_mm[0]) + int(position_mm[1])) % total_units
    return phase_units * SFT_TIME_TO_SECONDS


def sprite_render_metrics(
    texture_name: str,
    sft_group: str,
    sft_groups: dict[str, dict[str, Any]],
    sprite_lookup: dict[str, Path],
    texture_manifests: dict[str, TextureInfo],
) -> tuple[TextureInfo, float, int, int]:
    texture_info = register_texture(texture_name, "sprite", sprite_lookup, texture_manifests, 1.0)
    group_info = sft_groups.get(sft_group, {})
    first_frame = (group_info.get("frames") or [{}])[0]

    scale = first_frame.get("scale", 0) / 65536.0 if first_frame else 1.0
    if scale <= 0:
        scale = 1.0

    render_width = int(round(texture_info.width * scale))
    if render_width <= 0:
        render_width = texture_info.width or 64

    render_height = int(round(texture_info.height * scale))
    if render_height <= 0:
        render_height = texture_info.height or render_width

    return texture_info, scale, render_width, render_height


def build_monster_animation_clip(
    preferred_texture_name: str,
    sft_group: str,
    sft_groups: dict[str, dict[str, Any]],
    sprite_lookup: dict[str, Path],
    texture_manifests: dict[str, TextureInfo],
) -> dict[str, Any]:
    texture_name = resolve_sprite_texture_name(
        preferred_texture_name,
        sft_group,
        sft_groups,
        sprite_lookup,
    )
    texture_info, scale, render_width, render_height = sprite_render_metrics(
        texture_name,
        sft_group,
        sft_groups,
        sprite_lookup,
        texture_manifests,
    )
    (
        animation_texture_names,
        animation_texture_info_names,
        animation_frame_durations_seconds,
        _animation_frame_units,
    ) = build_sprite_animation(
        sft_group,
        sft_groups,
        sprite_lookup,
        texture_manifests,
    )

    return {
        "sftGroup": sft_group,
        "textureName": texture_name,
        "textureInfoName": texture_info.name,
        "scale": scale,
        "width": render_width,
        "height": render_height,
        "animationTextureNames": animation_texture_names,
        "animationTextureInfoNames": animation_texture_info_names,
        "animationFrameDurationsSeconds": animation_frame_durations_seconds,
    }


def build_townperson_entries(
    map_name: str,
    sft_groups: dict[str, dict[str, Any]],
    sprite_lookup: dict[str, Path],
    texture_manifests: dict[str, TextureInfo],
    install_dir: Optional[Path],
) -> list[dict[str, Any]]:
    install = load_install(install_dir)
    if install is None:
        return []

    monsters_text = parse_monsters_text(read_mm6_icons_text_table(install.icons_lod, "MONSTERS.TXT"))
    monlist = parse_monlist_bin(read_mm6_icons_table(install.icons_lod, "DMONLIST.BIN"))
    monsters_by_picture, _monsters_by_picture_base = build_monster_records(monlist, monsters_text)
    monsters_by_id = {int(record["monster_id"]): record for record in monsters_by_picture.values()}

    actors = read_mm6_outdoor_actors(install, map_name)
    townsfolk: list[dict[str, Any]] = []
    for actor in actors:
        npc_id = int(actor.get("npc_id", 0))
        monster_id = int(actor.get("monster_id", 0))
        if npc_id <= 0 or monster_id <= 0:
            continue

        monster_record = monsters_by_id.get(monster_id)
        if not monster_record:
            continue

        standing_sft_group = str(monster_record.get("frames_stand", "") or "")
        if not standing_sft_group:
            continue

        clip = build_monster_animation_clip(
            standing_sft_group,
            standing_sft_group,
            sft_groups,
            sprite_lookup,
            texture_manifests,
        )

        position_mm = actor.get("position") or actor.get("start_position")
        if not position_mm:
            continue

        display_name = str(actor.get("name") or monster_record.get("display_name") or "")
        if not display_name:
            display_name = f"Townsperson_{npc_id:03d}"

        townsfolk.append(
            {
                "name": f"{display_name}_{int(actor.get('index', 0)):03d}",
                "displayName": display_name,
                "npcId": npc_id,
                "monsterId": monster_id,
                "monsterInternalName": monster_record.get("internal_name", ""),
                "sftGroup": standing_sft_group,
                "textureName": clip["textureName"],
                "textureInfoName": clip["textureInfoName"],
                "positionMm": vec3_object(position_mm),
                "positionUnity": vec3_object(mm6_to_unity(position_mm)),
                "width": clip["width"],
                "height": clip["height"],
                "renderScale": clip["scale"],
                "bodyRadius": int(actor.get("body_radius", 0) or 0),
                "bodyHeight": int(actor.get("body_height", 0) or 0),
                "direction": int(actor.get("direction", 0) or 0),
                "bits": int(actor.get("bits", 0) or 0),
                "animationTextureNames": clip["animationTextureNames"],
                "animationTextureInfoNames": clip["animationTextureInfoNames"],
                "animationFrameDurationsSeconds": clip["animationFrameDurationsSeconds"],
                "animationStartOffsetSeconds": 0.0,
            }
        )

    townsfolk.sort(key=lambda entry: (entry["npcId"], entry["name"].lower()))
    return townsfolk


def choose_weighted_variant(difficulty: int, seed: int) -> str:
    weights = MONSTER_DIFFICULTY_WEIGHTS.get(difficulty, MONSTER_DIFFICULTY_WEIGHTS[0])
    roll = seed % 100
    if roll < weights[0]:
        return "A"
    if roll < weights[0] + weights[1]:
        return "B"
    return "C"


def spawn_slot_name(index: int) -> str:
    return SPAWN_SLOT_NAMES.get(index, f"unknown_{index}")


def monster_visualization_offset(count: int, ordinal: int, seed: int) -> tuple[float, float]:
    if count <= 1:
        return 0.0, 0.0

    base_angle = ((seed % 360) / 180.0) * math.pi
    angle = base_angle + (math.tau * ordinal / count)
    radius = 72.0 if count <= 3 else 96.0
    return math.cos(angle) * radius, math.sin(angle) * radius


def build_monster_entries(
    map_name: str,
    spawn_points: list[dict[str, Any]],
    sft_groups: dict[str, dict[str, Any]],
    sprite_lookup: dict[str, Path],
    texture_manifests: dict[str, TextureInfo],
    install_dir: Optional[Path],
) -> tuple[list[dict[str, Any]], dict[str, Any]]:
    install = load_install(install_dir)
    if install is not None:
        map_stats = parse_mapstats_text(read_mm6_icons_text_table(install.icons_lod, "MapStats.txt"))
        monsters_text = parse_monsters_text(read_mm6_icons_text_table(install.icons_lod, "MONSTERS.TXT"))
        monlist = parse_monlist_bin(read_mm6_icons_table(install.icons_lod, "DMONLIST.BIN"))
        _monsters_by_picture, monsters_by_picture_base = build_monster_records(monlist, monsters_text)
        map_info = map_stats.get(map_name.lower())
        if map_info:
            monsters = build_monster_entries_from_mapstats(
                map_name,
                spawn_points,
                sft_groups,
                sprite_lookup,
                texture_manifests,
                map_info,
                monsters_by_picture_base,
            )
            return monsters, {
                "enabled": bool(monsters),
                "source": f"icons.lod MapStats.txt + DMONLIST.BIN + MONSTERS.TXT from {install.install_dir}",
                "countsAreApproximate": False,
                "groupedOutdoorVisualizationSpread": True,
                "notes": [
                    "Encounter families and counts come from the original MM6 MapStats.txt.",
                    "Variant A/B/C visuals come from DMONLIST.BIN sprite frame names.",
                    "Unity still spreads multi-monster groups slightly for readability in-scene.",
                ],
            }

    fallback = MAP_MONSTER_FALLBACKS.get(map_name.lower())
    if not fallback:
        return [], {
            "enabled": False,
            "source": "",
            "countsAreApproximate": False,
            "groupedOutdoorVisualizationSpread": False,
            "notes": [],
        }

    encounters = fallback.get("slots", {})
    monsters: list[dict[str, Any]] = []

    for spawn_index, spawn in enumerate(spawn_points):
        if int(spawn.get("kind", 0)) != 3:
            continue

        slot_name = spawn_slot_name(int(spawn.get("index", 0)))
        fixed_variant = ""
        if len(slot_name) == 3 and slot_name.startswith("m") and slot_name[-1] in {"a", "b", "c"}:
            slot_key = slot_name[:2]
            fixed_variant = slot_name[-1].upper()
        else:
            slot_key = slot_name

        encounter = encounters.get(slot_key)
        if not encounter:
            continue

        variants = encounter.get("variants", {})
        if fixed_variant:
            count = 1
        else:
            low, high = encounter.get("countRange", [1, 1])
            low = int(low)
            high = int(high)
            if high < low:
                high = low
            count_seed = deterministic_hash(map_name, spawn_index, slot_name, "count")
            count = low + (count_seed % (high - low + 1))

        for monster_ordinal in range(count):
            if fixed_variant:
                variant_key = fixed_variant
            else:
                variant_seed = deterministic_hash(map_name, spawn_index, slot_name, monster_ordinal)
                variant_key = choose_weighted_variant(int(encounter.get("difficulty", 0)), variant_seed)

            variant = variants.get(variant_key)
            if not variant:
                continue

            standing_sft_group = variant.get("standingSftGroup", variant.get("sftGroup", ""))
            standing_clip = build_monster_animation_clip(
                variant.get("standingTextureName", variant.get("textureName", "")),
                standing_sft_group,
                sft_groups,
                sprite_lookup,
                texture_manifests,
            )
            idle_sft_group = str(variant.get("idleSftGroup", "") or standing_sft_group)
            idle_clip = build_monster_animation_clip(
                variant.get("idleTextureName", standing_clip["textureName"]),
                idle_sft_group,
                sft_groups,
                sprite_lookup,
                texture_manifests,
            )
            walking_sft_group = variant.get("walkingSftGroup", standing_sft_group)
            walking_clip = build_monster_animation_clip(
                variant.get("walkingTextureName", standing_clip["textureName"]),
                walking_sft_group,
                sft_groups,
                sprite_lookup,
                texture_manifests,
            )

            offset_seed = deterministic_hash(map_name, spawn_index, slot_name, "spread")
            offset_x, offset_z = monster_visualization_offset(count, monster_ordinal, offset_seed)
            spawn_position_unity = spawn["positionUnity"]

            monsters.append(
                {
                    "name": f"{variant.get('name', slot_name)}_{spawn_index:03d}_{monster_ordinal:02d}",
                    "displayName": variant.get("name", slot_name),
                    "slotName": slot_name,
                    "slotKey": slot_key,
                    "variant": variant_key,
                    "sourceSpawnIndex": spawn_index,
                    "textureName": standing_clip["textureName"],
                    "textureInfoName": standing_clip["textureInfoName"],
                    "sftGroup": standing_sft_group,
                    "positionMm": dict(spawn["positionMm"]),
                    "spawnPositionUnity": dict(spawn_position_unity),
                    "positionUnity": {
                        "x": float(spawn_position_unity["x"] + offset_x),
                        "y": float(spawn_position_unity["y"]),
                        "z": float(spawn_position_unity["z"] + offset_z),
                    },
                    "width": standing_clip["width"],
                    "height": standing_clip["height"],
                    "renderScale": standing_clip["scale"],
                    "bits": spawn.get("bits", 0),
                    "approximateGroupPlacement": count > 1,
                    "animationTextureNames": standing_clip["animationTextureNames"],
                    "animationTextureInfoNames": standing_clip["animationTextureInfoNames"],
                    "animationFrameDurationsSeconds": standing_clip["animationFrameDurationsSeconds"],
                    "idleSftGroup": idle_clip["sftGroup"],
                    "idleTextureName": idle_clip["textureName"],
                    "idleTextureInfoName": idle_clip["textureInfoName"],
                    "idleAnimationTextureNames": idle_clip["animationTextureNames"],
                    "idleAnimationTextureInfoNames": idle_clip["animationTextureInfoNames"],
                    "idleAnimationFrameDurationsSeconds": idle_clip["animationFrameDurationsSeconds"],
                    "animationStartOffsetSeconds": 0.0,
                    "standingStateDurationSeconds": idle_standing_duration_seconds(
                        deterministic_hash(map_name, spawn_index, slot_name, monster_ordinal, "stand")
                    ),
                    "walkingSftGroup": walking_clip["sftGroup"],
                    "walkingTextureName": walking_clip["textureName"],
                    "walkingTextureInfoName": walking_clip["textureInfoName"],
                    "walkingAnimationTextureNames": walking_clip["animationTextureNames"],
                    "walkingAnimationTextureInfoNames": walking_clip["animationTextureInfoNames"],
                    "walkingAnimationFrameDurationsSeconds": walking_clip["animationFrameDurationsSeconds"],
                    "moveSpeed": float(variant.get("moveSpeed", encounter.get("moveSpeed", 384.0)) or 384.0),
                    "activationDistance": float(
                        variant.get("activationDistance", encounter.get("activationDistance", 5120.0)) or 5120.0
                    ),
                    "loseInterestDistance": float(
                        variant.get("loseInterestDistance", encounter.get("loseInterestDistance", 7680.0)) or 7680.0
                    ),
                    "stopDistance": float(
                        variant.get("stopDistance", encounter.get("stopDistance", 384.0)) or 384.0
                    ),
                }
            )

    info = {
        "enabled": bool(monsters),
        "source": fallback.get("source", ""),
        "countsAreApproximate": bool(fallback.get("countsAreApproximate")),
        "groupedOutdoorVisualizationSpread": True,
        "notes": [
            "Outdoor MM6/OpenEnroth spawn points use encounter slots like m1 and m2, not explicit per-monster records.",
            "OpenEnroth currently leaves outdoor monsters stacked on the exact spawn point; Unity spreads multi-monster groups slightly so they remain readable in-scene.",
        ],
    }
    return monsters, info


def build_monster_entries_from_mapstats(
    map_name: str,
    spawn_points: list[dict[str, Any]],
    sft_groups: dict[str, dict[str, Any]],
    sprite_lookup: dict[str, Path],
    texture_manifests: dict[str, TextureInfo],
    map_info: dict[str, Any],
    monsters_by_picture_base: dict[str, dict[str, dict[str, Any]]],
) -> list[dict[str, Any]]:
    encounters = map_info.get("slots", {})
    monsters: list[dict[str, Any]] = []

    for spawn_index, spawn in enumerate(spawn_points):
        if int(spawn.get("kind", 0)) != 3:
            continue

        slot_name = spawn_slot_name(int(spawn.get("index", 0)))
        fixed_variant = ""
        if len(slot_name) == 3 and slot_name.startswith("m") and slot_name[-1] in {"a", "b", "c"}:
            slot_key = slot_name[:2]
            fixed_variant = slot_name[-1].upper()
        else:
            slot_key = slot_name

        encounter = encounters.get(slot_key)
        if not encounter:
            continue

        variants = monsters_by_picture_base.get(str(encounter.get("pictureBase", "")).lower(), {})
        if not variants:
            continue

        low, high = encounter.get("countRange", [1, 1])
        low = int(low or 1)
        high = int(high or low)
        if high < low:
            high = low

        if fixed_variant:
            count = 1
        else:
            count_seed = deterministic_hash(map_name, spawn_index, slot_name, "count")
            count = low + (count_seed % (high - low + 1))

        for monster_ordinal in range(count):
            if fixed_variant:
                variant_key = fixed_variant
            else:
                variant_seed = deterministic_hash(map_name, spawn_index, slot_name, monster_ordinal)
                variant_key = choose_weighted_variant(int(encounter.get("difficulty", 0)), variant_seed)

            variant = variants.get(variant_key)
            if not variant:
                continue

            standing_sft_group = str(variant.get("frames_stand", "") or "")
            standing_clip = build_monster_animation_clip(
                standing_sft_group,
                standing_sft_group,
                sft_groups,
                sprite_lookup,
                texture_manifests,
            )
            idle_sft_group = str(variant.get("frames_fidget", "") or standing_sft_group)
            idle_clip = build_monster_animation_clip(
                idle_sft_group,
                idle_sft_group,
                sft_groups,
                sprite_lookup,
                texture_manifests,
            )

            offset_seed = deterministic_hash(map_name, spawn_index, slot_name, "spread")
            offset_x, offset_z = monster_visualization_offset(count, monster_ordinal, offset_seed)
            spawn_position_unity = spawn["positionUnity"]

            monsters.append(
                {
                    "name": f"{variant.get('internal_name', slot_name)}_{spawn_index:03d}_{monster_ordinal:02d}",
                    "displayName": variant.get("display_name", encounter.get("displayName", slot_name)),
                    "slotName": slot_name,
                    "slotKey": slot_key,
                    "variant": variant_key,
                    "sourceSpawnIndex": spawn_index,
                    "monsterId": int(variant.get("monster_id", 0)),
                    "picture": variant.get("picture", ""),
                    "textureName": standing_clip["textureName"],
                    "textureInfoName": standing_clip["textureInfoName"],
                    "sftGroup": standing_sft_group,
                    "positionMm": dict(spawn["positionMm"]),
                    "spawnPositionUnity": dict(spawn_position_unity),
                    "positionUnity": {
                        "x": float(spawn_position_unity["x"] + offset_x),
                        "y": float(spawn_position_unity["y"]),
                        "z": float(spawn_position_unity["z"] + offset_z),
                    },
                    "width": standing_clip["width"],
                    "height": standing_clip["height"],
                    "renderScale": standing_clip["scale"],
                    "bits": spawn.get("bits", 0),
                    "approximateGroupPlacement": count > 1,
                    "animationTextureNames": standing_clip["animationTextureNames"],
                    "animationTextureInfoNames": standing_clip["animationTextureInfoNames"],
                    "animationFrameDurationsSeconds": standing_clip["animationFrameDurationsSeconds"],
                    "animationStartOffsetSeconds": 0.0,
                    "idleSftGroup": idle_clip["sftGroup"],
                    "idleTextureName": idle_clip["textureName"],
                    "idleTextureInfoName": idle_clip["textureInfoName"],
                    "idleAnimationTextureNames": idle_clip["animationTextureNames"],
                    "idleAnimationTextureInfoNames": idle_clip["animationTextureInfoNames"],
                    "idleAnimationFrameDurationsSeconds": idle_clip["animationFrameDurationsSeconds"],
                    "standingStateDurationSeconds": idle_standing_duration_seconds(
                        deterministic_hash(map_name, spawn_index, slot_name, monster_ordinal, "stand")
                    ),
                    "collisionRadius": int(variant.get("radius", 0)),
                    "collisionHeight": int(variant.get("height", 0)),
                    "moveSpeed": float(variant.get("velocity", 0)),
                    "activationDistance": 0.0,
                    "loseInterestDistance": 0.0,
                    "stopDistance": 0.0,
                }
            )

    return monsters


def export_map(args: argparse.Namespace) -> dict[str, Any]:
    bitmap_dirs = args.bitmap_dirs or DEFAULT_BITMAP_DIRS
    sprite_dirs = args.sprite_dirs or DEFAULT_SPRITE_DIRS
    bitmap_lookup = build_image_lookup(bitmap_dirs)
    sprite_lookup = build_image_lookup(sprite_dirs)
    dec_list, sft_groups = load_decoration_tables(args.dec_list, args.sft, args.install_dir)
    dtile_textures = load_dtile_textures(args.dtile_bin, args.install_dir)

    reader = Reader(args.map.read_bytes())
    map_name = args.map.stem

    name = reader.cstr(32)
    file_name = reader.cstr(32)
    version = reader.cstr(31)
    reader.read(1)
    sky_texture = reader.cstr(32)
    ground_texture = reader.cstr(32)
    tilesets = [TilesetDef(reader.i16(), reader.i16()) for _ in range(4)]
    height_map = list(reader.read(GRID_SIZE * GRID_SIZE))
    tile_map = list(reader.read(GRID_SIZE * GRID_SIZE))
    attribute_map = list(reader.read(GRID_SIZE * GRID_SIZE))

    if version != MM6_OUTDOOR_VERSION:
        raise ValueError(
            f"{args.map} has version {version!r}, expected {MM6_OUTDOOR_VERSION!r}; "
            "this exporter currently targets MM6 v1.11 outdoor maps only"
        )

    texture_manifests: dict[str, TextureInfo] = {}

    model_count = reader.u32()
    models = [parse_model(reader.read(MODEL_SIZE)) for _ in range(model_count)]

    exported_models: list[dict[str, Any]] = []
    bitmap_names: set[str] = set()

    for model_index, model in enumerate(models):
        vertices = [list(struct.unpack("<iii", reader.read(12))) for _ in range(model["vertex_count"])]
        face_blobs = [reader.read(MODEL_FACE_SIZE) for _ in range(model["face_count"])]
        ordering = [reader.i16() for _ in range(model["face_count"])]
        bsp_nodes = [list(struct.unpack("<hhhh", reader.read(8))) for _ in range(model["node_count"])]
        face_texture_names = [reader.cstr(10) for _ in range(model["face_count"])]

        faces = []
        for face_blob, texture_name in zip(face_blobs, face_texture_names):
            if texture_name:
                register_texture(
                    texture_name,
                    "bitmap",
                    bitmap_lookup,
                    texture_manifests,
                    args.bitmap_uv_scale,
                )
                bitmap_names.add(texture_name)
            faces.append(parse_face(face_blob, texture_name))

        exported_models.append(
            {
                "index": model_index,
                "name": model["name"],
                "objectName": model["name2"],
                "bits": model["bits"],
                "positionMm": vec3_object(model["position"]),
                "positionUnity": vec3_object(mm6_to_unity(model["position"])),
                "boundsMm": model["bounds"],
                "boundingCenterMm": vec3_object(model["bounding_center"]),
                "boundingRadius": model["bounding_radius"],
                "verticesMm": [vec3_object(vertex) for vertex in vertices],
                "verticesUnity": [vec3_object(mm6_to_unity(vertex)) for vertex in vertices],
                "ordering": ordering,
                "bspNodes": [vec4_object(node) for node in bsp_nodes],
                "faces": faces,
            }
        )

    sprite_count = reader.u32()
    sprites = [parse_sprite(reader.read(SPRITE_SIZE)) for _ in range(sprite_count)]
    sprite_names = [reader.cstr(32) for _ in range(sprite_count)]

    id_list_count = reader.u32()
    id_list = [struct.unpack("<H", reader.read(2))[0] for _ in range(id_list_count)]
    id_offsets = list(struct.unpack("<" + "i" * (GRID_SIZE * GRID_SIZE), reader.read(GRID_SIZE * GRID_SIZE * 4)))

    spawn_count = reader.u32()
    spawn_points = []
    for _ in range(spawn_count):
        x, y, z, radius, kind, index, bits = struct.unpack("<iiihhhH", reader.read(SPAWN_SIZE))
        spawn_points.append(
            {
                "positionMm": vec3_object([x, y, z]),
                "positionUnity": vec3_object(mm6_to_unity([x, y, z])),
                "radius": radius,
                "kind": kind,
                "index": index,
                "bits": bits,
            }
        )

    if reader.offset != len(reader.data):
        raise ValueError(
            f"{args.map} was not fully consumed: parsed {reader.offset} bytes of {len(reader.data)}"
        )

    terrain_texture_names: list[str] = []
    terrain_texture_index_by_name: dict[str, int] = {}
    terrain_cells: list[int] = []

    for y in range(GRID_SIZE - 1):
        for x in range(GRID_SIZE - 1):
            local_tile_id = tile_map[y * GRID_SIZE + x]
            texture_name = terrain_texture_name(local_tile_id, tilesets, dtile_textures)
            if texture_name not in terrain_texture_index_by_name:
                terrain_texture_index_by_name[texture_name] = len(terrain_texture_names)
                terrain_texture_names.append(texture_name)
                register_texture(
                    texture_name,
                    "bitmap",
                    bitmap_lookup,
                    texture_manifests,
                    args.bitmap_uv_scale,
                )
                bitmap_names.add(texture_name)
            terrain_cells.append(terrain_texture_index_by_name[texture_name])

    decorations = []
    for sprite, sprite_name in zip(sprites, sprite_names):
        decoration = build_decoration_entry(
            sprite,
            sprite_name,
            dec_list,
            sft_groups,
            sprite_lookup,
            texture_manifests,
        )
        if decoration is not None:
            decorations.append(decoration)

    townsfolk = build_townperson_entries(
        map_name,
        sft_groups,
        sprite_lookup,
        texture_manifests,
        args.install_dir,
    )

    monsters, monster_import = build_monster_entries(
        map_name,
        spawn_points,
        sft_groups,
        sprite_lookup,
        texture_manifests,
        args.install_dir,
    )
    environment = build_environment_section(
        map_name,
        sky_texture,
        bitmap_lookup,
        texture_manifests,
        args.bitmap_uv_scale,
    )

    animated_decoration_groups = sorted(
        {
            decoration["sftGroup"]
            for decoration in decorations
            if decoration.get("animationTextureInfoNames")
        }
    )
    animated_monster_groups = sorted(
        {
            group_name
            for monster in monsters
            for group_name in [monster.get("sftGroup", ""), monster.get("idleSftGroup", "")]
            if group_name and (
                monster.get("animationTextureInfoNames")
                or monster.get("idleAnimationTextureInfoNames")
            )
        }
    )

    sorted_textures = sorted(
        texture_manifests.values(),
        key=lambda info: (info.kind, info.name.lower()),
    )

    package = {
        "mapName": map_name,
        "sourceMapPath": str(args.map.resolve()),
        "header": {
            "name": name,
            "fileName": file_name,
            "version": version,
            "skyTexture": sky_texture,
            "groundTexture": ground_texture,
        },
        "terrain": {
            "gridSize": GRID_SIZE,
            "cellSize": CELL_SIZE,
            "heightScale": HEIGHT_SCALE,
            "heights": height_map,
            "tileValues": tile_map,
            "attributeMap": attribute_map,
            "tilesets": [
                {
                    "group": tileset.group,
                    "groupName": mm6_tileset_name(tileset.group),
                    "offset": tileset.offset,
                    "baseTexture": BASE_TEXTURE_BY_TILESET_GROUP.get(tileset.group, "DirtTyl"),
                }
                for tileset in tilesets
            ],
            "textureNames": terrain_texture_names,
            "cellTextureIndices": terrain_cells,
            "textureMode": "exact_dtile" if dtile_textures is not None else "tileset_base_fallback",
            "approximateTexturing": dtile_textures is None,
            "approximationReason": (
                ""
                if dtile_textures is not None
                else "dtile.bin was not provided, so terrain uses one representative texture per tileset group"
            ),
        },
        "models": exported_models,
        "decorations": decorations,
        "townsfolk": townsfolk,
        "monsters": monsters,
        "environment": environment,
        "monsterImport": monster_import,
        "objectIdList": id_list,
        "objectIdOffsets": id_offsets,
        "spawnPoints": spawn_points,
        "textures": {
            "bitmaps": [
                {
                    "name": info.name,
                    "sourcePath": str(info.source_path) if info.source_path is not None else "",
                    "width": info.width,
                    "height": info.height,
                    "uvWidth": info.uv_width,
                    "uvHeight": info.uv_height,
                    "found": info.source_path is not None,
                }
                for info in sorted_textures
                if info.kind == "bitmap"
            ],
            "sprites": [
                {
                    "name": info.name,
                    "sourcePath": str(info.source_path) if info.source_path is not None else "",
                    "width": info.width,
                    "height": info.height,
                    "uvWidth": info.uv_width,
                    "uvHeight": info.uv_height,
                    "found": info.source_path is not None,
                }
                for info in sorted_textures
                if info.kind == "sprite"
            ],
        },
        "summary": {
            "modelCount": len(exported_models),
            "decorationCount": len(decorations),
            "townspersonCount": len(townsfolk),
            "monsterCount": len(monsters),
            "spawnPointCount": len(spawn_points),
            "animatedDecorationCount": sum(1 for decoration in decorations if decoration.get("animationTextureInfoNames")),
            "animatedMonsterCount": sum(
                1
                for monster in monsters
                if monster.get("animationTextureInfoNames") or monster.get("idleAnimationTextureInfoNames")
            ),
            "animatedDecorationGroups": animated_decoration_groups,
            "animatedMonsterGroups": animated_monster_groups,
            "confirmedAnimatedBitmapTextures": [],
            "bitmapAnimationTableFound": False,
            "bitmapTextureCount": sum(1 for info in sorted_textures if info.kind == "bitmap"),
            "spriteTextureCount": sum(1 for info in sorted_textures if info.kind == "sprite"),
        },
    }

    return package


def main() -> int:
    args = parse_args()
    package = export_map(args)

    args.output.mkdir(parents=True, exist_ok=True)
    json_path = args.output / "map.json"
    json_path.write_text(json.dumps(package, indent=2), encoding="utf-8")

    print(
        f"Exported {package['mapName']} to {json_path} "
        f"({package['summary']['modelCount']} models, "
        f"{package['summary']['decorationCount']} decorations, "
        f"{package['summary']['townspersonCount']} townsfolk, "
        f"{package['summary']['bitmapTextureCount']} bitmap textures, "
        f"{package['summary']['spriteTextureCount']} sprite textures)"
    )
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(exc, file=sys.stderr)
        raise SystemExit(1)
