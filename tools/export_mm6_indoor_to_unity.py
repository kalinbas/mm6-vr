#!/usr/bin/env python3

from __future__ import annotations

import argparse
import json
import math
import struct
from pathlib import Path
from typing import Any, Optional

from export_mm6_outdoor_to_unity import (
    DEFAULT_BITMAP_DIRS,
    DEFAULT_BITMAP_UV_SCALE,
    DEFAULT_DEC_LIST,
    DEFAULT_INSTALL_DIR,
    DEFAULT_SFT,
    DEFAULT_SPRITE_DIRS,
    Reader,
    TextureInfo,
    animation_phase_offset_seconds,
    build_monster_animation_clip,
    build_image_lookup,
    build_sprite_animation,
    deterministic_hash,
    idle_standing_duration_seconds,
    load_decoration_tables,
    register_texture,
    resolve_image,
    vec3_object,
)


MM6_INDOOR_VERSION = "MM6 Indoor v0.2"

FACE_IS_PORTAL = 0x00000001
FACE_IS_FLUID = 0x00000010
FACE_IS_INVISIBLE = 0x00002000
FACE_ANIMATED = 0x00004000
FACE_INDOOR_SKY = 0x00400000
FACE_CLICKABLE = 0x02000000
FACE_ETHEREAL = 0x20000000

POLYGON_FLOOR = 0x03

BLV_HEADER_SIZE = 0x88
BLV_FACE_SIZE = 0x50
BLV_FACE_EXTRA_SIZE = 0x24
BLV_SECTOR_SIZE = 0x74
BLV_LIGHT_SIZE = 0x0C
BLV_BSP_NODE_SIZE = 0x08
BLV_DECORATION_SIZE = 0x1C
BLV_SPAWN_POINT_SIZE = 0x14
BLV_MAP_OUTLINE_SIZE = 0x0C

INDOOR_MONSTER_SPAWN_KIND = 2
INDOOR_MONSTER_IDLE_VARIANTS: tuple[dict[str, str], ...] = (
    {"name": "Rat", "standingSftGroup": "ratsta", "standingTextureName": "ratsta0", "idleSftGroup": "ratfia"},
    {"name": "Bat", "standingSftGroup": "batwa", "standingTextureName": "", "idleSftGroup": "batwa"},
    {"name": "Spider", "standingSftGroup": "spi1sta", "standingTextureName": "spi1sta0", "idleSftGroup": "spi1fia"},
    {"name": "Skeleton", "standingSftGroup": "ske1st", "standingTextureName": "skesta0", "idleSftGroup": "ske1fi"},
    {"name": "Ooze", "standingSftGroup": "oozsta", "standingTextureName": "oozwaa0", "idleSftGroup": "oozfia"},
    {"name": "Apprentice Mage", "standingSftGroup": "wd1fly", "standingTextureName": "wdmflya0", "idleSftGroup": "wd1fly"},
    {"name": "Goblin", "standingSftGroup": "gobsta", "standingTextureName": "gobsta0", "idleSftGroup": "gobfia"},
)


def mm6_to_unity(position: list[int]) -> list[float]:
    x, y, z = position
    return [float(x), float(z), float(-y)]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Export an MM6 indoor BLV dungeon map into a Unity-friendly JSON package."
    )
    parser.add_argument(
        "--map",
        type=Path,
        required=True,
        help="Path to a decompressed MM6 indoor .blv file",
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
        "--bitmap-uv-scale",
        type=float,
        default=DEFAULT_BITMAP_UV_SCALE,
        help="Divisor used to convert bitmap PNG dimensions into MM6 logical UV dimensions",
    )
    return parser.parse_args()


def read_prefixed_items(reader: Reader, size: int) -> list[bytes]:
    count = reader.u32()
    return [reader.read(size) for _ in range(count)]


def decode_fixed_string(blob: bytes) -> str:
    return blob.split(b"\0", 1)[0].decode("latin1")


