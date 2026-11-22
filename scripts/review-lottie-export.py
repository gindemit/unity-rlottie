"""Inspect a self-contained Lottie JSON export and sample it with the Windows plugin."""

import argparse
import ctypes
import hashlib
import json
import math
from pathlib import Path
import sys
from time import perf_counter


SHAPE_TYPES = set("gr rc rd el tr fl st gf gs sh sr tm rp".split())
MAX_INPUT_BYTES = 8 * 1024 * 1024


def objects(record, key, location):
    items = record.get(key, [])
    if not isinstance(items, list) or any(not isinstance(item, dict) for item in items):
        raise ValueError(f"{location}.{key} must be an array of objects")
    return ((f"{location}.{key}[{index}]", item) for index, item in enumerate(items))


def invalid_constant(value):
    raise ValueError(f"{value} is not a JSON number")


def finite_float(value):
    number = float(value)
    if not math.isfinite(number):
        raise ValueError("JSON number exceeds the finite floating-point range")
    return number


def unique_object(pairs):
    result = dict(pairs)
    if len(result) != len(pairs):
        raise ValueError("JSON object contains duplicate keys")
    return result


def rounded_frame(value):
    fraction, whole = math.modf(value)
    return int(whole) + int(fraction >= 0.5)


def read_export(data):
    if data.startswith(b"PK"):
        raise ValueError("Choose Lottie JSON in the exporter to review a dotLottie animation")
    if b"\0" in data:
        raise ValueError("Expected UTF-8 Lottie JSON without embedded NUL bytes")
    return json.loads(data.decode("utf-8-sig"), parse_constant=invalid_constant, parse_float=finite_float,
                      object_pairs_hook=unique_object)


def inspect_export(document, misplaced_shapes=None):
    if (not isinstance(document, dict) or not isinstance(document.get("layers"), list)
            or not isinstance(document.get("v"), str) or not document["v"]):
        raise ValueError("Expected a Lottie composition with a version and a layers array")
    width, height, first, end = (document.get(key) for key in ("w", "h", "ip", "op"))
    rate = document.get("fr")
    if (any(type(value) is not int for value in (width, height))
            or not (0 < width <= 16384 and 0 < height <= 16384)
            or any(type(value) not in (int, float) for value in (first, end, rate))
            or not (0 <= first < end <= 2**31 - 1 and 0 < rate <= 1000)):
        raise ValueError("Expected bounded positive dimensions/FPS and a nonnegative frame range")
    # The pinned parser applies std::lround to composition endpoints.
    raster_first, raster_end = rounded_frame(first), rounded_frame(end)
    if raster_first == raster_end:
        raise ValueError("Frame range is empty after rounding to the renderer timeline")

    issues = []
    layers, shapes = set(), set()

    def flag(path, message):
        issues.append({"path": path, "message": message})

    def is_static_zero(property_value):
        return (isinstance(property_value, dict) and property_value.get("a", 0) == 0
                and property_value.get("k") in (0, [0, 0]))

    def inspect_layer(path, layer):
        kind = layer.get("ty")
        if type(kind) is not int:
            raise ValueError(f"{path}.ty must be an integer")
        layers.add(kind)
        if kind not in (0, 1, 3, 4):
            flag(path + ".ty", "Use shape layers or self-contained precompositions for this review")
        for key, description in (("ef", "Layer effects"), ("ddd", "3D layers"),
                                 ("bm", "Non-default blend modes")):
            if layer.get(key):
                flag(path + "." + key, description + " require a separate compatibility check")
        inspect_transform(path + ".ks", layer.get("ks"))
        for mask_path, mask in objects(layer, "masksProperties", path):
            if mask.get("mode") not in (None, "n", "a", "s", "i"):
                flag(mask_path + ".mode", "Mask mode is outside the review profile")
            for key in ("x", "f"):
                if key in mask and not is_static_zero(mask[key]):
                    flag(mask_path + "." + key, "Mask expansion or feathering requires review")
        inspect_properties(path, {key: value for key, value in layer.items()
                                  if key in ("ks", "tm", "masksProperties")})
        for shape_path, shape in objects(layer, "shapes", path):
            inspect_shape(shape_path, shape)

    def inspect_transform(path, transform):
        if isinstance(transform, dict) and "sk" in transform:
            skew = transform["sk"]
            if not (isinstance(skew, dict) and skew.get("a", 0) == 0 and skew.get("k") == 0):
                flag(path + ".sk", "Skew is unsupported by the pinned renderer")

    def inspect_properties(path, properties):
        if isinstance(properties, dict):
            if isinstance(properties.get("x"), str) and properties["x"].strip():
                flag(path + ".x", "Expressions are unsupported by the pinned renderer")
            if properties.get("sid"):
                flag(path + ".sid", "Runtime slots require a separate compatibility check")
            for key, value in properties.items():
                inspect_properties(f"{path}.{key}", value)
        elif isinstance(properties, list):
            for index, value in enumerate(properties):
                inspect_properties(f"{path}[{index}]", value)

    def inspect_shape(path, shape):
        kind = shape.get("ty")
        if not isinstance(kind, str):
            raise ValueError(f"{path}.ty must be a string")
        shapes.add(kind)
        if kind not in SHAPE_TYPES:
            flag(path + ".ty", f"Shape type {kind!r} is unsupported by the pinned renderer")
        # The pinned parser starts shape dispatch at ty, after skipping earlier keys.
        keys = list(shape)
        ignored = [key for key in keys[:keys.index("ty")] if key not in ("mn", "ix", "ind", "meta")
                   and (key != "d" or shape["d"] != 1)]
        if ignored:
            flag(path + ".ty", f"Move ty before these shape fields so rlottie reads them: {', '.join(ignored)}")
            if kind in SHAPE_TYPES and misplaced_shapes is not None:
                misplaced_shapes.append(shape)
        if kind == "tr":
            inspect_transform(path, shape)
        inspect_properties(path, {key: value for key, value in shape.items()
                                  if key not in ("it", "nm", "mn", "ty", "meta")})
        for child_path, child in objects(shape, "it", path):
            inspect_shape(child_path, child)

    if document.get("slots"):
        flag("slots", "Runtime slots require a separate compatibility check")
    if document.get("ddd"):
        flag("ddd", "3D compositions require a separate compatibility check")
    for path, layer in objects(document, "layers", "$"):
        inspect_layer(path, layer)
    for path, asset in objects(document, "assets", "$"):
        if "layers" not in asset or "p" in asset or "u" in asset:
            flag(path, "Image, audio and other media assets are outside this self-contained vector review")
        for layer_path, layer in objects(asset, "layers", path):
            inspect_layer(layer_path, layer)
    markers = [{"name": marker.get("cm"), "frame": marker.get("tm"), "duration": marker.get("dr")}
               for _, marker in objects(document, "markers", "$")]
    return {"width": width, "height": height, "inFrame": first, "outFrame": end, "fps": rate,
            "rasterInFrame": raster_first, "rasterOutFrame": raster_end,
            "markers": markers, "layerTypes": sorted(layers), "shapeTypes": sorted(shapes),
            "issues": issues}


