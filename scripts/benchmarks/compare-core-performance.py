"""Run identical Unity probes back to back and fail on a repeatable slowdown."""

import argparse
import csv
import json
import math
import os
from pathlib import Path
import platform
import statistics
import subprocess


# Relative limit and minimum absolute increase in milliseconds per operation.
LIMITS = {
    "cpu": {"load_file": (.12, .10), "load_data": (.12, .10),
            "raster": (.12, .025), "prepare": (.12, .12)},
    "d3d12": {"d3d12_draw": (.15, .05), "d3d12_frame": (.15, .20),
              "d3d12_dispose_p95": (.15, .04)},
}
ROUNDS = 9


def read_samples(path, metrics):
    groups = {metric: {} for metric in metrics}
    with path.open(newline="", encoding="utf-8-sig") as stream:
        for round_id, metric, milliseconds in csv.reader(stream):
            round_id, milliseconds = int(round_id), float(milliseconds)
            if (metric not in groups or round_id not in range(1, ROUNDS + 1)
                    or round_id in groups[metric] or not math.isfinite(milliseconds) or milliseconds <= 0):
                raise ValueError(f"Invalid benchmark sample in {path}: {round_id}, {metric}, {milliseconds}")
            groups[metric][round_id] = milliseconds
    if any(len(values) != ROUNDS for values in groups.values()):
        raise ValueError(f"Incomplete benchmark samples: {path}")
    return {metric: statistics.median(values.values()) for metric, values in groups.items()}


def measure(player, fixture, output, mode, metrics):
    samples = output.with_suffix(".csv")
    args = [str(player), "-logFile", str(output.with_suffix(".log")),
            "-lottieCorePerformance", str(samples), str(fixture)]
    if mode == "cpu":
        args += ["-batchmode", "-nographics"]
    else:
        args += ["-lottieCoreD3D12", "-force-d3d12", "-screen-fullscreen", "0",
                 "-screen-width", "1280", "-screen-height", "720"]
    subprocess.run(args, cwd=player.parent, timeout=180, check=True)
    return read_samples(samples, metrics)


def compare(pairs, limits):
    report = {}
    for metric, (relative, absolute) in limits.items():
        baseline = [pair["baseline"][metric] for pair in pairs]
        candidate = [pair["candidate"][metric] for pair in pairs]
        changes = [(after - before, after / before - 1) for before, after in zip(baseline, candidate)]
        violations = sum(delta > absolute and ratio > relative for delta, ratio in changes)
        report[metric] = {
            "baseline_ms": statistics.median(baseline), "candidate_ms": statistics.median(candidate),
            "change_percent": statistics.median(ratio for _, ratio in changes) * 100,
            "change_ms": statistics.median(delta for delta, _ in changes),
            "relative_limit_percent": relative * 100, "absolute_limit_ms": absolute,
            "pairs_over_limit": violations, "regressed": violations > len(pairs) // 2,
        }
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--baseline", type=Path, required=True)
    parser.add_argument("--candidate", type=Path, required=True)
    parser.add_argument("--baseline-ref", required=True)
    parser.add_argument("--candidate-ref", required=True)
    parser.add_argument("--fixture", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True, help="New results directory")
    parser.add_argument("--mode", choices=LIMITS, default="cpu")
    args = parser.parse_args()
    players = {name: getattr(args, name).resolve(strict=True) for name in ("baseline", "candidate")}
    fixture = args.fixture.resolve(strict=True)
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    limits = LIMITS[args.mode]
    pairs = []
    for repeat in range(1, 6):
        order = ("baseline", "candidate") if repeat % 2 else ("candidate", "baseline")
        pair = {}
        for name in order:
            print(f"Pair {repeat}/5: {name}", flush=True)
            pair[name] = measure(players[name], fixture, output / f"{repeat}-{name}", args.mode, limits)
        pairs.append(pair)

    metrics = compare(pairs, limits)
    result = {
        "baseline_ref": args.baseline_ref, "candidate_ref": args.candidate_ref,
        "platform": platform.platform(), "mode": args.mode, "pairs": pairs, "metrics": metrics,
    }
    (output / "comparison.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    lines = [f"### {args.mode} performance from `{args.baseline_ref}` to `{args.candidate_ref}`", "",
             "| Metric | Baseline (ms) | Candidate (ms) | Paired change | Pairs over limit |",
             "| --- | ---: | ---: | ---: | ---: |"]
    for name, metric in metrics.items():
        status = " **FAIL**" if metric["regressed"] else ""
        lines.append(f"| `{name}` | {metric['baseline_ms']:.4f} | {metric['candidate_ms']:.4f} | "
                     f"{metric['change_percent']:+.1f}% | {metric['pairs_over_limit']}/5{status} |")
    summary = "\n".join(lines) + "\n"
    print(summary)
    if os.getenv("GITHUB_STEP_SUMMARY"):
        with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as stream:
            stream.write(summary)
    return int(any(metric["regressed"] for metric in metrics.values()))


if __name__ == "__main__":
    raise SystemExit(main())
