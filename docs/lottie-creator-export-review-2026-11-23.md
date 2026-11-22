# Reviewing Lottie AI and Creator MCP exports with Unity RLottie

## Replay and slides

LottieFiles' November 19 replay runs 1:17:42, including the opening countdown. I reviewed the slides in the replay. The video description links to the products and social accounts. A separate downloadable slide deck did not surface.

| Replay section | What the demonstration shows | Implication for local authoring |
| --- | --- | --- |
| Lottie AI, around 22:20–28:57 | The presentation frames the output as editable vector code. The demo shows Turbo and Quality generation, a fintech loading-animation brief, timeline preview, controls, and multiple exports. | Preserve the prompt, intended dimensions, timing and editable source. Check the exported JSON on the actual renderer after each targeted edit. |
| Creator MCP workflow, around 41:06 | The presenter describes the agent writing code, the engine running it, and the result coming back as an animation, video or file. | Give a local authoring agent the same feedback loop with a renderer receipt. Compare the result of executing the export against the original brief instead of evaluating its JSON alone. |
| Medieval Town Square, around 42:45 | The cathedral revision adds architectural detail to a named part of an existing scene. The slide pairs close-up before/after images with the original ask and the changes. | Identify the layer and the expected change in a revision. Preserve a reference for surrounding pixels and for the intended changes. |
| Medieval Town Square, around 45:00 | The finishing slide compares the graded scene with the finished treatment, adding haze, long shadows, light and glows. | Compare exported alpha against light and dark backgrounds and check shadow clipping. The slide shows an appearance, so its implementation and the fidelity of any export remain unverified. |
| Creator MCP and Q&A | The Creator MCP segment shows multiple rich scenes playing alongside the speaker. The Q&A follows the two product demos. | Capture reference frames at the same times for which the local renderer returns pixels. A successful authoring preview says little about how much of the scene a different renderer will draw. |

