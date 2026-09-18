"""Read ELF version requirements without loading untrusted/native machine code.

This is an architecture/glibc gate, not proof of target-machine compatibility.
Run ldd and the model/camera smoke tests on the target as well.
"""
import argparse
import json
import pathlib
import struct


def inspect_elf(data, runtime, max_glibc):
    def unpack(fmt, offset):
        if offset < 0 or offset + struct.calcsize(fmt) > len(data):
            raise ValueError("Truncated ELF data")
        return struct.unpack_from(fmt, data, offset)

    if len(data) < 64 or data[:7] != b"\x7fELF\x02\x01\x01":
        raise ValueError("Expected a little-endian ELF64 binary")
    expected = {"linux-arm64": 183, "linux-x64": 62}[runtime]
    if unpack("<H", 18)[0] != expected:
        raise ValueError(f"ELF architecture does not match {runtime}")
    offset = unpack("<Q", 40)[0]
    size, count = unpack("<HH", 58)
    if size != 64 or count < 1 or offset < 64:
        raise ValueError("Unsupported or missing ELF section table")
    sections = [unpack("<IIQQQQIIQQ", offset + i * size) for i in range(count)]

    def content(section):
        start, length = section[4:6]
        if start + length > len(data):
            raise ValueError("Truncated ELF section")
        return data[start:start + length]

    def string_at(table, index):
        if index >= len(table) or b"\0" not in table[index:]:
            raise ValueError("Invalid ELF string index")
        return table[index:table.index(b"\0", index)].decode("ascii")

    versions, dependencies = set(), set()
    for section in sections:
        if section[1] not in (6, 0x6ffffffe):
            continue
        if section[6] >= len(sections):
            raise ValueError("Invalid ELF string table link")
        strings = content(sections[section[6]])
        start, length = section[4:6]
        content(section)  # Validate bounds before walking linked records.
        if section[1] == 6:
            for pos in range(start, start + length, 16):
                tag, value = unpack("<qQ", pos)
                if tag == 0:
                    break
                if tag == 1:
                    dependencies.add(string_at(strings, value))
        else:
            pos = start
            while pos < start + length:
                _, auxiliary_count, filename, auxiliary, next_record = unpack("<HHIII", pos)
                dependencies.add(string_at(strings, filename))
                aux = pos + auxiliary
                for _ in range(auxiliary_count):
                    if aux < start or aux + 16 > start + length:
                        raise ValueError("Invalid ELF version record")
                    _, _, _, name, next_aux = unpack("<IHHII", aux)
                    versions.add(string_at(strings, name))
                    aux += next_aux
                if not next_record:
                    break
                if next_record < 16:
                    raise ValueError("Invalid ELF version chain")
                pos += next_record
    version_key = lambda value: tuple(int(part) for part in value.split("."))
    glibc = sorted((v[6:] for v in versions if v.startswith("GLIBC_") and v[6:7].isdigit()), key=version_key)
    if glibc and version_key(glibc[-1]) > version_key(max_glibc):
        raise ValueError(f"Requires glibc {glibc[-1]}, exceeds target {max_glibc}")
    return {"runtime": runtime, "glibc": glibc, "dependencies": sorted(dependencies),
            "cpp_versions": sorted(v for v in versions if v.startswith(("GLIBCXX_", "CXXABI_")))}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("path", type=pathlib.Path)
    parser.add_argument("--runtime", choices=["linux-arm64", "linux-x64"], required=True)
    parser.add_argument("--max-glibc", default="2.31")
    args = parser.parse_args()
    candidates = sorted(args.path.rglob("*")) if args.path.is_dir() else [args.path]
    checked = 0
    for path in candidates:
        if not path.is_file():
            continue
        with path.open("rb") as stream:
            magic = stream.read(4)
        if magic != b"\x7fELF":
            if ".so" in path.name or path == args.path and args.path.is_file():
                parser.exit(1, f"Invalid native binary: {path}\n")
            continue
        try:
            info = inspect_elf(path.read_bytes(), args.runtime, args.max_glibc)
        except (ValueError, struct.error) as error:
            parser.exit(1, f"Native compatibility check failed for {path.name}: {error}\n")
        print(json.dumps({"file": path.name, **info}, ensure_ascii=False))
        checked += 1
    if not checked:
        parser.exit(1, "No ELF binaries found\n")


if __name__ == "__main__":
    main()
