# Runtime color overrides

`unity-rlottie` exposes rlottie's dynamic fill and stroke properties through the
native C ABI and the Unity runtime API. This lets multiple visual themes share one
Lottie file when their geometry and motion are identical.

## Authoring stable palette slots

Keypaths are built from nested Lottie object names separated by dots. Author a
shape group named `palette` and give its fill or stroke paints semantic names such
as `base`, `shade`, `light`, `leaf`, `leafLight`, `magic`, `cream`, or `ink`.
Then address the paints with keypaths such as `**.palette.base` and
`**.palette.ink`.

Do not name one paint `palette.base`. rlottie interprets the dot as a hierarchy
separator, so that single object name does not create the same keypath. Semantic
names also keep consumers independent of incidental layer and shape names.

## Unity API

Create overrides with `LottieColorOverride.Fill` and
`LottieColorOverride.Stroke`. Applying the whole palette in one call minimizes
managed-to-native transitions:

```csharp
var palette = new[]
{
    LottieColorOverride.Fill("**.palette.base", new Color(0.42f, 0.31f, 0.20f)),
    LottieColorOverride.Fill("**.palette.light", new Color(0.72f, 0.60f, 0.43f)),
    LottieColorOverride.Stroke("**.palette.ink", new Color(0.12f, 0.09f, 0.07f))
};

var animation = LottieAnimation.LoadFromJsonData(json, resourcesPath, 256, 256,
    new LottieAnimationOptions { ColorOverrides = palette });

// Individual calls can build the initial palette before the first render.
animation.ApplyColorOverrides(palette);
animation.SetFillColor("**.palette.base", Color.red);
animation.SetStrokeColor("**.palette.ink", Color.black);
```

For immutable shared frames, set `LottieFrameCacheOptions.ColorOverrides`. The
cache applies the palette before warming, so every cached texture contains the
independently overridden semantic colors. `LottieCpuRasterizer` accepts the same
list in its load methods and also exposes the three runtime methods.

rlottie's fill and stroke color properties contain RGB only. Unity `Color.a` is
ignored; opacity remains controlled by the Lottie fill/stroke opacity property.
Values are passed through as supplied and must be finite. These overrides change
individual paints and do not use Unity UI tinting.

`LottieAnimation` waits for an outstanding asynchronous render before changing
properties. `LottieCpuRasterizer` serializes override and render operations.
Apply a template palette through the load/cache options before its first frame.
When changing a property after rendering has begun, advance to a different
source frame before comparing output; rlottie may retain cached content when the
same static source frame is requested repeatedly.

## Native ABI

Native integrations can call `lottie_set_fill_color`,
`lottie_set_stroke_color`, or pass an array of `lottie_color_override` values to
`lottie_apply_color_overrides`. A zero-length batch is valid. Invalid animation
pointers, keypaths, properties, or non-finite RGB values return `-1`; success
returns `0`.

As with rlottie's `Animation` object itself, native callers must not mutate an
animation concurrently with rendering. Finish any asynchronous render before
calling a color override function.