def parse_blv_header(blob: bytes) -> dict[str, int]:
    if len(blob) != BLV_HEADER_SIZE:
        raise ValueError(f"Expected {BLV_HEADER_SIZE} bytes for BLV header, got {len(blob)}")
    return {
        "faceDataSizeBytes": struct.unpack_from("<i", blob, 0x68)[0],
        "sectorDataSizeBytes": struct.unpack_from("<i", blob, 0x6C)[0],
        "sectorLightDataSizeBytes": struct.unpack_from("<i", blob, 0x70)[0],
        "doorsDataSizeBytes": struct.unpack_from("<i", blob, 0x74)[0],
    }


def parse_vec3s(blob: bytes) -> list[int]:
    return list(struct.unpack("<hhh", blob))


def parse_bboxs(blob: bytes) -> dict[str, int]:
    x1, x2, y1, y2, z1, z2 = struct.unpack("<hhhhhh", blob)
    return {
        "x1": x1,
        "x2": x2,
        "y1": y1,
        "y2": y2,
        "z1": z1,
        "z2": z2,
    }


def parse_face(blob: bytes) -> dict[str, Any]:
    normal_x_raw, normal_y_raw, normal_z_raw, normal_dist_raw = struct.unpack_from("<iiii", blob, 0x00)
    attributes = struct.unpack_from("<I", blob, 0x1C)[0]
    face_extra_id = struct.unpack_from("<H", blob, 0x38)[0]
    bitmap_id = struct.unpack_from("<h", blob, 0x3A)[0]
    sector_id = struct.unpack_from("<h", blob, 0x3C)[0]
    back_sector_id = struct.unpack_from("<h", blob, 0x3E)[0]
    polygon_type = blob[0x4C]
    vertex_count = blob[0x4D]
    return {
        "normal": [
            normal_x_raw / 65536.0,
            normal_y_raw / 65536.0,
            normal_z_raw / 65536.0,
        ],
        "normalDistance": normal_dist_raw / 65536.0,
        "attributes": attributes,
        "faceExtraId": face_extra_id,
        "bitmapId": bitmap_id,
        "sectorId": sector_id,
        "backSectorId": back_sector_id,
        "boundingBoxMm": {
            "x1": struct.unpack_from("<h", blob, 0x40)[0],
            "x2": struct.unpack_from("<h", blob, 0x42)[0],
            "y1": struct.unpack_from("<h", blob, 0x44)[0],
            "y2": struct.unpack_from("<h", blob, 0x46)[0],
            "z1": struct.unpack_from("<h", blob, 0x48)[0],
            "z2": struct.unpack_from("<h", blob, 0x4A)[0],
        },
        "polygonType": polygon_type,
        "vertexCount": vertex_count,
    }


def parse_face_data(face: dict[str, Any], face_data: list[int], offset: int) -> tuple[dict[str, Any], int]:
    vertex_count = int(face.get("vertexCount", 0) or 0)
    if vertex_count < 0:
        raise ValueError(f"Invalid indoor face vertex count: {vertex_count}")

    def slice_values(start: int) -> list[int]:
        end = start + vertex_count
        return [int(value) for value in face_data[start:end]]

    face["vertexIndices"] = slice_values(offset)
    offset += vertex_count + 1
    offset += vertex_count + 1
    offset += vertex_count + 1
    offset += vertex_count + 1
    face["uList"] = slice_values(offset)
    offset += vertex_count + 1
    face["vList"] = slice_values(offset)
    offset += vertex_count + 1
    return face, offset


def parse_face_extra(blob: bytes) -> dict[str, Any]:
    return {
        "faceId": struct.unpack_from("<h", blob, 0x0C)[0],
        "additionalBitmapId": struct.unpack_from("<H", blob, 0x0E)[0],
        "textureDeltaU": struct.unpack_from("<h", blob, 0x14)[0],
        "textureDeltaV": struct.unpack_from("<h", blob, 0x16)[0],
        "cogNumber": struct.unpack_from("<h", blob, 0x18)[0],
        "eventId": struct.unpack_from("<H", blob, 0x1A)[0],
    }


