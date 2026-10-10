# Optional parallel frame preparation

`LottieFrameCache` remains the only sampling/texture cache. The existing
three-argument constructor and `Preparation = Serial` default retain serial
behavior. To opt in, set `Preparation = LottieFramePreparation.Parallel` and
pass an explicitly owned `LottieFramePreparationPool` as the fourth argument.
Share one pool across a palette. Dispose all caches before disposing the pool;
premature pool disposal throws without invalidating its outstanding work.

Pool defaults are two CPU workers, eight frames per batch, a shared 64 MiB
pending-pixel budget and a five-second browser timeout. Valid limits are 1–8
workers, 1–32 frames, 4 bytes–64 MiB pending pixels, and 0.1–60 seconds timeout.
Parallel dimensions support 1–4096 on each axis (including rectangular surfaces),
within Unity's surface limits and checked batch byte budget. Unsupported parallel
surface/batch combinations recover to serial. `MaximumRawPixelBytes` still
rejects an oversized complete cache before scheduling or allocating result
buffers. The pending budget covers batches waiting for CPU/upload; it does not
claim a bound on total process memory, renderer surfaces, models or textures.

Native tasks own separate rasterizers and surfaces, share the pool's semaphore,
and return premultiplied BGRA buffers. Warmup polls without waiting; only explicit
disposal joins pending native work. Faulted tasks are observed before fallback.
Unity creates, uploads, publishes and destroys textures on the cache's creating
thread. `WarmStep(maxFrames)` uploads at most that many frames. The canonical
plan still owns marker mapping, deduplication, sample positions, loops and exact
endpoints. Readiness requires every texture upload. Playback reuses immutable
textures and releases preparation resources once ready.

For WebGL, wrap the build in `LottiePlugin.Editor.LottieRasterBuildScope(target,
parallel)`. If `BrowserWorkersIncluded`, add `LOTTIE_PARALLEL_PREPARATION` to
`BuildPlayerOptions.extraScriptingDefines`; call `CopyTo(playerDirectory)` after
the build succeeds and dispose the scope in a finally/using block. The scope
validates the payload and selected archive hashes, restores bridge compatibility,
and removes only known stale package sidecars when used for serial builds.
The default bridge importer excludes all platforms. Serial and Android builds
ship no browser sidecar. Older Unity 2019/2021 archive variants currently use
serial preparation and omit the browser symbol/payload. Explicit parallel
requests without an enabled browser payload report compatible serial fallback.

Workers use independent CPU-only WASM runtimes, no pthreads, SharedArrayBuffer or
new cross-origin isolation. URLs derive from the document base and work below
a host subdirectory. Owner/request identities, expected byte lengths and slot
identity fence late/corrupt replies. Failure or pending retirement terminates
the shared slot and makes its other owners fall back; pool disposal terminates
all slots. CSP, import/WASM errors, malformed results and stalls use serial
without changing security policy. Published textures survive fallback.
Browser external resource paths and semantic color overrides use serial with a
diagnostic because the narrow C ABI cannot apply those options. Native parallel
supports both through the independently owned canonical rasterizer.
`FellBack`, `FallbackReason` and `ParallelFrameCount` distinguish actual
parallel completion from recovery; they do not synthesize readiness.

Sidecars live in each selected archive's inert `RasterWorker~` directory. The
shared stager selection is authoritative: Legacy uses Unity 2022.3.62f3's
Emscripten 3.1.8 linker; WasmExceptions uses Unity 6000.5.3f1's 4.0.20-git linker
with `-fwasm-exceptions`. Link with `scripts/build-raster-worker.py` using that
variant's exact `librlottie.a.bytes`. Do not link against inactive top-level
archives. The native delivery workflow rebuilds these sidecars from the same
selected archive bytes with the matching emsdk versions, includes licenses and
records compiler, renderer dependency commit, wrapper and emitted file hashes.
Worker heaps start at 16 MiB and cap at 128 MiB to accommodate validated larger
pixel batches; that cap does not reserve 128 MiB at startup.

Run `node --test scripts/test-raster-bridge.mjs scripts/test-raster-content.mjs`
for bridge identity/failure checks and a real two-animation WASM content-key
regression. `LOTTIE_WORKER_ROOT` selects an explicit sidecar for the latter.
Run `python scripts/qualify-raster-pixels.py --package PACKAGE --fixtures FIXTURES
--output NEW_DIRECTORY` for fresh Windows native/WASM differential hashes.
Package Unity tests cover both alpha modes, several sizes, canonical sampling,
upload budgets, failure/malformed results, pending disposal and validation.
Browser, Android build and physical-device qualification remain separate.
