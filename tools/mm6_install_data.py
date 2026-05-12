#!/usr/bin/env python3

from __future__ import annotations

import csv
import io
import re
import struct
import zlib
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Optional


LOD_HEADER_SIZE = 256
LOD_ENTRY_SIZE = 32
MM6_TABLE_ENTRY_HEADER_SIZE = 48
MM6_COMPRESSED_HEADER_SIZE = 16
MM6_SFT_ITEM_SIZE = 56
MM6_DECLIST_ITEM_SIZE = 80
MM6_MONLIST_ITEM_SIZE = 148
MM6_TILE_ITEM_SIZE = 26
MM6_TFT_ITEM_SIZE = 20
MM6_MAPMONSTER_ITEM_SIZE = 0x224
MM6_OUTDOOR_DDM_HEADER_SIZE = 8
MM6_OUTDOOR_DISCOVERY_MAP_SIZE = 88 * 11
MM6_OUTDOOR_ACTOR_COUNT_OFFSET = MM6_OUTDOOR_DDM_HEADER_SIZE + MM6_OUTDOOR_DISCOVERY_MAP_SIZE * 2
MM6_OUTDOOR_ACTOR_DATA_OFFSET = MM6_OUTDOOR_ACTOR_COUNT_OFFSET + 4


@dataclass(frozen=True)
class Mm6Install:
    install_dir: Path
    data_dir: Path
    games_lod: Path
    icons_lod: Path
    bitmaps_lod: Path
    sprites_lod: Path
    new_lod: Optional[Path]


def _candidate_install_dirs(extra_candidates: Optional[list[Path]] = None) -> list[Path]:
    candidates = [
        Path("originaldata/Might and Magic 6"),
        Path("originaldata/might and magic 6"),
        Path("originaldata"),
        Path.cwd() / "originaldata" / "Might and Magic 6",
        Path.cwd() / "originaldata",
        Path.cwd(),
    ]
    if extra_candidates:
        candidates = list(extra_candidates) + candidates
    return [candidate for candidate in candidates if candidate]


def resolve_install_dir(install_dir: Optional[Path] = None) -> Optional[Path]:
    if install_dir is not None:
        candidate = install_dir.expanduser().resolve()
        if (candidate / "data" / "Games.lod").exists():
            return candidate
        if candidate.name.lower() == "data" and (candidate / "Games.lod").exists():
            return candidate.parent
        return None

    for candidate in _candidate_install_dirs():
        resolved = candidate.expanduser().resolve()
        if (resolved / "data" / "Games.lod").exists():
            return resolved
    return None


def load_install(install_dir: Optional[Path] = None) -> Optional[Mm6Install]:
    resolved = resolve_install_dir(install_dir)
    if resolved is None:
        return None

    data_dir = resolved / "data"
    new_lod = data_dir / "new.lod"
    if not new_lod.exists():
        new_lod = None

    required = {
        "games": data_dir / "Games.lod",
        "icons": data_dir / "icons.lod",
        "bitmaps": data_dir / "BITMAPS.LOD",
        "sprites": data_dir / "SPRITES.LOD",
    }
    if not all(path.exists() for path in required.values()):
        return None

    return Mm6Install(
        install_dir=resolved,
        data_dir=data_dir,
        games_lod=required["games"],
        icons_lod=required["icons"],
        bitmaps_lod=required["bitmaps"],
        sprites_lod=required["sprites"],
        new_lod=new_lod,
    )


def _decode_cstr(blob: bytes) -> str:
    return blob.split(b"\0", 1)[0].decode("latin1")


def list_lod_entries(lod_path: Path) -> dict[str, tuple[int, int, str]]:
    data = lod_path.read_bytes()
    if len(data) < LOD_HEADER_SIZE + LOD_ENTRY_SIZE:
        raise ValueError(f"{lod_path} is too small to be a valid MM6 LOD archive")

    root_offset, _root_size, _root_unknown, num_items, _priority = struct.unpack_from(
        "<IIIHH",
        data,
        LOD_HEADER_SIZE + 16,
    )

    entries: dict[str, tuple[int, int, str]] = {}
    for index in range(num_items):
        offset = root_offset + index * LOD_ENTRY_SIZE
        entry_name = _decode_cstr(data[offset : offset + 16])
        relative_offset, size, _unknown, child_count, _priority = struct.unpack_from("<IIIHH", data, offset + 16)
        if child_count != 0:
            continue
        entries[entry_name.lower()] = (root_offset + relative_offset, size, entry_name)

    return entries