def parse_sector(blob: bytes) -> dict[str, Any]:
    return {
        "flags": struct.unpack_from("<i", blob, 0x00)[0],
        "numFloors": struct.unpack_from("<H", blob, 0x08)[0],
        "numWalls": struct.unpack_from("<H", blob, 0x10)[0],
        "numCeilings": struct.unpack_from("<H", blob, 0x18)[0],
        "numFluids": struct.unpack_from("<H", blob, 0x20)[0],
        "numPortals": struct.unpack_from("<h", blob, 0x28)[0],
        "numFaces": struct.unpack_from("<H", blob, 0x30)[0],
        "numNonBspFaces": struct.unpack_from("<H", blob, 0x2E)[0],
        "numCylinderFaces": struct.unpack_from("<H", blob, 0x34)[0],
        "numCogs": struct.unpack_from("<H", blob, 0x40)[0],
        "numDecorations": struct.unpack_from("<H", blob, 0x48)[0],
        "numMarkers": struct.unpack_from("<H", blob, 0x50)[0],
        "numLights": struct.unpack_from("<H", blob, 0x58)[0],
        "waterLevel": struct.unpack_from("<h", blob, 0x5C)[0],
        "mistLevel": struct.unpack_from("<h", blob, 0x5E)[0],
        "lightDistanceMultiplier": struct.unpack_from("<h", blob, 0x60)[0],
        "minAmbientLightLevel": struct.unpack_from("<h", blob, 0x62)[0],
        "firstBspNode": struct.unpack_from("<h", blob, 0x64)[0],
        "exitTag": blob[0x67],
        "boundingBoxMm": parse_bboxs(blob[0x68:0x74]),
    }


def parse_sector_data(sector: dict[str, Any], values: list[int], offset: int) -> tuple[dict[str, Any], int]:
    def take(count: int) -> list[int]:
        nonlocal offset
        count = max(0, count)
        result = [int(value) for value in values[offset : offset + count]]
        offset += count
        return result

    sector["floorIds"] = take(sector["numFloors"])
    sector["wallIds"] = take(sector["numWalls"])
    sector["ceilingIds"] = take(sector["numCeilings"])
    take(sector["numFluids"])
    sector["portalIds"] = take(sector["numPortals"])
    sector["faceIds"] = take(sector["numFaces"])
    sector["nonBspFaceIds"] = list(sector["faceIds"][: max(0, sector["numNonBspFaces"])])
    take(sector["numCogs"])
    sector["decorationIds"] = take(sector["numDecorations"])
    take(sector["numMarkers"])
    return sector, offset


def parse_sector_light_data(sector: dict[str, Any], values: list[int], offset: int) -> tuple[dict[str, Any], int]:
    count = max(0, int(sector.get("numLights", 0) or 0))
    sector["lightIds"] = [int(value) for value in values[offset : offset + count]]
    offset += count
    return sector, offset


def parse_light(blob: bytes) -> dict[str, Any]:
    x, y, z = struct.unpack_from("<hhh", blob, 0x00)
    attributes = struct.unpack_from("<H", blob, 0x06)[0]
    brightness = struct.unpack_from("<h", blob, 0x08)[0]
    radius = struct.unpack_from("<h", blob, 0x0A)[0]
    return {
        "position": [x, y, z],
        "radius": radius,
        "red": 255,
        "green": 255,
        "blue": 255,
        "type": 0,
        "attributes": attributes,
        "brightness": brightness,
    }


def parse_decoration(blob: bytes) -> dict[str, Any]:
    return {
        "decListId": struct.unpack_from("<H", blob, 0x00)[0],
        "flags": struct.unpack_from("<H", blob, 0x02)[0],
        "position": list(struct.unpack_from("<iii", blob, 0x04)),
        "yaw": struct.unpack_from("<i", blob, 0x10)[0],
        "cog": 0,
        "eventId": struct.unpack_from("<H", blob, 0x16)[0],
        "triggerRadius": struct.unpack_from("<H", blob, 0x18)[0],
        "yawDegrees": struct.unpack_from("<h", blob, 0x1A)[0],
        "eventVarId": struct.unpack_from("<h", blob, 0x14)[0],
    }


