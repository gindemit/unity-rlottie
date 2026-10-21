# Upload pump scheduling harness

Run `dotnet run --project tests/managed-upload-pump/UploadPumpTests.csproj`.
Repeat with `-p:DefineConstants=UNITY_WEBGL` and `-p:DefineConstants=UNITY_IOS`
to check that static-link players preserve their existing callback path.

The harness compiles the actual `LottieUploadPump.cs` against small Unity and
native bridge doubles. It verifies callback submission counts, frame caps,
repeated polling after empty/retry snapshots, stable event ID and compatibility
with older dynamically loaded native libraries. Static-link player doubles omit
the new query so accidental imports from the pump fail compilation.

This is scheduling evidence only. The native ABI suite checks the DLL query
export; the native queue fixture checks production queue code with a synthetic
lifetime registry, including deduplication, removal and modeled retry requeue.
These tests do not prove GPU uploads, actual instance disposal, backend
retry behavior, frame timing, visual continuity or IL2CPP player compatibility.

The new query applies to dynamically loaded native players and the Editor.
WebGL and iOS players retain fixed-budget polling so existing static libraries
do not acquire an unsatisfied linker reference. Dynamic libraries without the
new export also retain fixed-budget polling and emit one warning per pump.

Arrivals after the snapshot and retried uploads can wait until the next
`LateUpdate`. The snapshot is advisory: render-thread consumption may leave a
submitted callback with an empty queue. Work still uses the existing callback,
event ID and upload/lifetime path.
