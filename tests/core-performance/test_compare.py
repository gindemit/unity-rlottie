import runpy
import tempfile
import unittest
from pathlib import Path


benchmark = runpy.run_path(str(Path(__file__).resolve().parents[2]
                               / "scripts/benchmarks/compare-core-performance.py"))


class ComparisonTests(unittest.TestCase):
    def test_only_repeatable_slowdowns_over_both_limits_fail(self):
        def comparison(baseline, candidates):
            pairs = [{"baseline": {"metric": baseline}, "candidate": {"metric": value}}
                     for value in candidates]
            return benchmark["compare"](pairs, {"metric": (.12, .10)})["metric"]

        self.assertTrue(comparison(1, [1.2, 1.2, 1.2, 1.0, .9])["regressed"])
        self.assertFalse(comparison(1, [5, 5, 1.1, .9, .8])["regressed"])
        self.assertFalse(comparison(.01, [.02] * 5)["regressed"])
        self.assertFalse(comparison(10, [10.5] * 5)["regressed"])

    def test_samples_must_have_all_unique_rounds_and_finite_positive_times(self):
        rows = [f"{index},raster,{index}" for index in range(1, 10)]
        with tempfile.TemporaryDirectory() as directory:
            samples = Path(directory) / "samples.csv"
            samples.write_text("\n".join(rows), encoding="utf-8")
            self.assertEqual(benchmark["read_samples"](samples, ["raster"]), {"raster": 5.0})
            for invalid in [rows[:-1], rows + rows[:1], rows[:-1] + ["9,raster,nan"],
                            rows[:-1] + ["9,raster,0"], rows[:-1] + ["9,other,1"]]:
                with self.subTest(invalid=invalid[-1]), self.assertRaises(ValueError):
                    samples.write_text("\n".join(invalid), encoding="utf-8")
                    benchmark["read_samples"](samples, ["raster"])


if __name__ == "__main__":
    unittest.main()