def parse_spawn_point(blob: bytes) -> dict[str, Any]:
    x, y, z = struct.unpack_from("<iii", blob, 0x00)
    radius = struct.unpack_from("<H", blob, 0x0C)[0]
    spawn_type = struct.unpack_from("<H", blob, 0x0E)[0]
    index = struct.unpack_from("<H", blob, 0x10)[0]
    attributes = struct.unpack_from("<H", blob, 0x12)[0]
    return {
        "position": [x, y, z],
        "radius": radius,
        "type": spawn_type,
        "index": index,
        "attributes": attributes,
        "group": 0,
    }


def parse_map_outline(blob: bytes) -> dict[str, int]:
    v1, v2, f1, f2, z, flags = struct.unpack("<HHHHhH", blob)
    return {
        "vertex1Id": v1,
        "vertex2Id": v2,
        "face1Id": f1,
        "face2Id": f2,
        "z": z,
        "flags": flags,
    }


def face_flags(attributes: int) -> dict[str, bool]:
    return {
        "isPortal": bool(attributes & FACE_IS_PORTAL),
        "isFluid": bool(attributes & FACE_IS_FLUID),
        "isInvisible": bool(attributes & FACE_IS_INVISIBLE),
        "isAnimated": bool(attributes & FACE_ANIMATED),
        "isIndoorSky": bool(attributes & FACE_INDOOR_SKY),
        "isClickable": bool(attributes & FACE_CLICKABLE),
        "isEthereal": bool(attributes & FACE_ETHEREAL),
    }


def resolve_indoor_dec_entry(
    decoration: dict[str, Any],
    decoration_name: str,
    dec_list: dict[int, dict[str, Any]],
) -> dict[str, Any]:
    dec_entry = dec_list.get(decoration.get("decListId", -1), {})
    if decoration_name:
        lowered_name = decoration_name.lower()
        if dec_entry.get("name", "").lower() != lowered_name:
            for candidate in dec_list.values():
                if candidate.get("name", "").lower() == lowered_name:
                    return candidate
    return dec_entry


def build_indoor_decoration_entry(
    decoration: dict[str, Any],
    decoration_name: str,
    dec_list: dict[int, dict[str, Any]],
    sft_groups: dict[str, dict[str, Any]],
    sprite_lookup: dict[str, Path],
    texture_manifests: dict[str, TextureInfo],
) -> Optional[dict[str, Any]]:
    if (decoration_name or "").strip().lower() == "party start":
        return None

    dec_entry = resolve_indoor_dec_entry(decoration, decoration_name, dec_list)
    if dec_entry.get("no_draw"):
        return None

    sft_group = dec_entry.get("sft_group", "")
    sft_group = "" if sft_group.lower() == "null" else sft_group
    sft_group_info = sft_groups.get(sft_group, {})
    first_frame = (sft_group_info.get("frames") or [{}])[0]

    candidates = [
        decoration_name,
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
        "name": decoration_name or dec_entry.get("name", ""),
        "decListId": decoration["decListId"],
        "textureName": texture_name,
        "textureInfoName": texture_info.name,
        "sftGroup": sft_group,
        "positionMm": vec3_object(decoration["position"]),
        "positionUnity": vec3_object(mm6_to_unity(decoration["position"])),
        "directionDegrees": decoration.get("yawDegrees", 0),
        "flags": decoration["flags"],
        "eventId": decoration["eventId"],
        "triggerRadius": decoration["triggerRadius"],
        "width": render_width,
        "height": render_height,
        "radius": radius,
        "collisionHeight": height,
        "renderScale": scale,
        "animationTextureNames": animation_texture_names,
        "animationTextureInfoNames": animation_texture_info_names,
        "animationFrameDurationsSeconds": animation_frame_durations_seconds,
        "animationStartOffsetSeconds": animation_phase_offset_seconds(
            decoration["position"],
            animation_frame_units,
        ),
    }


def polygon_centroid(face: dict[str, Any], vertices_unity: list[list[float]]) -> Optional[list[float]]:
    indices = face.get("vertexIndices") or []
    points: list[list[float]] = []
    for vertex_index in indices:
        if vertex_index < 0 or vertex_index >= len(vertices_unity):
            return None
        points.append(vertices_unity[vertex_index])
    if not points:
        return None

    x = sum(point[0] for point in points) / len(points)
    y = sum(point[1] for point in points) / len(points)
    z = sum(point[2] for point in points) / len(points)
    return [float(x), float(y), float(z)]


