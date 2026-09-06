# Android OpenGL ES 2 device validation — 2026-09-06

## Result

The Unity 2022.3.62f3 Built-in player passed all 16 rendered-player checks on
a physical Samsung Galaxy Note10+ while Unity reported `OpenGLES2`. Both sample
controls selected the Unity-owned `NativeOpenGL` upload backend; the device log
contains the `Unity-owned OpenGL native upload enabled` marker and no rlottie
fallback message.

The checks covered loading, first-frame visibility, advancing animation pixels,
button playback, exact Gamma color calibration, and 12 create/play/destroy
lifecycle-stress cycles. The saved screenshot also shows both animations and
the calibration targets rendered on the device.

## Environment

| Item | Value |
|---|---|
| Device | Samsung SM-N975F (Galaxy Note10+) |
| SoC / GPU | Exynos 9825 / ARM Mali-G76 |
| OS | Android 12, API 31 |
| Unity | 2022.3.62f3, Built-in pipeline |
| Requested/reported graphics API | `OpenGLES2` |
| Driver | OpenGL ES 3.2 v1.r32p1-01bet2-mbs2v39_0.131801e953429f661ecce1d5e1d2b3ef |
| Color space | Gamma |
| Upload backend | `NativeOpenGL` |
| Source commit before local validation fix | `c64c252b39e403f7c80dd9e61f4d510874e7ad24` |

The GPU supports a newer OpenGL ES version, but the built player explicitly
requested GLES2 and Unity's runtime metadata reported `OpenGLES2`; this validates
the GLES2 compatibility path on that driver, not a GLES2-only GPU.

## Issue found and fixed

The first run passed its visual and interaction checks but selected the legacy
plugin-owned `NativeExternalTexture` path. The managed selection predicate had
enabled the newer Unity-owned OpenGL upload only for OpenGL Core and GLES3,
despite the native upload implementation and platform documentation also
covering GLES2.

The predicate now includes `GraphicsDeviceType.OpenGLES2`. A regression test
locks the selection set to OpenGL Core, GLES2, and GLES3 while rejecting Vulkan.
After rebuilding, the physical-device run selected `NativeOpenGL` and passed all
16 checks.

## Local evidence

Build products and device evidence are retained outside the repository at:

`C:\Work\git\gindemit\unity-rlottie\results\android-gles2-20260906-fixed`

That directory contains `RLottieSmoke.apk`, `build.log`, `device.log`,
`smoke-result.json`, and `screenshot.png`.

