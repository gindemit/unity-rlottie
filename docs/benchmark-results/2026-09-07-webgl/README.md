# WebGL browser validation — 2026-09-07

## Result

Chrome 152 created the requested WebGL contexts and exercised the native upload
entry points:

| Target | Unity | Shipped archive | Browser result |
|---|---|---|---|
| WebGL 2 / OpenGL ES 3 | 6000.5.3f1 | `WasmExceptions` | Passed native initialization and continuous frame rendering |
| WebGL 1 / OpenGL ES 2 | 2019.4.41f2 | `Legacy` | Failed before animation startup: missing `lottie_get_webgl_render_event_func` |
| WebGL 1 / OpenGL ES 2 | 2019.4.41f2 | Unity-2019/Fastcomp monolithic test archive | Passed native initialization and continuous frame rendering |
| WebGL 2 / OpenGL ES 3 | 2021.3.45f2 | `Legacy` | Failed to link because the archive references incompatible libc++ symbols |

The passing WebGL 2 run logged `Creating WebGL 2.0 context`,
`Unity-owned WebGL native upload enabled`, and repeated successful rendered
frames without an abort. The test-only WebGL 1 archive produced the equivalent
WebGL 1.0/OpenGL ES 2.0 evidence. The checked-in WebGL 1 configuration therefore
remains broken even though the renderer and native upload implementation work
when rebuilt with the matching legacy toolchain.

These are basic native-render browser smokes, not full per-check JSON results.
The WebGL player stores its result under browser IndexedDB and the current
runner cannot assert that persisted payload. Gamma/Linear comparison,
managed-fallback testing, and packaging compatible archives for each supported
Unity Emscripten ABI remain outstanding.

## Reusable runner

`scripts/ci/run-webgl-browser-smoke.ps1` builds a selected WebGL version, serves
it with the local Node server, launches a clean headless Chrome profile, and
requires the requested context, native-upload marker, a successfully rendered
frame, and no runtime abort.

Local logs and build products are retained outside the repository under:

`C:\Work\git\gindemit\unity-rlottie\results\webgl-browser-20260906`