def mm6_vector_to_unity(values: list[float] | tuple[float, float, float]) -> list[float]:
    return [float(values[0]), float(values[2]), float(-values[1])]


def face_forward(face: dict[str, Any], vertices_unity: list[list[float]]) -> list[float]:
    indices = face.get("vertexIndices") or []
    if len(indices) < 2:
        return [0.0, 0.0, 1.0]

    p0 = vertices_unity[indices[0]]
    p1 = vertices_unity[indices[1]]
    dx = float(p1[0] - p0[0])
    dz = float(p1[2] - p0[2])
    length = math.hypot(dx, dz)
    if length <= 0.0001:
        return [0.0, 0.0, 1.0]
    return [dx / length, 0.0, dz / length]


def choose_player_start(
    faces: list[dict[str, Any]],
    vertices_unity: list[list[float]],
    spawn_points: list[dict[str, Any]],
) -> dict[str, Any]:
    for face in faces:
        flags = face.get("flags") or {}
        if face.get("polygonType") != POLYGON_FLOOR:
            continue
        if flags.get("isPortal") or flags.get("isInvisible"):
            continue

        position = polygon_centroid(face, vertices_unity)
        if position is None:
            continue

        return {
            "source": "floor_face",
            "faceId": face.get("index", -1),
            "sectorId": face.get("sectorId", -1),
            "positionUnity": vec3_object(position),
            "forwardUnity": vec3_object(face_forward(face, vertices_unity)),
        }

    for spawn_index, spawn in enumerate(spawn_points):
        return {
            "source": "spawn_point",
            "spawnPointIndex": spawn_index,
            "positionUnity": vec3_object(mm6_to_unity(spawn["position"])),
            "forwardUnity": {"x": 0.0, "y": 0.0, "z": 1.0},
        }

    return {
        "source": "fallback",
        "positionUnity": {"x": 0.0, "y": 128.0, "z": 0.0},
        "forwardUnity": {"x": 0.0, "y": 0.0, "z": 1.0},
    }


