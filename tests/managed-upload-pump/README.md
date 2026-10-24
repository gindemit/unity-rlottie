# Upload pump scheduling harness

Run `dotnet run --project tests/managed-upload-pump/UploadPumpTests.csproj`.
Repeat with `-p:DefineConstants=UNITY_WEBGL` and `-p:DefineConstants=UNITY_IOS`
to check the separate WebGL callback path and native iOS queue polling.

The harness compiles the actual `LottieUploadPump.cs` against small Unity and
native bridge doubles. It verifies callback submission counts, frame caps,
repeated polling after empty/retry snapshots and stable event ID. The WebGL
player double exposes only the separate WebGL pending query, so accidental calls
to the native queue query fail compilation. All paths run the same scheduling
checks. The trace-symbol variant also checks that only submitted events reserve
trace metadata.

This is scheduling evidence only. The native ABI suite checks the DLL query
export; the native queue fixture checks production queue code with a synthetic
lifetime registry, including deduplication, removal and modeled retry requeue.
These tests do not prove GPU uploads, actual instance disposal, backend
retry behavior, frame timing, visual continuity or IL2CPP player compatibility.

The query applies to all native players, including statically linked iOS, and
the Editor. C# and native libraries must be delivered together with the current
API; missing libraries or symbols are integration errors and propagate normally.
WebGL polls its separate non-consuming pending-state query and applies the same
configured frame cap.

Arrivals after the snapshot and retried uploads can wait until the next
`LateUpdate`. The snapshot is advisory: render-thread consumption may leave a
submitted callback with an empty queue. Work still uses the existing callback,
event ID and upload/lifetime path.
