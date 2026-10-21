# Codex Workspace Guide

This file is for Codex agents working in this workspace.
Goal: provide stable, low-noise context about structure and conventions.

## Current owner override — 2026-10-21

Use only main in C:/Work/git/gindemit/unity-rlottie/unity-rlottie, per workspace
plans/REPOSITORY-CONSOLIDATION-2026-10-21.md. Commit/push coherent reviewed steps;
retire parallel source clones after verified preservation. This supersedes the
historical dev/adjacent-clone instructions below. Keep runtime/release gates.

## Workspace Layout

- `unity-rlottie/`: primary repository (default edit target unless user says otherwise).
- `unity-rlottie-2019.4.41f2/`, `unity-rlottie-2021.3.45f2/`, `unity-rlottie-6000.2.13f1/`, `unity-rlottie-6000.3.7f1/`, `unity-rlottie-6000.3.7f1-hdrp/`, `unity-rlottie-6000.3.7f1-urp/`: Unity-version variants.
- `unity-rlottie_webgl/`: WebGL-focused variant.
- `scripts/`: local automation for version setup and Unity test runs.
- `results/`: generated test/build outputs and archived runs (do not treat as source code).
- `doc/`: reports/notes.

## Default Working Rules

- If the user does not specify a target repo, edit only `unity-rlottie/`.
- When mentioning a local file in a response, print its full browser-openable
  URL as literal inline code in the form
  `file:///C:/Work/path/to/file.ext`. Apply this to HTML, Markdown, text,
  source, and other local files. Do not use a Markdown link, a relative path,
  a path beginning with `/C:/`, or only the filename: Orca can strip the drive
  letter from those forms. The literal `file:///C:/...` form can be copied into
  the browser and preserves the complete Windows path.
- Use only the `dev` branch for shared work in `unity-rlottie/`. Do not commit,
  merge, cherry-pick, push, or otherwise modify `main` unless the user explicitly
  overrides this rule in the current request.
- Treat every push to `dev` as a synchronized release to all `unity/**` branches.
  Do not run a standalone `git push origin dev`; after committing on `dev`, run
  `scripts/sync-unity-branches.ps1` from `unity-rlottie/`. The script pushes
  `dev`, merges it into every adjacent Unity-version/render-pipeline clone, and
  pushes each corresponding `unity/**` branch.
- If any Unity branch fails to synchronize, report the failure and leave the
  task incomplete until it is fixed or the user explicitly accepts the partial
  synchronization.
- Do not edit files under `results/` unless the user asks for report processing.
- Prefer minimal, localized changes and preserve existing architecture.

## Structure Inside Each Repo Copy

- `src/`: native C++ plugin implementation.
- `projects/CMake/`: CMake build logic for native binaries.
- `projects/AndroidStudio/`: Android sample/project scaffolding.
- `unity/RLottieUnity/Assets/LottiePlugin/Runtime/`: runtime C# code.
- `unity/RLottieUnity/Assets/LottiePlugin/Editor/`: Unity editor tooling.
- `unity/RLottieUnity/Assets/LottiePlugin/Tests/Runtime/`: Unity runtime tests.
- `unity/RLottieUnity/Assets/LottiePlugin/Plugins/`: platform plugin binaries.
- `dependency/`: third-party code/submodules (`rlottie`, toolchains, etc.).

## Coding Conventions

- Follow `.editorconfig` in each repo copy (C# style, naming, formatting).
- Keep Unity `.meta` files in sync when adding, moving, or deleting Unity assets.
- Avoid changing dependency submodules unless the task requires it.
- Keep public runtime behavior backward compatible unless user asks for a breaking change.

## Validation Commands

- Single repo tests:
  - `scripts\run-tests.cmd`
  - `scripts\run-tests.cmd --repo-dir "<repo-path>" --batch`
  - `scripts\run-tests.cmd --repo-dir "<repo-path>" --unity "<Unity.exe>" --out-dir "<results-dir>"`
- Multi-version sweep (installed Unity editors):
  - `scripts\run-tests-all-unity.cmd`
  - `scripts\run-tests-all-unity.cmd --batch`

## Practical Target Map

- Runtime C# changes: `unity-rlottie/unity/RLottieUnity/Assets/LottiePlugin/Runtime/src/`
- Editor tooling changes: `unity-rlottie/unity/RLottieUnity/Assets/LottiePlugin/Editor/src/`
- Native plugin changes: `unity-rlottie/src/`
- Native build updates: `unity-rlottie/projects/CMake/`