def build_indoor_monster_entries(
    map_name: str,
    spawn_points: list[dict[str, Any]],
    sft_groups: dict[str, dict[str, Any]],
    sprite_lookup: dict[str, Path],
    texture_manifests: dict[str, TextureInfo],
) -> tuple[list[dict[str, Any]], dict[str, Any]]:
    monsters: list[dict[str, Any]] = []

    for spawn_index, spawn in enumerate(spawn_points):
        if int(spawn.get("type", 0) or 0) != INDOOR_MONSTER_SPAWN_KIND:
            continue

        seed = deterministic_hash(
            map_name,
            spawn_index,
            spawn.get("index", 0),
            spawn.get("group", 0),
            spawn.get("position", []),
        )
        variant = INDOOR_MONSTER_IDLE_VARIANTS[seed % len(INDOOR_MONSTER_IDLE_VARIANTS)]
        standing_clip = build_monster_animation_clip(
            variant.get("standingTextureName", ""),
            variant.get("standingSftGroup", ""),
            sft_groups,
            sprite_lookup,
            texture_manifests,
        )
        idle_clip = build_monster_animation_clip(
            variant.get("idleTextureName", ""),
            variant.get("idleSftGroup", variant.get("standingSftGroup", "")),
            sft_groups,
            sprite_lookup,
            texture_manifests,
        )

        durations = [float(duration) for duration in standing_clip.get("animationFrameDurationsSeconds") or []]
        total_duration = sum(max(0.01, duration) for duration in durations)
        start_offset_seconds = 0.0
        if total_duration > 0.01:
            start_offset_seconds = (((seed >> 8) & 1023) / 1024.0) * total_duration

        monsters.append(
            {
                "name": f"{variant.get('name', 'Monster')}_{spawn_index:03d}",
                "displayName": variant.get("name", "Monster"),
                "kind": int(spawn.get("type", 0) or 0),
                "sourceSpawnIndex": spawn_index,
                "sourceSpawnGroup": int(spawn.get("group", 0) or 0),
                "textureName": standing_clip["textureName"],
                "textureInfoName": standing_clip["textureInfoName"],
                "sftGroup": standing_clip["sftGroup"],
                "positionMm": vec3_object(spawn["position"]),
                "positionUnity": vec3_object(mm6_to_unity(spawn["position"])),
                "width": standing_clip["width"],
                "height": standing_clip["height"],
                "renderScale": standing_clip["scale"],
                "radius": int(spawn.get("radius", 0) or 0),
                "bits": int(spawn.get("attributes", 0) or 0),
                "animationTextureNames": standing_clip["animationTextureNames"],
                "animationTextureInfoNames": standing_clip["animationTextureInfoNames"],
                "animationFrameDurationsSeconds": durations,
                "idleTextureName": idle_clip["textureName"],
                "idleTextureInfoName": idle_clip["textureInfoName"],
                "idleAnimationTextureNames": idle_clip["animationTextureNames"],
                "idleAnimationTextureInfoNames": idle_clip["animationTextureInfoNames"],
                "idleAnimationFrameDurationsSeconds": idle_clip["animationFrameDurationsSeconds"],
                "animationStartOffsetSeconds": start_offset_seconds,
                "standingStateDurationSeconds": idle_standing_duration_seconds(seed),
            }
        )

    info = {
        "enabled": bool(monsters),
        "source": "Indoor BLV spawn points only.",
        "countsAreApproximate": False,
        "notes": [
            "Indoor monster previews currently come from BLV spawn points with kind 2.",
            "Exact indoor monster families still depend on the paired DLV runtime state, so the current idle-only preview uses deterministic fallback monster visuals.",
        ],
    }
    return monsters, info


