#!/usr/bin/env python3
"""Run source-sliced managed WebGL upload contracts using host Unity/native stubs."""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

EXPECTED_TESTS = 33
RUNTIME_RELATIVE = Path("unity/RLottieUnity/Assets/LottiePlugin/Runtime/src")
GUARD = "#if UNITY_WEBGL && !UNITY_EDITOR && DEVELOPMENT_BUILD"
MAX_LOG_BYTES = 2 * 1024 * 1024


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(256 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def guarded_region(source: str) -> tuple[str, int, int]:
    if source.count(GUARD) != 1:
        raise ValueError("Expected one Development WebGL diagnostic guard.")
    start = source.index(GUARD)
    depth = 0
    offset = start
    for line in source[start:].splitlines(keepends=True):
        directive = line.strip()
        if directive.startswith("#if "):
            depth += 1
        elif directive == "#endif":
            depth -= 1
            if depth == 0:
                end = offset + len(line)
                return source[start:end], start, end
        offset += len(line)
    raise ValueError("Unterminated diagnostic guard.")


def source_block(source: str, signature: str) -> tuple[str, int, int]:
    """Find a C# brace block, ignoring comments and quoted literal braces."""
    if source.count(signature) != 1:
        raise ValueError("Expected one source signature: " + signature)
    start = source.index(signature)
    opening = source.index("{", start)
    index, depth, state, quote, verbatim = opening, 0, "code", "", False
    while index < len(source):
        char = source[index]
        following = source[index + 1] if index + 1 < len(source) else ""
        if state == "line":
            if char == "\n":
                state = "code"
        elif state == "comment":
            if char == "*" and following == "/":
                state = "code"
                index += 1
        elif state == "string":
            if verbatim and char == '"' and following == '"':
                index += 1
            elif char == quote:
                state = "code"
            elif not verbatim and char == "\\":
                index += 1
        elif char == "/" and following == "/":
            state = "line"
            index += 1
        elif char == "/" and following == "*":
            state = "comment"
            index += 1
        elif char in ("'", '"'):
            state, quote = "string", char
            verbatim = char == '"' and "@" in source[max(0, index - 2):index]
        elif char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return source[start:index + 1], start, index + 1
        index += 1
    raise ValueError("Unterminated C# source block: " + signature)


def fresh_external_output(requested: Path, repository: Path) -> Path:
    if requested.exists() or requested.is_symlink():
        raise ValueError("--output must not already exist.")
    output = requested.expanduser().resolve()
    if output.exists():
        raise ValueError("--output must not already exist after path resolution.")
    if output == repository or repository in output.parents:
        raise ValueError("--output must be outside the source repository.")
    if any((parent / ".git").exists() for parent in (output, *output.parents)):
        raise ValueError("--output must be outside all Git repositories.")
    return output


def resolve_dotnet(value: str) -> str:
    candidate = Path(value).expanduser()
    if candidate.is_file():
        return str(candidate.resolve())
    located = shutil.which(value)
    if not located:
        raise ValueError("Local dotnet executable not found: " + value)
    return str(Path(located).resolve())


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True, type=Path,
                        help="Fresh output directory outside all Git repositories.")
    parser.add_argument("--dotnet", default="dotnet",
                        help="Existing local dotnet executable or command (default: dotnet).")
    arguments = parser.parse_args(argv)
    tests = Path(__file__).resolve().parent
    repository = tests.parents[1]
    runtime = repository / RUNTIME_RELATIVE
    inputs = {
        "runtime": runtime / "LottieAnimation.Platform.cs",
        "enum": runtime / "LottieAnimation.cs",
        "tests": tests / "Program.cs",
        "project": tests / "HostContract.csproj",
        "runner": Path(__file__).resolve(),
    }
    try:
        for path in inputs.values():
            if not path.is_file():
                raise ValueError("Required canonical input is absent: " + str(path))
        output = fresh_external_output(arguments.output, repository)
        dotnet = resolve_dotnet(arguments.dotnet)
        before = {name: sha256(path) for name, path in inputs.items()}
    except (OSError, ValueError) as error:
        parser.error(str(error))
    output.mkdir(parents=True)
    receipt = {
        "scope": "Host source-sliced C# contracts; Unity/native APIs are stubs, not runtime acceptance.",
        "repository": str(repository),
        "inputs": {name: {"path": str(path), "sha256": before[name]} for name, path in inputs.items()},
        "dotnet": dotnet,
        "status": "running",
        "steps": [],
    }

    def save_receipt() -> None:
        (output / "result.json").write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")

    environment = os.environ.copy()
    environment.update({
        "DOTNET_CLI_HOME": str(output / ".dotnet-home"),
        "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
        "DOTNET_SKIP_FIRST_TIME_EXPERIENCE": "1",
        "DOTNET_NOLOGO": "1",
        "DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE": "true",
        "NUGET_PACKAGES": str(output / ".nuget-packages"),
        "NUGET_HTTP_CACHE_PATH": str(output / ".nuget-http-cache"),
        "MSBUILDDISABLENODEREUSE": "1",
    })

    def execute(name: str, command: list[str], timeout: int) -> str:
        log = output / (name + ".log")
        step = {"name": name, "command": command, "timeoutSeconds": timeout, "log": log.name}
        receipt["steps"].append(step)
        save_receipt()
        try:
            with log.open("wb") as stream:
                result = subprocess.run(command, cwd=output, env=environment,
                                        stdin=subprocess.DEVNULL, stdout=stream,
                                        stderr=subprocess.STDOUT, timeout=timeout, check=False)
            step["exitCode"] = result.returncode
        except subprocess.TimeoutExpired:
            step["timedOut"] = True
            raise RuntimeError(name + " exceeded its bounded timeout.")
        finally:
            if log.exists():
                step["logBytes"] = log.stat().st_size
                step["logSha256"] = sha256(log)
            save_receipt()
        if result.returncode != 0:
            raise RuntimeError(name + " failed; see " + str(log))
        if log.stat().st_size > MAX_LOG_BYTES:
            raise RuntimeError(name + " log exceeds the bounded read limit.")
        return log.read_text(encoding="utf-8", errors="replace")

    passed = False
    try:
        source = inputs["runtime"].read_text(encoding="utf-8-sig")
        enum_source = inputs["enum"].read_text(encoding="utf-8-sig")
        region, start, end = guarded_region(source)
        request, request_start, request_end = source_block(source, "        private void RequestTextureUpload()")
        backend, _, _ = source_block(enum_source, "    public enum LottieTextureUploadBackend")
        generated = ("// HOST ONLY: exact source slices; Unity/native APIs are stubs in Program.cs.\n"
                     "using System;\nusing UnityEngine;\nnamespace LottiePlugin {\n" + backend +
                     "\npublic sealed partial class LottieAnimation {\n" + region + "\n" + request + "\n}\n}\n")
        (output / "SourceSlice.generated.cs").write_text(generated, encoding="utf-8", newline="\n")
        shutil.copyfile(inputs["tests"], output / "Program.cs")
        shutil.copyfile(inputs["project"], output / "HostContract.csproj")
        (output / "NuGet.Config").write_text(
            "<configuration><packageSources><clear /></packageSources></configuration>\n", encoding="utf-8")
        receipt["slices"] = {
            "diagnosticLines": [source.count("\n", 0, start) + 1, source.count("\n", 0, end) + 1],
            "requestLines": [source.count("\n", 0, request_start) + 1, source.count("\n", 0, request_end) + 1],
            "generatedSha256": sha256(output / "SourceSlice.generated.cs"),
        }
        save_receipt()
        receipt["sdkVersion"] = execute("sdk", [dotnet, "--version"], 10).strip()
        project = str(output / "HostContract.csproj")
        execute("restore", [dotnet, "restore", project, "--configfile", str(output / "NuGet.Config"),
                            "--disable-parallel"], 60)
        execute("build", [dotnet, "build", project, "-c", "Release", "--no-restore", "--nologo",
                          "--disable-build-servers", "-p:UseSharedCompilation=false"], 60)
        executable = output / "bin/Release/net8.0/HostContract.dll"
        run = execute("run", [dotnet, str(executable)], 30)
        summaries = [line[7:] for line in run.splitlines() if line.startswith("RESULT ")]
        if len(summaries) != 1:
            raise RuntimeError("Expected exactly one host contract RESULT record.")
        summary = json.loads(summaries[0])
        names = summary.get("tests", [])
        if (summary.get("passed") != EXPECTED_TESTS or summary.get("failed") != 0 or
                len(names) != EXPECTED_TESTS or len(set(names)) != EXPECTED_TESTS):
            raise RuntimeError("The complete 33-test host inventory did not pass.")
        if not str(summary.get("runtime", "")).startswith("8."):
            raise RuntimeError("Expected execution on a locally installed .NET 8 runtime.")
        receipt["hostResult"] = summary
        receipt["executableSha256"] = sha256(executable)
        passed = True
    except (OSError, ValueError, RuntimeError) as error:
        receipt["error"] = str(error)
    finally:
        after = {name: sha256(path) if path.is_file() else None for name, path in inputs.items()}
        changed = [name for name in inputs if after[name] != before[name]]
        receipt["sourceUnchanged"] = not changed
        receipt["changedInputs"] = changed
        receipt["status"] = "passed" if passed and not changed else "failed"
        save_receipt()
    print(str(output / "result.json"))
    if receipt["status"] != "passed":
        print(receipt.get("error", "Canonical inputs changed during the host proof."), file=sys.stderr)
        return 1
    print("33 host contracts passed; Unity/native/browser acceptance remains separate.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