def read_lod_entry(lod_path: Path, entry_name: str) -> bytes:
    data = lod_path.read_bytes()
    entries = list_lod_entries(lod_path)
    key = entry_name.lower()
    if key not in entries:
        raise FileNotFoundError(f"{entry_name} not found in {lod_path}")

    offset, size, _original_name = entries[key]
    return data[offset : offset + size]


def decode_lod_maybe_compressed(blob: bytes) -> bytes:
    if len(blob) >= 8:
        compressed_size, decompressed_size = struct.unpack_from("<II", blob, 0)
        payload = blob[8:]
        if compressed_size > 0 and compressed_size <= len(blob) - 8:
            payload = blob[8 : 8 + compressed_size]
        elif compressed_size == len(blob):
            payload = blob[8:]
        if (
            compressed_size > 0
            and decompressed_size > 0
            and payload
            and blob[8:10] in {b"\x78\x01", b"\x78\x5e", b"\x78\x9c", b"\x78\xda"}
        ):
            decoded = zlib.decompress(payload)
            if len(decoded) == decompressed_size:
                return decoded

    if len(blob) < MM6_COMPRESSED_HEADER_SIZE:
        return blob

    version, signature, stored_size, decompressed_size = struct.unpack_from("<I4sII", blob, 0)
    if version != 91969 or signature != b"mvii":
        return blob

    if stored_size == len(blob):
        payload = blob[MM6_COMPRESSED_HEADER_SIZE:]
    else:
        payload = blob[MM6_COMPRESSED_HEADER_SIZE : MM6_COMPRESSED_HEADER_SIZE + stored_size]

    return zlib.decompress(payload) if decompressed_size else payload


def decode_mm6_table_entry(blob: bytes) -> bytes:
    if len(blob) < MM6_TABLE_ENTRY_HEADER_SIZE:
        return blob

    compressed_size = struct.unpack_from("<I", blob, 20)[0]
    decompressed_size = struct.unpack_from("<I", blob, 40)[0]
    payload_offset = MM6_TABLE_ENTRY_HEADER_SIZE
    payload_end = payload_offset + compressed_size
    payload = blob[payload_offset:payload_end]

    if len(payload) != compressed_size:
        raise ValueError(
            f"MM6 table entry is truncated: expected {compressed_size} bytes of payload, got {len(payload)}"
        )

    if compressed_size and payload.startswith((b"\x78\x01", b"\x78\x5e", b"\x78\x9c", b"\x78\xda")):
        decoded = zlib.decompress(payload)
        if decompressed_size and len(decoded) != decompressed_size:
            raise ValueError(
                f"MM6 table entry decompressed to {len(decoded)} bytes, expected {decompressed_size}"
            )
        return decoded

    return payload if compressed_size else blob


def read_mm6_icons_table(icons_lod: Path, entry_name: str) -> bytes:
    return decode_mm6_table_entry(read_lod_entry(icons_lod, entry_name))


def read_mm6_icons_text_table(icons_lod: Path, entry_name: str) -> str:
    return read_mm6_icons_table(icons_lod, entry_name).decode("latin1")


def parse_mm6_outdoor_ddm_actors(blob: bytes) -> list[dict[str, Any]]:
    data = decode_lod_maybe_compressed(blob)
    if len(data) < MM6_OUTDOOR_ACTOR_DATA_OFFSET:
        return []

    actor_count = struct.unpack_from("<I", data, MM6_OUTDOOR_ACTOR_COUNT_OFFSET)[0]
    max_count = (len(data) - MM6_OUTDOOR_ACTOR_DATA_OFFSET) // MM6_MAPMONSTER_ITEM_SIZE
    if actor_count > max_count:
        raise ValueError(
            f"Outdoor DDM actor count {actor_count} exceeds payload capacity {max_count}"
        )

    actors: list[dict[str, Any]] = []
    for index in range(actor_count):
        offset = MM6_OUTDOOR_ACTOR_DATA_OFFSET + index * MM6_MAPMONSTER_ITEM_SIZE
        record = data[offset : offset + MM6_MAPMONSTER_ITEM_SIZE]
        current_position = list(struct.unpack_from("<hhh", record, 0x7E))
        start_position = list(struct.unpack_from("<hhh", record, 0x92))
        guard_position = list(struct.unpack_from("<hhh", record, 0x98))

        actors.append(
            {
                "index": index,
                "name": _decode_cstr(record[:32]),
                "npc_id": struct.unpack_from("<h", record, 0x20)[0],
                "bits": struct.unpack_from("<I", record, 0x24)[0],
                "hp": struct.unpack_from("<h", record, 0x28)[0],
                "monster_id": record[0x34],
                "body_radius": struct.unpack_from("<h", record, 0x78)[0],
                "body_height": struct.unpack_from("<h", record, 0x7A)[0],
                "velocity": struct.unpack_from("<h", record, 0x7C)[0],
                "position": current_position,
                "velocity_vector": list(struct.unpack_from("<hhh", record, 0x84)),
                "direction": struct.unpack_from("<H", record, 0x8A)[0],
                "look_angle": struct.unpack_from("<H", record, 0x8C)[0],
                "room": struct.unpack_from("<h", record, 0x8E)[0],
                "current_action_length": struct.unpack_from("<H", record, 0x90)[0],
                "start_position": start_position,
                "guard_position": guard_position,
                "guard_radius": struct.unpack_from("<h", record, 0x9E)[0],
                "ai_state": struct.unpack_from("<h", record, 0xA0)[0],
            }
        )

    return actors


