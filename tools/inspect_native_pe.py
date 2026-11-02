#!/usr/bin/env python3
"""Inspect PE metadata and embedded strings without loading game DLLs.

This is a read-only triage tool. It does not execute, patch, or decompile DLLs.
"""
from __future__ import annotations

import argparse
import pathlib
import struct
import sys


def u16(data, offset):
    return struct.unpack_from("<H", data, offset)[0]


def u32(data, offset):
    return struct.unpack_from("<I", data, offset)[0]


def c_string(data, offset, limit=512):
    end = data.find(b"\0", offset, min(len(data), offset + limit))
    if end < 0:
        end = min(len(data), offset + limit)
    return data[offset:end].decode("ascii", "replace")


def inspect(path):
    data = pathlib.Path(path).read_bytes()
    if len(data) < 0x40 or data[:2] != b"MZ":
        raise ValueError("not a DOS/PE image")
    pe = u32(data, 0x3C)
    if data[pe:pe + 4] != b"PE\0\0":
        raise ValueError("PE signature missing")
    coff = pe + 4
    machine, sections_count = struct.unpack_from("<HH", data, coff)
    optional_size = u16(data, coff + 16)
    optional = coff + 20
    magic = u16(data, optional)
    if magic == 0x10B:
        bits, directories = 32, optional + 96
    elif magic == 0x20B:
        bits, directories = 64, optional + 112
    else:
        raise ValueError(f"unknown optional header magic 0x{magic:04x}")

    sections = []
    section_base = optional + optional_size
    for index in range(sections_count):
        off = section_base + index * 40
        name = data[off:off + 8].split(b"\0", 1)[0].decode("ascii", "replace")
        virtual_size, virtual_address, raw_size, raw_pointer = struct.unpack_from("<IIII", data, off + 8)
        sections.append((name, virtual_address, max(virtual_size, raw_size), raw_pointer, raw_size))

    def rva_offset(rva):
        for _, va, span, raw, raw_size in sections:
            if va <= rva < va + span:
                delta = rva - va
                if delta >= raw_size:
                    return None
                return raw + delta
        return None

    def directory(index):
        off = directories + index * 8
        if off + 8 > len(data):
            return 0, 0
        return struct.unpack_from("<II", data, off)

    exports = []
    export_rva, export_size = directory(0)
    export_off = rva_offset(export_rva) if export_rva else None
    if export_off is not None and export_off + 40 <= len(data):
        fields = struct.unpack_from("<IIHHIIIIIII", data, export_off)
        base, number_functions, number_names = fields[5], fields[6], fields[7]
        names_rva, ordinals_rva = fields[9], fields[10]
        names_off, ordinals_off = rva_offset(names_rva), rva_offset(ordinals_rva)
        if names_off is not None and ordinals_off is not None:
            for i in range(min(number_names, 20000)):
                name_rva = u32(data, names_off + i * 4)
                name_off = rva_offset(name_rva)
                ordinal = u16(data, ordinals_off + i * 2)
                if name_off is not None:
                    exports.append((base + ordinal, c_string(data, name_off)))

    imports = []
    import_rva, _ = directory(1)
    import_off = rva_offset(import_rva) if import_rva else None
    if import_off is not None:
        for i in range(4096):
            off = import_off + i * 20
            if off + 20 > len(data):
                break
            original, stamp, chain, name_rva, thunk = struct.unpack_from("<IIIII", data, off)
            if not any((original, stamp, chain, name_rva, thunk)):
                break
            name_off = rva_offset(name_rva)
            if name_off is not None:
                imports.append(c_string(data, name_off))

    keywords = (
        "imgui", "unity", "il2cpp", "mono", "community", "friend", "leader",
        "profile", "avatar", "overlay", "metal", "ufo", "d3d", "dxgi",
        "opengl", "vulkan", "winhttp", "wininet", "websocket", "curl",
        "http", "socket", "ranked", "player", "panel", "discord"
    )
    found = set()
    for match in __import__("re").finditer(rb"[\x20-\x7e]{5,}", data):
        value = match.group().decode("ascii", "replace")
        if any(word in value.lower() for word in keywords):
            found.add(value[:240])
    for match in __import__("re").finditer(rb"(?:[\x20-\x7e]\x00){5,}", data):
        value = match.group().decode("utf-16le", "replace")
        if any(word in value.lower() for word in keywords):
            found.add(value[:240])

    machine_name = {0x14C: "x86", 0x8664: "x64", 0xAA64: "ARM64"}.get(machine, f"0x{machine:04x}")
    lines = [
        f"FILE: {path}",
        f"SIZE: {len(data)} bytes",
        f"PE: {bits}-bit {machine_name}",
        "SECTIONS:",
    ]
    lines.extend(f"  {name}: RVA=0x{va:x} raw={raw_size}" for name, va, _, _, raw_size in sections)
    lines.append("IMPORTS:")
    lines.extend(f"  {name}" for name in imports)
    lines.append("EXPORTS:")
    lines.extend(f"  ordinal={ordinal} {name}" for ordinal, name in exports)
    lines.append("RELEVANT EMBEDDED STRINGS:")
    lines.extend(f"  {value}" for value in sorted(found)[:500])
    return "\n".join(lines)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("files", nargs="+")
    args = parser.parse_args()
    failed = False
    for path in args.files:
        try:
            print(inspect(path))
        except Exception as exc:
            failed = True
            print(f"FILE: {path}\nERROR: {exc}")
        print("\n" + "=" * 78 + "\n")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
