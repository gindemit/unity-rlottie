# One main working checkout — 2026-10-21

The owner explicitly authorized consolidation onto `main`, frequent reviewed
GitHub source backup and retirement of redundant working clones. Canonical path:
`C:/Work/git/gindemit/unity-rlottie/unity-rlottie`. This supersedes the old `dev`
and adjacent Unity-version source-clone workflow; immutable release tags and
historical compatibility evidence are preserved.

The old local main `fd0f043` contained an unfinished merge of remote main
`475d1d4` whose resolved index matched the old local tree. Its original history,
submodule position and merge metadata were privately backed up before explicitly
closing that historical resolution. Current source is selected exactly from
reviewed `.251` delivery `3c884ab4d492bd5dc301735fed48dc86668e08fb`, tree
`328bab09a275fecf4f400f035de55a160a6a13b2`, before these workflow documents.
The old local registry Test Framework compatibility change is already present
in `.251`; do not downgrade the current manifests to recover it.

The selected dependency is `rlottie` commit
`e9bf55b786c33ad2291d35a0e6885dde5f988c1d`. The previous clean detached dependency
`3840c6e33e56b15df4e8b1b5d4639579b2d5677e` has a verified private bundle, and its
history is retained by the dependency repository. No native source or binaries
are rebuilt by this consolidation; existing `.251` consumer pins remain valid.

Older Unity-version/pipeline trees, stashes and local branch histories are
recovery material, not proposed overrides of current runtime code. Preserve
their exact identities and distinguish any source-history-only merge from an
active source integration. Private recovery/archive root:
`D:/Backups/game-platform-consolidation-20261021`. Raw logs, user settings and
runtime state are private recovery material and are not published in this repo.

Existing native-library release workflows are still scoped to their historical
release branches/manual triggers. A preservation push to main does not authorize
a new package/release or claim a new native/platform qualification. Future build
or workflow changes require their own checks on this canonical main source.

## Completed checkout retirement

All redundant plugin source clones are retired after verified private backup.
The separate `C:/Work/git/gindemit/unity-rlottie/rlottie` vendor clone is also
retired. Its branch tips and settings are backed up; the divergent historical
shape branch is recovery history, not an active vendor modification.
The required pinned dependency remains at
`C:/Work/git/gindemit/unity-rlottie/unity-rlottie/dependency/rlottie`.
Do not remove that submodule or change its pin to achieve a cosmetic clone count.
The plugin's sole local working branch and GitHub default are now `main`.
Historical remote branches/tags and private stashes remain recoverable.
