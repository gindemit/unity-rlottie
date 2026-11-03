# Reuse preparation batch buffers

The WebGL first-run investigation found repeated exact-size frame-index and
managed result-pixel allocations in parallel frame-cache preparation. Consecutive
equal-size batches now reuse those arrays after complete consumption. Short clip
tails retain exact lengths. Native result allocation stays inside the admitted
worker task; browser allocation stays on its existing caller path. Pending starts
cannot overwrite active requests, and disposal still joins native work.

This is a managed-source improvement with unchanged native binaries, archive
hashes, worker protocol and package version. It does not add a new threading
backend. Existing browser workers use independent CPU-only Wasm; Unity GPU uploads
remain on the main thread. Progressive game detail does not use this cache path.

Each unfinished cache may retain one completed result buffer until reuse or
disposal. The pool's pending-byte counter measures in-flight reservations, not
all retained managed residency. Reuse removes repeated allocations; no measured
working-set reduction or universal frame-time gain is claimed.

Independent review caught a first revision moving native buffer allocation to the
caller; the accepted revision restores worker-side allocation. Host bridge and
real-Wasm content tests passed 11/11. Graphics-enabled Unity 2022.3.62f3 PlayMode
ran seven cases: six passed and the new index-tail fixture failed because both
clips divided evenly by its batch size. The test-only correction uses batch size
two and passed its exact native rerun 1/1. The unchanged six passing cases include
real native parallel/serial pixel equivalence, result-buffer reuse/length/overlap,
fallback and lifetime behavior. Raw XML/logs are retained externally under
`C:/Work/scratch/webgl-first-run-2026-11-03/plugin-native/` and
`plugin-tail-rerun/`. No browser/device worker execution is certified by those
native results.