def read_mm6_outdoor_actors(install: Mm6Install, map_name: str) -> list[dict[str, Any]]:
    entry_name = f"{Path(map_name).stem.lower()}.ddm"
    best: list[dict[str, Any]] = []
    best_npc_count = -1

    for lod_path in (install.new_lod, install.games_lod):
        if lod_path is None:
            continue
        try:
            actors = parse_mm6_outdoor_ddm_actors(read_lod_entry(lod_path, entry_name))
        except FileNotFoundError:
            continue
        npc_count = sum(1 for actor in actors if int(actor.get("npc_id", 0)) > 0)
        if npc_count > best_npc_count or (npc_count == best_npc_count and len(actors) > len(best)):
            best = actors
            best_npc_count = npc_count

    return best


def parse_sft_bin(blob: bytes) -> tuple[dict[str, dict[str, Any]], list[str]]:
    if len(blob) < 8:
        raise ValueError("DSFT.BIN is too small")

    frame_count, eframe_count = struct.unpack_from("<II", blob, 0)
    expected_size = 8 + frame_count * MM6_SFT_ITEM_SIZE + eframe_count * 2
    if len(blob) != expected_size:
        raise ValueError(f"DSFT.BIN size mismatch: expected {expected_size}, got {len(blob)}")

    groups: dict[str, dict[str, Any]] = {}
    frame_group_names: list[str] = []
    current_group = ""

    for index in range(frame_count):
        offset = 8 + index * MM6_SFT_ITEM_SIZE
        group_name = _decode_cstr(blob[offset : offset + 12])
        sprite_name = _decode_cstr(blob[offset + 12 : offset + 24])
        scale = struct.unpack_from("<i", blob, offset + 40)[0]
        bits = struct.unpack_from("<H", blob, offset + 44)[0]
        palette_id = struct.unpack_from("<h", blob, offset + 48)[0]
        time_units = struct.unpack_from("<h", blob, offset + 52)[0]

        if group_name:
            current_group = group_name
        frame_group_names.append(current_group)
        if not current_group:
            continue

        group = groups.setdefault(current_group, {"group_name": current_group, "frames": []})
        if not sprite_name or sprite_name.lower() == "null":
            continue

        group["frames"].append(
            {
                "sprite_name": sprite_name,
                "scale": scale,
                "time": time_units,
                "palette_id": palette_id,
                "transparent": False,
                "image1": bool(bits & 0x0010),
                "images3": False,
                "fidget": bool(bits & 0x0040),
            }
        )

    return groups, frame_group_names


def parse_dec_list_bin(blob: bytes, frame_group_names: Optional[list[str]] = None) -> dict[int, dict[str, Any]]:
    if len(blob) < 4:
        raise ValueError("DDECLIST.BIN is too small")

    count = struct.unpack_from("<I", blob, 0)[0]
    expected_size = 4 + count * MM6_DECLIST_ITEM_SIZE
    if len(blob) != expected_size:
        raise ValueError(f"DDECLIST.BIN size mismatch: expected {expected_size}, got {len(blob)}")

    result: dict[int, dict[str, Any]] = {}
    for index in range(count):
        offset = 4 + index * MM6_DECLIST_ITEM_SIZE
        name = _decode_cstr(blob[offset : offset + 32])
        game_name = _decode_cstr(blob[offset + 32 : offset + 64])
        type_id, height, radius, light_radius, sft_index, bits, sound_id = struct.unpack_from(
            "<hhhhhhh",
            blob,
            offset + 64,
        )

        sft_group = ""
        if frame_group_names and 0 <= sft_index < len(frame_group_names):
            sft_group = frame_group_names[sft_index]

        result[index] = {
            "id": index,
            "name": name,
            "game_name": game_name,
            "type": int(type_id),
            "height": int(height),
            "radius": int(radius),
            "light_radius": int(light_radius),
            "sft_group": sft_group,
            "no_draw": bool(bits & 0x0002),
        }

    return result


