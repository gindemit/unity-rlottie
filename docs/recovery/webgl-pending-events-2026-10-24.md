# WebGL pending upload events — 2026-10-24

Owner resumed release qualification and authorized Android Chrome testing and
evidence-backed fixes. ROOT owns Git, Editor/builds, browser, phone and ports.

Normal Pocket Bloom candidate 0734ec2 with renderer def31c353 takes 41.297 seconds
on fresh local origin and 40.954 seconds on warm reload on Samsung SM-N975F.
Separate 15-second CPU diagnostics spend 10.803 seconds in getError. Exact normal
WASM function 18867 references Unity native-plug-in error-check strings; it is
distinct from the renderer upload callback at function 60037. WebGL upload pump
issues its full MaxUploadsPerFrame budget (minimum 32) even with no pending work;
other platforms already clamp their budget to a non-consuming pending count.
The earlier diagnostic first-64 trace contains noPending callbacks.

Raw evidence stays outside repositories under
`C:/Users/khind/Documents/Codex/evidence/crazygames-analytics-2026-10-23/resume-owner-2026-10-24/`.
CPU profiling is diagnostic, not accepted loading timing. No claim that all GL
query cost belongs to the renderer, or that this fix alone meets release gates.

## Selected change

Expose a non-consuming count of pending WebGL texture states, with matching native
and managed ABI. Clamp WebGL event budget to min(pending, configured budget).
Keep one upload per callback, all GL error/state checks, native availability,
registration/recovery/fallback, and non-WebGL behavior unchanged. Do not replace
the native pending snapshot with a managed wakeup flag. Preserve pending work
published after a poll for the next tick; count must not consume or clear it.

## Validation and delivery

Add focused native cases for empty/multiple/replaced/consumed/destroyed pending
states and non-consuming repeated counts. Add managed pump cases for zero,
below-budget and above-budget WebGL work plus retry/next-tick publication.
Run existing affected native trace and managed pump regression checks.
Fresh nonauthor review precedes committing/pushing and consumer adoption.
Build matching WebGL archive and consumer with exact source/hash receipts; retain
the old normal candidate unchanged. Compare physical foreground loading/frame
captures, verify visible uploads, input, reload/resume and recovery coverage.
Trace/color and full release acceptance remain separate; no portal submission,
public telemetry, production deployment or unrelated platform change is selected.