def normalize_shape_order(shapes):
    for shape in shapes:
        kind = shape.pop("ty")
        ordered = {"ty": kind, **shape}
        shape.clear()
        shape.update(ordered)


class Surface(ctypes.Structure):
    _fields_ = [("buffer", ctypes.c_void_p), ("width", ctypes.c_uint32),
                ("height", ctypes.c_uint32), ("stride", ctypes.c_uint32)]


def render_filmstrip(data, report, native, output, size, sample_count):
    from PIL import Image, ImageDraw

    library = ctypes.CDLL(str(native.resolve()))
    pointer = ctypes.c_void_p
    by_ref = ctypes.POINTER(pointer)
    functions = {
        "lottie_load_from_data": [ctypes.c_char_p, ctypes.c_char_p, by_ref],
        "lottie_allocate_render_data": [by_ref],
        "lottie_render_immediately": [pointer, pointer, ctypes.c_uint32, ctypes.c_bool, ctypes.c_bool],
        "lottie_dispose_render_data": [by_ref],
        "lottie_dispose_wrapper": [by_ref],
    }
    for name, arguments in functions.items():
        function = getattr(library, name)
        function.argtypes = arguments
        function.restype = ctypes.c_int32
    animation, surface = pointer(), pointer()
    width, height = report["width"], report["height"]
    scale = size / max(width, height)
    width, height = max(1, round(width * scale)), max(1, round(height * scale))
    pixels = (ctypes.c_ubyte * (width * height * 4))()
    last = report["rasterOutFrame"] - report["rasterInFrame"] - 1
    indices = sorted({round(index * last / (sample_count - 1)) for index in range(sample_count)})
    columns = min(4, len(indices))
    cell_width, cell_height = max(width, 100), height + 22
    strip = Image.new("RGBA", (columns * cell_width, math.ceil(len(indices) / columns) * cell_height))
    labels = ImageDraw.Draw(strip)
    frames = []
    try:
        start = perf_counter()
        if library.lottie_load_from_data(data, b"", ctypes.byref(animation)) != 0:
            raise RuntimeError("Native animation load failed")
        load_ms = (perf_counter() - start) * 1000
        if library.lottie_allocate_render_data(ctypes.byref(surface)) != 0:
            raise RuntimeError("Native surface allocation failed")
        header = ctypes.cast(surface, ctypes.POINTER(Surface)).contents
        header.buffer, header.width, header.height, header.stride = ctypes.addressof(pixels), width, height, width * 4
        for position, index in enumerate(indices):
            ctypes.memset(pixels, 0, len(pixels))
            start = perf_counter()
            status = library.lottie_render_immediately(animation, surface, index, True, False)
            raster_ms = (perf_counter() - start) * 1000
            if status != 0:
                raise RuntimeError(f"Native frame {index} failed with status {status}")
            raw = bytes(pixels)
            bgra = Image.frombytes("RGBA", (width, height), raw, "raw", "BGRA")
            frame = Image.frombytes("RGBa", bgra.size, bgra.tobytes()).convert("RGBA")
            x, y = (position % columns) * cell_width, (position // columns) * cell_height
            strip.paste(frame, (x, y))
            source_frame = index + report["rasterInFrame"]
            labels.rectangle((x, y + height, x + cell_width - 1, y + cell_height - 1), fill="#222222")
            labels.text((x + 4, y + height + 4), f"Frame {source_frame}", fill="white")
            frames.append({"sourceFrame": source_frame, "nativeFrame": index,
                           "rasterMs": round(raster_ms, 3), "bgraSha256": hashlib.sha256(raw).hexdigest(),
                           "visiblePixels": width * height - frame.getchannel("A").histogram()[0]})
    finally:
        if surface:
            library.lottie_dispose_render_data(ctypes.byref(surface))
        if animation:
            library.lottie_dispose_wrapper(ctypes.byref(animation))
    strip.save(output / "filmstrip.png")
    return {"path": str(native.resolve()), "sha256": hashlib.sha256(native.read_bytes()).hexdigest(),
            "width": width, "height": height, "loadMs": round(load_ms, 3), "frames": frames}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("animation", type=Path)
    parser.add_argument("--output", required=True, type=Path, help="New directory for report and optional filmstrip")
    parser.add_argument("--native", type=Path, help="Explicit Windows x64 LottiePlugin.dll to render with")
    parser.add_argument("--normalize-shape-order", action="store_true",
                        help="Write and review a separate JSON copy with misplaced shape type keys moved first")
    parser.add_argument("--size", type=int, choices=range(16, 513), default=160, metavar="16..512")
    parser.add_argument("--samples", type=int, choices=range(2, 17), default=8, metavar="2..16")
    args = parser.parse_args()
    try:
        with args.animation.open("rb") as source:
            data = source.read(MAX_INPUT_BYTES + 1)
        if len(data) > MAX_INPUT_BYTES:
            raise ValueError("Animation exceeds the 8 MiB review input limit")
        document = read_export(data)
        misplaced_shapes = []
        report = inspect_export(document, misplaced_shapes)
        source_hash = hashlib.sha256(data).hexdigest()
        normalized = None
        if args.normalize_shape_order and misplaced_shapes:
            original_issues = report["issues"]
            normalize_shape_order(misplaced_shapes)
            report = inspect_export(document)
            normalized = json.dumps(document, ensure_ascii=True, separators=(",", ":"), allow_nan=False).encode("utf-8")
            report["normalization"] = {"shapeCount": len(misplaced_shapes), "originalIssues": original_issues,
                                       "path": "normalized.json", "sha256": hashlib.sha256(normalized).hexdigest()}
        args.output.mkdir(parents=True, exist_ok=False)
        if normalized is not None:
            (args.output / "normalized.json").write_bytes(normalized)
        report.update({"schemaVersion": 1, "input": str(args.animation.resolve()),
                       "inputSha256": source_hash, "inputBytes": len(data)})
    except (OSError, ValueError, RecursionError) as error:
        print(f"Review failed: {error}", file=sys.stderr)
        return 1
    result = 2 if report["issues"] else 0
    try:
        if args.native and result == 0:
            render_data = normalized if normalized is not None else data.removeprefix(b"\xef\xbb\xbf")
            report["native"] = render_filmstrip(render_data, report,
                                               args.native, args.output, args.size, args.samples)
            report["allSamplesTransparent"] = not any(frame["visiblePixels"] for frame in report["native"]["frames"])
            if report["allSamplesTransparent"]:
                result = 2
    except (ImportError, AttributeError, OSError, RuntimeError) as error:
        report["renderError"] = str(error)
        result = 1
    report["status"] = {0: "sampled" if args.native else "inspected", 1: "render-error", 2: "needs-review"}[result]
    try:
        (args.output / "review.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    except OSError as error:
        print(f"Could not write review report: {error}", file=sys.stderr)
        return 1
    print(f'{report["status"]}: {args.output / "review.json"}')
    return result


if __name__ == "__main__":
    sys.exit(main())