def parse_monlist_bin(blob: bytes) -> list[dict[str, Any]]:
    if len(blob) < 4:
        raise ValueError("DMONLIST.BIN is too small")

    count = struct.unpack_from("<I", blob, 0)[0]
    expected_size = 4 + count * MM6_MONLIST_ITEM_SIZE
    if len(blob) != expected_size:
        raise ValueError(f"DMONLIST.BIN size mismatch: expected {expected_size}, got {len(blob)}")

    monsters: list[dict[str, Any]] = []
    for index in range(count):
        offset = 4 + index * MM6_MONLIST_ITEM_SIZE
        height, radius, velocity, to_hit_radius = struct.unpack_from("<hhhh", blob, offset)
        sound_attack, sound_die, sound_get_hit, sound_fidget = struct.unpack_from("<4H", blob, offset + 8)
        internal_name = _decode_cstr(blob[offset + 16 : offset + 48])
        frame_names = [
            _decode_cstr(blob[offset + 48 + frame_index * 10 : offset + 58 + frame_index * 10])
            for frame_index in range(8)
        ]

        monsters.append(
            {
                "index": index,
                "internal_name": internal_name,
                "height": int(height),
                "radius": int(radius),
                "velocity": int(velocity),
                "to_hit_radius": int(to_hit_radius),
                "sound_attack": int(sound_attack),
                "sound_die": int(sound_die),
                "sound_get_hit": int(sound_get_hit),
                "sound_fidget": int(sound_fidget),
                "frames_stand": frame_names[0],
                "frames_walk": frame_names[1],
                "frames_attack": frame_names[2],
                "frames_shoot": frame_names[3],
                "frames_stun": frame_names[4],
                "frames_die": frame_names[5],
                "frames_dead": frame_names[6],
                "frames_fidget": frame_names[7],
            }
        )

    return monsters


def parse_dtile_bin_bytes(blob: bytes) -> dict[int, str]:
    if len(blob) < 4:
        raise ValueError("DTILE.BIN is too small")

    count = struct.unpack_from("<I", blob, 0)[0]
    expected_size = 4 + count * MM6_TILE_ITEM_SIZE
    if len(blob) != expected_size:
        raise ValueError(f"DTILE.BIN size mismatch: expected {expected_size}, got {len(blob)}")

    textures: dict[int, str] = {}
    for index in range(count):
        offset = 4 + index * MM6_TILE_ITEM_SIZE
        textures[index] = _decode_cstr(blob[offset : offset + 16])
    return textures


def parse_dtft_bin_bytes(blob: bytes) -> list[dict[str, Any]]:
    if len(blob) < 4:
        raise ValueError("DTFT.BIN is too small")

    count = struct.unpack_from("<I", blob, 0)[0]
    expected_size = 4 + count * MM6_TFT_ITEM_SIZE
    if len(blob) != expected_size:
        raise ValueError(f"DTFT.BIN size mismatch: expected {expected_size}, got {len(blob)}")

    frames: list[dict[str, Any]] = []
    for index in range(count):
        offset = 4 + index * MM6_TFT_ITEM_SIZE
        frames.append(
            {
                "name": _decode_cstr(blob[offset : offset + 12]),
                "index": struct.unpack_from("<h", blob, offset + 12)[0],
                "time": struct.unpack_from("<h", blob, offset + 14)[0],
                "total_time": struct.unpack_from("<h", blob, offset + 16)[0],
                "bits": struct.unpack_from("<H", blob, offset + 18)[0],
            }
        )
    return frames


def _parse_appear_range(value: str) -> tuple[int, int]:
    numbers = [int(part) for part in re.findall(r"\d+", value or "")]
    if not numbers:
        return (1, 1)
    if len(numbers) == 1:
        return (numbers[0], numbers[0])
    return (numbers[0], numbers[1])


