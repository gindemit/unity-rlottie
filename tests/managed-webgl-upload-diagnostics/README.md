# Managed WebGL upload diagnostic host contracts

These 33 executable contracts use the **actual canonical C# diagnostic region,
RequestTextureUpload method and backend enum**. They do not launch Unity or a browser.

Run from any working directory using an existing local SDK and .NET 8 reference/runtime packs:

```text
python tests/managed-webgl-upload-diagnostics/run_contract.py --output <fresh-external-directory>
```

Add `--dotnet <existing-local-dotnet-executable>` when needed. In environments requiring
RTK, prefix the command with `rtk proxy`. The output directory must not exist and must
be outside Git repositories. The runner never fetches, edits source or package caches,
or installs an SDK. NuGet sources are cleared. Missing local framework packs fail offline.

The runner copies the versioned test program/project to the fresh output, extracts exact
source slices there, and uses that directory for all generated source, restore/build files,
CLI home, caches and logs. Restore/build/run timeouts are 60/60/30 seconds; SDK detection
has a 10-second timeout. Large logs fail the bounded read limit. The receipt records input,
generated-source, log and executable hashes, command arguments, actual results and whether
canonical inputs stayed unchanged. Missing, duplicate or incomplete RESULT inventories fail.

## Explicit stubs and evidence limits

Program.cs stubs Unity Application/Time/Texture2D/JsonUtility/startup attributes,
native availability/request functions, upload pump, registration/fallback effectors,
and the remaining animation shell. Native failure results are test stimuli.
The sliced request/gate/recorder logic itself is unchanged.

JsonUtility uses System.Text.Json with fields included. This is **not** Unity serializer
coverage. Manual invocation of the sliced startup method is **not** proof of Unity startup
ordering. The background type-use case covers added sliced initialization, not unrelated
existing animation initialization. Initial constructor registration-fallback instrumentation
outside RequestTextureUpload is not exercised.

Cases cover URL/origin opt-in, duplicate/conflicting/encoded parameters, 0/1/8/>8 episodes,
dropped/instance/counter meanings, silent recovery, throwing Apply placement, fallback,
request rejection, disabled-gate effects and read-only export.

These results are host C# contract evidence. Owning package compilation, actual Unity
serialization/startup, native callback/error attribution, real WebGL recovery, graphics and
release acceptance require their separate source/artifact-bound checks.

Do not version SourceSlice.generated.cs, logs, receipts, bin/obj, caches or other outputs.
