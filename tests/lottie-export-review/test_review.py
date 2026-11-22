import hashlib
import importlib.util
import json
from pathlib import Path
import runpy
import subprocess
import sys
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
REVIEW = runpy.run_path(str(ROOT / "scripts/review-lottie-export.py"))
NATIVE = ROOT / "unity/RLottieUnity/Assets/LottiePlugin/Plugins/Windows/x86_64/LottiePlugin.dll"


def encoded(document):
    return json.dumps(document).encode("utf-8")


def constant(value):
    return {"a": 0, "k": value}


def fixture():
    return {
        "v": "5.12.0", "w": 32, "h": 16, "ip": 10.5, "op": 12.5, "fr": 30,
        "layers": [{
            "ty": 4, "ind": 1, "nm": "Calibration", "ip": 10, "op": 13, "st": 0, "sr": 1,
            "ks": {"o": constant(100), "r": constant(0), "p": constant([0, 0]),
                   "a": constant([0, 0]), "s": constant([100, 100]), "sk": constant(0)},
            "shapes": [{"d": 1, "ty": "rc", "p": constant([16, 8]), "s": constant([20, 12]),
                        "r": constant(0)}, {"ty": "fl", "c": constant([1, 0, 0, 1]),
                                             "o": constant(50), "r": 1}],
        }],
    }


class ReviewTests(unittest.TestCase):
    def test_fractional_bounds_and_split_position(self):
        document = fixture()
        document["layers"][0]["ks"]["p"] = {"s": True, "x": constant(0), "y": constant(0)}
        report = REVIEW["inspect_export"](document)
        self.assertEqual((report["inFrame"], report["outFrame"]), (10.5, 12.5))
        self.assertEqual((report["rasterInFrame"], report["rasterOutFrame"]), (11, 13))
        self.assertEqual(report["issues"], [])

    def test_finds_skipped_geometry_and_nested_expressions(self):
        document = fixture()
        layer = document["layers"].pop()
        rectangle, fill = layer["shapes"]
        layer["shapes"] = [{"ty": "gr", "it": [{"p": rectangle["p"], **rectangle}, fill]}]
        fill["o"]["x"] = "time * 20"
        document["assets"] = [{"id": "nested", "layers": [layer]}]
        issues = REVIEW["inspect_export"](document)["issues"]
        self.assertEqual([issue["path"] for issue in issues],
                         ["$.assets[0].layers[0].shapes[0].it[0].ty",
                          "$.assets[0].layers[0].shapes[0].it[1].o.x"])

    def test_rejects_container_and_ambiguous_json(self):
        for data in (b"PK\x03\x04", b'{"v":"5","v":"4"}', b'{"x":NaN}', b'{"x":1e400}',
                     b'{"w":16,"h":16,"ip":0,"op":1,"fr":30,"layers":[]}'):
            with self.subTest(data=data), self.assertRaises(ValueError):
                REVIEW["inspect_export"](REVIEW["read_export"](data))

    def test_flags_media_skew_and_unsupported_shapes(self):
        document = fixture()
        layer = document["layers"][0]
        layer["ks"]["sk"] = constant(10)
        layer["shapes"].append({"ty": "mm"})
        document["assets"] = [{"id": "img", "p": "image.png", "u": "images/"}]
        issues = REVIEW["inspect_export"](document)["issues"]
        self.assertEqual([issue["path"] for issue in issues],
                         ["$.layers[0].ks.sk", "$.layers[0].shapes[2].ty", "$.assets[0]"])

    def test_cli_writes_normalized_copy_and_hashes(self):
        document = fixture()
        rectangle = document["layers"][0]["shapes"][0]
        rectangle["ty"] = rectangle.pop("ty")
        data = encoded(document)
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / "export.json"
            source.write_bytes(data)
            command = [sys.executable, str(ROOT / "scripts/review-lottie-export.py"), str(source)]
            flagged = subprocess.run(command + ["--output", str(root / "flagged")], capture_output=True)
            self.assertEqual(flagged.returncode, 2, flagged.stderr)
            repaired = root / "repaired"
            result = subprocess.run(command + ["--output", str(repaired), "--normalize-shape-order"],
                                    capture_output=True)
            self.assertEqual(result.returncode, 0, result.stderr)
            report = json.loads((repaired / "review.json").read_text(encoding="utf-8"))
            normalized = (repaired / "normalized.json").read_bytes()
            self.assertEqual(report["inputSha256"], hashlib.sha256(data).hexdigest())
            self.assertEqual(report["normalization"]["sha256"], hashlib.sha256(normalized).hexdigest())
            self.assertEqual(source.read_bytes(), data)
            self.assertEqual(json.loads(normalized), document)
            self.assertEqual(report["issues"], [])

    @unittest.skipUnless(sys.platform == "win32" and sys.maxsize > 2**32 and NATIVE.exists()
                         and importlib.util.find_spec("PIL"), "Requires the Windows x64 plugin and Pillow")
    def test_native_frames_alpha_and_ordering_regression(self):
        from PIL import Image

        document = fixture()
        data = encoded(document)
        report = REVIEW["inspect_export"](document)
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory)
            sampled = REVIEW["render_filmstrip"](data, report, NATIVE, output, 32, 8)
            self.assertEqual((sampled["width"], sampled["height"]), (32, 16))
            self.assertEqual([(frame["sourceFrame"], frame["nativeFrame"]) for frame in sampled["frames"]],
                             [(11, 0), (12, 1)])
            self.assertEqual([frame["visiblePixels"] for frame in sampled["frames"]], [240, 240])
            with Image.open(output / "filmstrip.png") as filmstrip:
                self.assertEqual(filmstrip.getpixel((0, 0)), (0, 0, 0, 0))
                red, green, blue, alpha = filmstrip.getpixel((16, 8))
                self.assertGreaterEqual(red, 253)
                self.assertEqual((green, blue), (0, 0))
                self.assertIn(alpha, (127, 128))
            rectangle = document["layers"][0]["shapes"][0]
            rectangle["ty"] = rectangle.pop("ty")
            broken = REVIEW["render_filmstrip"](encoded(document), report, NATIVE, output, 32, 8)
            self.assertEqual([frame["visiblePixels"] for frame in broken["frames"]], [0, 0])
            misplaced = []
            self.assertTrue(REVIEW["inspect_export"](document, misplaced)["issues"])
            REVIEW["normalize_shape_order"](misplaced)
            self.assertEqual(REVIEW["inspect_export"](document)["issues"], [])
            repaired = REVIEW["render_filmstrip"](encoded(document), report, NATIVE, output, 32, 8)
            self.assertEqual([frame["bgraSha256"] for frame in repaired["frames"]],
                             [frame["bgraSha256"] for frame in sampled["frames"]])


if __name__ == "__main__":
    unittest.main()