def parse_mapstats_text(text: str) -> dict[str, dict[str, Any]]:
    lines = text.splitlines()
    if len(lines) < 4:
        return {}

    data_lines = lines[3:]
    reader = csv.reader(io.StringIO("\n".join(data_lines)), delimiter="\t")
    result: dict[str, dict[str, Any]] = {}

    for row in reader:
        if not row or not row[0].strip().isdigit():
            continue
        if len(row) < 26:
            continue

        file_name = row[2].strip()
        map_key = Path(file_name).stem.lower()
        slots = {}
        for slot_index, label in enumerate(("m1", "m2", "m3")):
            base = 13 + slot_index * 4
            picture_base = row[base].strip()
            if not picture_base or picture_base == "0":
                continue
            low, high = _parse_appear_range(row[base + 3].strip())
            slots[label] = {
                "pictureBase": picture_base,
                "displayName": row[base + 1].strip(),
                "difficulty": int((row[base + 2] or "0").strip() or 0),
                "countRange": [low, high],
            }

        result[map_key] = {
            "id": int(row[0].strip()),
            "name": row[1].strip(),
            "fileName": file_name,
            "lock": int((row[6] or "0").strip() or 0),
            "trap": int((row[7] or "0").strip() or 0),
            "treasure": int((row[8] or "0").strip() or 0),
            "encounterChance": int((row[9] or "0").strip() or 0),
            "encounterChanceM1": int((row[10] or "0").strip() or 0),
            "encounterChanceM2": int((row[11] or "0").strip() or 0),
            "encounterChanceM3": int((row[12] or "0").strip() or 0),
            "slots": slots,
        }

    return result


def parse_monsters_text(text: str) -> dict[str, dict[str, Any]]:
    lines = text.splitlines()
    if len(lines) < 4:
        return {}

    rows = lines[3:]
    reader = csv.reader(io.StringIO("\n".join(rows)), delimiter="\t")
    result: dict[str, dict[str, Any]] = {}

    for row in reader:
        if not row:
            continue
        identifier = (row[0] if len(row) > 0 else "").strip()
        picture = (row[1] if len(row) > 1 else "").strip()
        if not identifier.isdigit() or not picture:
            continue

        result[picture.lower()] = {
            "id": int(identifier),
            "picture": picture,
            "name": (row[2] if len(row) > 2 else "").strip(),
            "level": int((row[3] if len(row) > 3 else "0").replace('"', "").strip() or 0),
        }

    return result


def parse_2d_events_text(text: str) -> list[dict[str, Any]]:
    lines = text.splitlines()
    if len(lines) < 3:
        return []

    rows = lines[2:]
    reader = csv.reader(io.StringIO("\n".join(rows)), delimiter="\t")
    result: list[dict[str, Any]] = []

    for row in reader:
        if not row or not row[0].strip().isdigit():
            continue

        result.append(
            {
                "id": int(row[0].strip()),
                "localId": int((row[1] if len(row) > 1 else "0").strip() or 0),
                "type": (row[2] if len(row) > 2 else "").strip(),
                "map": (row[3] if len(row) > 3 else "").strip(),
                "picture": int((row[4] if len(row) > 4 else "0").strip() or 0),
                "name": (row[5] if len(row) > 5 else "").strip(),
                "proprietorName": (row[6] if len(row) > 6 else "").strip(),
                "title": (row[7] if len(row) > 7 else "").strip(),
                "notesA": (row[16] if len(row) > 16 else "").strip(),
                "notesB": (row[17] if len(row) > 17 else "").strip(),
                "openHour": int((row[18] if len(row) > 18 else "0").strip() or 0),
                "closeHour": int((row[19] if len(row) > 19 else "0").strip() or 0),
            }
        )

    return result


def build_default_bitmap_dirs() -> list[Path]:
    return [
        Path("generated/mm6-install/bitmaps"),
        Path("generated/mm6-install/bitmaps_lod"),
    ]


def build_default_sprite_dirs() -> list[Path]:
    return [
        Path("generated/mm6-install/sprites"),
        Path("generated/mm6-install/sprites_lod"),
    ]


def build_monster_records(
    monlist: list[dict[str, Any]],
    monster_text: dict[str, dict[str, Any]],
) -> tuple[dict[str, dict[str, Any]], dict[str, dict[str, dict[str, Any]]]]:
    by_picture: dict[str, dict[str, Any]] = {}
    by_picture_base: dict[str, dict[str, dict[str, Any]]] = {}

    for monster in monlist:
        internal_name = monster.get("internal_name", "")
        if not internal_name:
            continue

        key = internal_name.lower()
        text_info = monster_text.get(key, {})
        record = {
            **monster,
            "picture": text_info.get("picture", internal_name),
            "display_name": text_info.get("name", internal_name),
            "monster_id": text_info.get("id", monster.get("index", 0) + 1),
        }
        by_picture[key] = record

        variant = ""
        base_name = internal_name
        if internal_name[-1:] in {"A", "B", "C"}:
            variant = internal_name[-1]
            base_name = internal_name[:-1]

        if variant:
            by_picture_base.setdefault(base_name.lower(), {})[variant] = record

    return by_picture, by_picture_base
