"""Checks the release gate with small ELF fixtures; no native code is executed."""
import importlib.util
import pathlib
import struct
import unittest

spec = importlib.util.spec_from_file_location("check_linux_native", pathlib.Path(__file__).with_name("check-linux-native.py"))
checker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(checker)


def elf(machine=183, version="GLIBC_2.31"):
    strings = b"\0libc.so.6\0" + version.encode() + b"\0"
    need = struct.pack("<HHIII", 1, 1, 1, 16, 0) + struct.pack("<IHHII", 0, 0, 2, 11, 0)
    header = bytearray(64)
    header[:7] = b"\x7fELF\x02\x01\x01"
    struct.pack_into("<HHI", header, 16, 3, machine, 1)
    struct.pack_into("<Q", header, 40, 64 + len(strings) + len(need))
    struct.pack_into("<HHH", header, 58, 64, 3, 0)
    section = lambda kind, offset, size, link: struct.pack("<IIQQQQIIQQ", 0, kind, 0, 0, offset, size, link, 0, 1, 0)
    return bytes(header) + strings + need + bytes(64) + section(3, 64, len(strings), 0) + section(0x6ffffffe, 64 + len(strings), len(need), 1)


class NativeGateTests(unittest.TestCase):
    def test_accepts_target_baseline(self):
        info = checker.inspect_elf(elf(), "linux-arm64", "2.31")
        self.assertEqual(info["glibc"], ["2.31"])
        self.assertEqual(info["dependencies"], ["libc.so.6"])

    def test_rejects_newer_glibc(self):
        with self.assertRaisesRegex(ValueError, "2.38"):
            checker.inspect_elf(elf(version="GLIBC_2.38"), "linux-arm64", "2.31")

    def test_rejects_wrong_cpu(self):
        with self.assertRaisesRegex(ValueError, "architecture"):
            checker.inspect_elf(elf(machine=62), "linux-arm64", "2.31")

    def test_rejects_non_elf_and_truncated_sections(self):
        for content in [b"not ELF", elf()[:70]]:
            with self.subTest(length=len(content)), self.assertRaises(ValueError):
                checker.inspect_elf(content, "linux-arm64", "2.31")


if __name__ == "__main__":
    unittest.main()
