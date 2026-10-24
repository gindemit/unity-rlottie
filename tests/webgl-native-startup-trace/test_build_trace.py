"""Small producer contracts; no compiler, Unity, cache, build or publication."""
import json
from pathlib import Path
import tempfile
import unittest

import build_trace as build


class BuildReceiptContracts(unittest.TestCase):
    def test_actual_command_windows_quoting(self):
        command = '"C:/Program Files/emscripten/em++.bat" -I"C:/source dir/inc" -fwasm-exceptions -c "C:/source dir/a.cpp"'
        self.assertEqual(build.tokens(command), ['C:/Program Files/emscripten/em++.bat',
                         '-IC:/source dir/inc', '-fwasm-exceptions', '-c', 'C:/source dir/a.cpp'])
        path = Path('C:/Program Files/tool.exe')
        self.assertEqual(build.argv_strings([path, '--version']), [path.as_posix(), '--version'])

    def test_defined_and_empty_undefined_inventory(self):
        self.assertEqual(build.normalize_symbols('a.o:\nz T 0 1\na U 0 0\nz W 0 1\n'), ['a', 'z'])
        self.assertEqual(build.normalize_symbols('', allow_empty=True), [])
        self.assertEqual(build.inventory_hash([]), build.sha(b''))
        self.assertEqual(build.inventory_hash(['a', 'z']), build.sha(b'a\nz\n'))
        with self.assertRaises(ValueError):
            build.normalize_symbols('unexpected output')

    def test_protected_or_existing_output_rejected_before_mutation(self):
        with tempfile.TemporaryDirectory() as temporary:
            parent = Path(temporary)
            missing = parent / 'protected' / 'new'
            with self.assertRaises(ValueError):
                build.safe_external(missing, [parent / 'protected'])
            self.assertFalse(missing.exists())
            with self.assertRaises(ValueError):
                build.safe_external(parent, [])

    def test_receipt_descriptor_binds_exact_utf8_bytes(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / 'receipt.json'
            descriptor = build.write_json(path, {'exit': 0, 'symbols': ['a']})
            self.assertEqual(descriptor['path'], 'receipt.json')
            self.assertEqual(descriptor['bytes'], len(path.read_bytes()))
            self.assertEqual(descriptor['sha256'], build.sha(path.read_bytes()))
            self.assertEqual(json.loads(path.read_bytes())['symbols'], ['a'])

    def test_empty_compiled_source_is_explicitly_allowed_only_for_source_inventory(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / 'empty.cpp'
            path.write_bytes(b'')
            with self.assertRaises(ValueError):
                build.descriptor(path)
            descriptor = build.descriptor(path, 'empty.cpp', allow_empty=True)
            self.assertEqual(descriptor, {'path': 'empty.cpp', 'bytes': 0, 'sha256': build.sha(b'')})


if __name__ == '__main__':
    unittest.main()