[Replay](https://www.youtube.com/watch?v=LqprbIyFU4Y) · [Cathedral example](https://www.youtube.com/watch?v=LqprbIyFU4Y&t=2565s) · [Finishing example](https://www.youtube.com/watch?v=LqprbIyFU4Y&t=2700s)

## Feature implications

| Feature | Where it fits here |
| --- | --- |
| Lottie AI prompt, SVG input, Turbo/Quality, controls and Frolic source | Use it to explore candidate motion and tune parameters. Frolic is procedural authoring source that compiles to an export. The local renderer consumes Lottie animation JSON. Preserve source, export hash and a reference clip so edits can be compared. |
| Creator MCP persistent scenes, JavaScript edits, revisions and rollback | The transferable idea is a small edit to a named layer, followed by render evidence and a reviewable revision. The plugin has named keypaths for semantic color overrides. Preserve names during export and validate those overrides where used. |
| Creator previews, video exports and still filmstrips | Reference images help identify missing shapes and unstable timing. Compare identical timestamps and canvas sizes. Compare alpha independently of RGB and inspect composited output on the intended surface. A filmstrip cannot establish motion cadence, a clean loop seam or audio synchronization. |
| Shapes, strokes, gradients, trim paths, masks and precompositions | Basic vector shapes and transforms are a practical starting point. Inspect gradient edges, alpha and precomp clipping. A Pocket Bloom comparison previously found shadow geometry outside a source canvas when composing nested plant clips. |
| Text, blur, shadows, color effects, expressions, skew and merge paths | The pinned player can silently drop features. Export text as actual shape layers when an outlined label is appropriate. Use Unity text for localized labels. Check apparent effects individually and build a compatible authored treatment if the native render differs. Creator's text export can contain glyph outlines inside a text layer, which is different from path layers. |
| Named segments, triggers, themes, audio and dotLottie packages | Prefer verified JSON markers for milestones and Unity logic for state and audio. Treat dotLottie state machines, package resources and runtime themes as a distinct integration with its own performance and behavioral tests. A ZIP container or an exported video does not add those features to rlottie. |

[Lottie AI](https://docs.lottiefiles.com/en/lottie-ai) · [Lottie AI exports](https://docs.lottiefiles.com/en/lottie-ai/export) · [Creator MCP capabilities](https://docs.lottiefiles.com/en/creator-mcp/reference) · [Creator MCP preview](https://docs.lottiefiles.com/en/developers/creator-mcp/preview) · [Samsung feature matrix](https://github.com/Samsung/rlottie/blob/master/README.md#supported-after-effects-features) · [Lottie specification](https://lottie.github.io/lottie-spec/latest/single-page/) · [dotLottie v2](https://www.dotlottie.io/spec/2.0/)

## What changed

Run the new local review command from the plugin repository. It accepts one trusted, self-contained vector export. Python 3.9 or later is required. Pillow is required for the optional Windows x64 native filmstrip.

```powershell
python scripts/review-lottie-export.py C:/exports/accent.json --output C:/review/accent-v1 --native unity/RLottieUnity/Assets/LottiePlugin/Plugins/Windows/x86_64/LottiePlugin.dll
```

The output directory must be new. The report includes content hashes, source and renderer frame bounds, markers, known feature issues with JSON paths, and the explicit native DLL identity when used. The filmstrip preserves transparency, labels sampled source frames, and includes the first and last drawable frames even for markerless clips. The report records raw premultiplied BGRA hashes, visible-pixel counts and sample timings. Compare an authoring reference separately. An entirely transparent sample set returns `needs-review` because some legitimate clips only draw between samples.

The pinned parser dispatches a shape when it sees `ty` and skips fields before it. Astra tested a reordered rectangle with the packaged WebGL worker. It fell from 144 visible pixels to zero, although loading and rendering both returned success. The Windows regression fixture independently went from 240 visible pixels to zero per frame. Run with `--normalize-shape-order` to write a separate `normalized.json` and review that copy. Only recognized shapes with relevant fields before `ty` are reordered. Other diagnostics still apply, the original stays intact, and the report records both content hashes. In the Windows fixture the recovered pixel hashes matched the original render exactly.

Exit status 0 means the file was inspected or sampled. Status 2 means the report surfaced a known review condition. Status 1 means reading, validation, rendering or output failed. The rules are a conservative authoring profile. The command runs offline and has no Unity hooks. It is not a general Lottie schema validator or a sandbox for untrusted content.

## Engine and Pocket Bloom follow-up

- **Parser correctness.** A native fix would make shape dispatch independent of field order. Qualify it against the pinned evaluator on Windows and WebGL, including type-first and type-last fixtures, nested groups, default and reversed path directions, and pixel hashes on existing authored content. It requires synchronized native artifacts across supported platforms. The offline copy is a practical correction for current builds.
- **Mature-garden startup.** The physical Note10+ comparison kept 1,029 poses and improved median paired reopen by 3.130 s for new documents and 4.056 s for warm reloads. Candidate warm frame p95 reached 100.1 ms in two samples compared with 83.5 ms on the control, so the candidate stayed unqualified. Measure raster, buffer copies, texture upload/compression and yields separately before changing the budget. Compare time to playable, p95 and intervals over 100 ms on matched warm and cold runs. Preserve pose admission and save semantics. The workspace receipt is `docs/work/pocket-bloom-weekend-2026-11-21/FOLLOWUP-RUN02-RESULTS.md`.
- **Staging copies.** The native plugin already copies worker preparation output directly into its checked batch slice. The Pocket Bloom synchronous consumer still stages and copies a full BGRA buffer per frame, or 1 MiB at 512 × 512. A direct synchronous path would need checked slices, exact alpha and non-square pixel parity, sentinel guards, unchanged destination on rejected frames, and a measured improvement on the target. Host raster times in this review do not measure those costs.
- **First win and WebGL workers.** Reusing the animation source saved 0.4 ms median against a predeclared 16.7 ms target. The larger stall remains unattributed. Live WebGL rasterization is synchronous even through the future entry point. Existing browser workers serve frame-cache batches and have resource and color-override limits. Moving live playback needs bounded queues, backpressure, stale-completion and target-recreation handling, plus latency and responsiveness evidence on the real browser. Profile the call path before changing scheduling.
- **Cache and payload size.** Historical animation pruning cut raw animal JSON from 6.18 to 2.37 MB but saved only about 12.4 KB of isolated Brotli output. Measure parser/preparation time and pixel parity before treating that as a runtime win. Keep Pocket Bloom's alpha-preserving platform compression policy for cached assets and verify actual format, bytes and fallback on the target. External evidence is tracked in the workspace `docs/work/ANIMATION-FONT-SIZE-AUDIT-2026-10-26.md`.

## Verification

Luna traced the physical performance evidence. Terra traced the synchronous consumer and native bridge. Sol validated the existing Pocket Bloom native review and package pinning tools with 7 and 13 passing tests. Astra independently reproduced the ordering bug in the WebGL worker and reviewed the implementation. The follow-up corrected fractional in/out rounding, default ellipse direction handling, and structural checks for a missing version and ambiguous JSON.

- `python -B -m unittest discover -s tests/lottie-export-review -v` passed all six tests, including native fractional frame selection, partial alpha, blank output on reordered geometry, pixel-identical recovery, and the CLI's copy and hash reporting.
- Static review completed on 108 bundled animation compositions. It reported 89 inspected and 19 with review findings.
- The checked-in Patch Pal neutral export from the animation lab produced eight distinct, nontransparent sample frames at 160 × 160 using the canonical Windows x64 DLL. This is a local sample rather than a new LottieFiles generation.
- This change affects offline review tooling. No runtime binaries, GPU upload paths, game assets or review builds were changed or qualified.