def main() -> None:
    args = parse_args()
    args.output.mkdir(parents=True, exist_ok=True)

    bitmap_dirs = args.bitmap_dirs or DEFAULT_BITMAP_DIRS
    sprite_dirs = args.sprite_dirs or DEFAULT_SPRITE_DIRS

    bitmap_lookup = build_image_lookup([path.resolve() for path in bitmap_dirs])
    sprite_lookup = build_image_lookup([path.resolve() for path in sprite_dirs])
    dec_list, sft_groups = load_decoration_tables(args.dec_list, args.sft, args.install_dir)

    reader = Reader(args.map.read_bytes())
    header = parse_blv_header(reader.read(BLV_HEADER_SIZE))

    vertices_mm = [parse_vec3s(blob) for blob in read_prefixed_items(reader, 6)]
    vertices_unity = [mm6_to_unity(vertex) for vertex in vertices_mm]

    faces = [parse_face(blob) for blob in read_prefixed_items(reader, BLV_FACE_SIZE)]
    face_data_size = header["faceDataSizeBytes"] // 2
    face_data = list(struct.unpack("<" + ("h" * face_data_size), reader.read(header["faceDataSizeBytes"]))) if face_data_size > 0 else []

    face_offset = 0
    for face in faces:
        face, face_offset = parse_face_data(face, face_data, face_offset)

    face_textures = [decode_fixed_string(reader.read(10)) for _ in range(len(faces))]

    face_extras = [parse_face_extra(blob) for blob in read_prefixed_items(reader, BLV_FACE_EXTRA_SIZE)]
    # MM6 BLV stores a 10-byte trailing string slot after each face extra. In practice these are
    # usually zeroed, but we preserve them in case a mod uses them.
    face_extra_textures = [decode_fixed_string(reader.read(10)) for _ in range(len(face_extras))]

    sectors = [parse_sector(blob) for blob in read_prefixed_items(reader, BLV_SECTOR_SIZE)]
    sector_data_size = header["sectorDataSizeBytes"] // 2
    sector_data = list(struct.unpack("<" + ("H" * sector_data_size), reader.read(header["sectorDataSizeBytes"]))) if sector_data_size > 0 else []
    sector_offset = 0
    for sector in sectors:
        sector, sector_offset = parse_sector_data(sector, sector_data, sector_offset)

    sector_light_data_size = header["sectorLightDataSizeBytes"] // 2
    sector_light_data = (
        list(struct.unpack("<" + ("H" * sector_light_data_size), reader.read(header["sectorLightDataSizeBytes"])))
        if sector_light_data_size > 0
        else []
    )
    sector_light_offset = 0
    for sector in sectors:
        sector, sector_light_offset = parse_sector_light_data(sector, sector_light_data, sector_light_offset)

    door_count = reader.u32()

    decorations_raw = [parse_decoration(blob) for blob in read_prefixed_items(reader, BLV_DECORATION_SIZE)]
    decoration_names = [decode_fixed_string(reader.read(32)) for _ in range(len(decorations_raw))]
    if len(decoration_names) != len(decorations_raw):
        raise ValueError(
            f"Indoor decoration name count {len(decoration_names)} does not match decoration count {len(decorations_raw)}"
        )

    lights_raw = [parse_light(blob) for blob in read_prefixed_items(reader, BLV_LIGHT_SIZE)]
    bsp_nodes_raw = [list(struct.unpack("<hhhh", blob)) for blob in read_prefixed_items(reader, BLV_BSP_NODE_SIZE)]
    spawn_points_raw = [parse_spawn_point(blob) for blob in read_prefixed_items(reader, BLV_SPAWN_POINT_SIZE)]
    map_outlines_raw = [parse_map_outline(blob) for blob in read_prefixed_items(reader, BLV_MAP_OUTLINE_SIZE)]

    texture_manifests: dict[str, TextureInfo] = {}
    exported_faces: list[dict[str, Any]] = []
    exported_decorations: list[dict[str, Any]] = []
    exported_lights: list[dict[str, Any]] = []
    exported_spawn_points: list[dict[str, Any]] = []
    exported_sectors: list[dict[str, Any]] = []

    for index, face in enumerate(faces):
        texture_name = face_textures[index] if index < len(face_textures) else ""
        extra = face_extras[face["faceExtraId"]] if 0 <= face["faceExtraId"] < len(face_extras) else None
        extra_texture_name = face_extra_textures[face["faceExtraId"]] if 0 <= face["faceExtraId"] < len(face_extra_textures) else ""

        texture_info_name = ""
        if texture_name:
            texture_info = register_texture(
                texture_name,
                "bitmap",
                bitmap_lookup,
                texture_manifests,
                args.bitmap_uv_scale,
            )
            texture_info_name = texture_info.name

        exported_faces.append(
            {
                "index": index,
                "textureName": texture_name,
                "textureInfoName": texture_info_name,
                "additionalTextureName": extra_texture_name,
                "sectorId": face["sectorId"],
                "backSectorId": face["backSectorId"],
                "polygonType": face["polygonType"],
                "attributes": face["attributes"],
                "flags": face_flags(face["attributes"]),
                "normal": vec3_object(mm6_vector_to_unity(face["normal"])),
                "normalDistance": face["normalDistance"],
                "boundingBoxMm": face["boundingBoxMm"],
                "vertexIndices": list(face["vertexIndices"]),
                "uList": list(face["uList"]),
                "vList": list(face["vList"]),
                "textureDeltaU": int(extra["textureDeltaU"]) if extra is not None else 0,
                "textureDeltaV": int(extra["textureDeltaV"]) if extra is not None else 0,
                "eventId": int(extra["eventId"]) if extra is not None else 0,
            }
        )

    for index, sector in enumerate(sectors):
        exported_sectors.append(
            {
                "index": index,
                "flags": sector["flags"],
                "boundingBoxMm": sector["boundingBoxMm"],
                "floorIds": list(sector.get("floorIds") or []),
                "wallIds": list(sector.get("wallIds") or []),
                "ceilingIds": list(sector.get("ceilingIds") or []),
                "portalIds": list(sector.get("portalIds") or []),
                "faceIds": list(sector.get("faceIds") or []),
                "decorationIds": list(sector.get("decorationIds") or []),
                "lightIds": list(sector.get("lightIds") or []),
                "minAmbientLightLevel": sector["minAmbientLightLevel"],
            }
        )

    for decoration, decoration_name in zip(decorations_raw, decoration_names):
        exported = build_indoor_decoration_entry(
            decoration,
            decoration_name,
            dec_list,
            sft_groups,
            sprite_lookup,
            texture_manifests,
        )
        if exported is not None:
            exported_decorations.append(exported)

    for index, light in enumerate(lights_raw):
        exported_lights.append(
            {
                "index": index,
                "positionMm": vec3_object(light["position"]),
                "positionUnity": vec3_object(mm6_to_unity(light["position"])),
                "radius": light["radius"],
                "color": {
                    "r": light["red"],
                    "g": light["green"],
                    "b": light["blue"],
                },
                "type": light["type"],
                "attributes": light["attributes"],
                "brightness": light["brightness"],
            }
        )

    for spawn in spawn_points_raw:
        exported_spawn_points.append(
            {
                "positionMm": vec3_object(spawn["position"]),
                "positionUnity": vec3_object(mm6_to_unity(spawn["position"])),
                "radius": spawn["radius"],
                "kind": spawn["type"],
                "index": spawn["index"],
                "bits": spawn["attributes"],
                "group": spawn["group"],
            }
        )

    monsters, monster_import = build_indoor_monster_entries(
        args.map.stem.lower(),
        spawn_points_raw,
        sft_groups,
        sprite_lookup,
        texture_manifests,
    )
    player_start = choose_player_start(exported_faces, vertices_unity, spawn_points_raw)

    sorted_textures = sorted(texture_manifests.values(), key=lambda info: (info.kind, info.name.lower()))
    package = {
        "mapType": "indoor",
        "version": MM6_INDOOR_VERSION,
        "mapName": args.map.stem.lower(),
        "sourceMapPath": str(args.map.resolve()),
        "verticesMm": [vec3_object(vertex) for vertex in vertices_mm],
        "verticesUnity": [vec3_object(vertex) for vertex in vertices_unity],
        "faces": exported_faces,
        "sectors": exported_sectors,
        "decorations": exported_decorations,
        "monsters": monsters,
        "lights": exported_lights,
        "spawnPoints": exported_spawn_points,
        "playerStart": player_start,
        "bspNodes": [
            {"a": node[0], "b": node[1], "c": node[2], "d": node[3]}
            for node in bsp_nodes_raw
        ],
        "mapOutlines": map_outlines_raw,
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
            "vertexCount": len(vertices_mm),
            "faceCount": len(exported_faces),
            "sectorCount": len(exported_sectors),
            "decorationCount": len(exported_decorations),
            "monsterCount": len(monsters),
            "lightCount": len(exported_lights),
            "spawnPointCount": len(exported_spawn_points),
            "doorCount": door_count,
            "mapOutlineCount": len(map_outlines_raw),
            "animatedDecorationCount": sum(
                1
                for decoration in exported_decorations
                if decoration.get("animationTextureInfoNames")
            ),
            "animatedMonsterCount": sum(
                1
                for monster in monsters
                if monster.get("animationTextureInfoNames") or monster.get("idleAnimationTextureInfoNames")
            ),
        },
        "notes": [
            "This indoor export currently imports static BLV geometry plus BLV decorations, lights, spawn points, and idle monster previews from BLV spawn records.",
            "Door animation/runtime state lives in the corresponding DLV file and is not part of this first-pass exporter yet.",
            "Indoor monster visuals are deterministic preview stand-ins until the paired DLV runtime state is parsed.",
            "Animated bitmap faces still depend on dtft.bin, so indoor face texture animation is not exported yet.",
        ],
        "monsterImport": monster_import,
    }

    json_path = args.output / "map.json"
    json_path.write_text(json.dumps(package, indent=2))

    print(
        f"Wrote {json_path} "
        f"({package['summary']['faceCount']} faces, "
        f"{package['summary']['decorationCount']} decorations, "
        f"{package['summary']['monsterCount']} monsters, "
        f"{package['summary']['lightCount']} lights)"
    )


if __name__ == "__main__":
    main()
