# Selective marker parsing candidate — 2026-10-26

This source-only candidate is staged externally against ROOT's supplied package
head `bcfd77cab522017f75ddf2710291cbc2d3e07ce2`, matching the consumer pin at
assignment. The canonical checkout, dependency submodules, native binaries and
untracked startup-trace cache are unchanged by the worker. Base-file hashes,
candidate files, patch, host source and raw report are outside the repository in
`C:/Users/khind/Documents/Codex/evidence/game-polish-2026-10-25/marker-parser-stage/`.

The old metadata parser builds a complete dictionary/list/string/boxed-number DOM
for every shape although its consumer uses only root `fr`, `ip`, `op`, `markers`
and each marker's `cm`, `tm`, `dr`. The candidate materializes those fields only.
It still reads and validates every ignored value and object key. Reusable
per-object-depth hash sets store source-slice value types; equality compares
decoded UTF-16 characters after the hash check. Private process-salted 64-bit FNV
accumulation and final mixing avoid predictable polynomial collision families;
hashes never decide equality alone. The salt is created once from existing core
Guid APIs, not per object/key. Escaped-equivalent keys therefore
remain duplicates, distinct colliding keys remain valid, and nested/sibling scopes
remain independent. A shared active-token stack removes only each completed
object's keys. It avoids repeatedly clearing a prior wide sibling's entire table
capacity when subsequent objects are small. Scratch state dies with the parser;
no resource-path/digest/global document cache is introduced.

Ignored strings and ordinary ignored numbers do not allocate value objects.
Required numbers retain the exact existing `double.TryParse` conversion. The
skipped-number fast path is limited to valid ASCII tokens of at most 128
characters and exponent magnitude at most 128, which guarantees a finite result
below 1e256. Exceptional numeric tokens use the old substring/TryParse routine,
preserving runtime-specific overflow and Unicode-digit behavior. Unicode escapes
use the normal four-hex-digit path without allocation; uncommon forms delegate
to the original `HexNumber` conversion, including its whitespace quirks and lone
surrogate behavior. No new validity, width or depth ceiling is imposed.

`ParseDocument`, frame mapping, marker validation, public API, string-instance
ConditionalWeakTable cache and per-owner disposal behavior are unchanged. In
particular, missing/null/wrong-type `markers` retains its existing empty-list
behavior, while array entries and selected field types stay strict. Unknown
subtrees still reject decoded duplicate keys, bad escapes/numbers/commas and
trailing data before metadata validation. No Span APIs, runtime dependencies,
nullable syntax or newer language feature is added to the runtime source.

## External validation

The final .NET 8.0.15 differential run passed **4,570 comparisons** of old and new
public parsing outcomes, including exact exception type/message, marker metadata,
frame samples and durations. Its immutable 140-file snapshot contains every
current consumer animation JSON plus six package fixtures. All 140 files were
compared, including the non-animation manifest rejected by both parsers. The run
also covers malformed/adversarial JSON, escaped keys/lone surrogates, legacy hex
whitespace, exceptional numbers, deterministic mutations/truncations, a 20,000-key
object, that wide object followed by 20,000 tiny siblings, and 16,384 equal-length
Aa/BB-family keys that all collide under a polynomial-31 hash. The last case is
595,177 characters and completed in 2.62 ms with the candidate versus 3.52 ms old
in this single host run. This is a liveness sanity check, not device timing proof.

All **29 cases** in the actual candidate marker NUnit fixture passed under the
host's minimal Resources/TextAsset adapter: seven existing cases and 22 new strict
selection, key-collision/scope, typing and owner-disposal cases. This is managed
host execution, not a Unity/native test receipt.

For each allocation measurement, an equal fresh nonempty JSON string is created
before the measured interval to force a cache miss. Live deliberate controls
observed 65,560 allocated bytes before and after. The 139 valid real corpus parses
allocated 886,465,224 bytes with the old parser and 2,247,232 with the candidate.
The actual gardener JSON fell from 8,585,928 to 27,256 bytes; willow from 10,301,504
to 21,760. These figures exclude input-copy and outcome-report construction and
apply to this .NET host, not Unity/WebGL. Very wide objects still require a key
index; no zero-allocation parsing claim is made. The 20,000-key/sibling stress
completed without a capacity-clear blowup.

The report separates those real-corpus totals from synthetic stress: the three
wide/collision stress cases add 26,872,240 old bytes and 10,977,728 candidate bytes.
The combined report total is therefore 913,337,464 old / 13,224,960 candidate;
it must not be confused with the real-corpus subtotal. Separate compilation of
the actual candidate runtime source with C# 7.3 succeeded without warnings/errors
against the host's framework references; that is a language check, not an actual
Unity 2019.4 API/runtime receipt.

The first host attempt corrected its empty-string copy assertion: .NET interns
empty strings, and parsing rejects them before cache lookup. An intermediate run
saw a temporary 64-file build projection; the final run uses an external snapshot
captured after ROOT restored all 140 source files. No projected subset is claimed
as the complete corpus.

ROOT owns independent review, integration, package/consumer pin changes and Unity
qualification. The package fixture filter is
`LottiePlugin.Tests.Runtime.LottieMarkerSetTests`. Unity 2019.4 compatibility and
the current consumer's actual fresh-board parse/allocation/timing require their
real builds/runtimes. Actual UI callback, native resource setup, image upload,
save transactions and other cold allocations remain outside this parser proof.
No native ABI/binary change, public deployment or universal zero-GC claim follows.

## Canonical native qualification

ROOT ran the existing marker fixture in the canonical plugin project with its
declared Unity 2022.3.62f3 before integration: seven cases passed, zero failed or
skipped. After independent source review and guarded integration, the same filter
executed all 29 cases: 29 passed, zero failed or skipped. Both external runner
receipts report no changed tracked inputs and no restored Editor inputs.
The unrelated pre-existing Python cache directory was preserved. These results
qualify this native managed fixture only; Unity 2019 and the current consumer's
WebGL payload and device allocation/timing remain separate pending evidence.
