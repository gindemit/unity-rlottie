"""Hash each native/worker pixel buffer using explicit delivery and fixture paths.
No Unity launch, installation, PackageCache edits or fixed package identity.
"""
import argparse
import ctypes
import hashlib
import json
from pathlib import Path
import subprocess


class Surface(ctypes.Structure):
    _fields_ = [('buffer', ctypes.c_void_p), ('width', ctypes.c_uint32),
                ('height', ctypes.c_uint32), ('stride', ctypes.c_uint32)]


def qualify(package, fixtures, output):
    output.mkdir(parents=True, exist_ok=False)
    native = package / 'Plugins/Windows/x86_64/LottiePlugin.dll'
    worker = package / 'Editor/WebGLArchives/WasmExceptions/RasterWorker~'
    receipt = json.loads((worker / 'provenance.json').read_text())
    if receipt['archiveSha256'] != hashlib.sha256((worker.parent / 'librlottie.a.bytes').read_bytes()).hexdigest():
        raise RuntimeError('Worker/renderer archive mismatch')
    for entry in receipt['files']:
        if hashlib.sha256((worker / entry['file']).read_bytes()).hexdigest() != entry['sha256']:
            raise RuntimeError('Invalid worker payload')
    library = ctypes.CDLL(str(native.resolve()))
    library.lottie_load_from_data.argtypes = [ctypes.c_char_p, ctypes.c_char_p, ctypes.POINTER(ctypes.c_void_p)]
    library.lottie_allocate_render_data.argtypes = [ctypes.POINTER(ctypes.c_void_p)]
    library.lottie_render_immediately.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_uint32, ctypes.c_bool, ctypes.c_bool]
    library.lottie_dispose_render_data.argtypes = [ctypes.POINTER(ctypes.c_void_p)]
    library.lottie_dispose_wrapper.argtypes = [ctypes.POINTER(ctypes.c_void_p)]
    cases = []
    for asset in sorted(fixtures.rglob('*.json')):
        data = asset.read_bytes()
        doc = json.loads(data)
        markers = doc.get('markers', [])
        if not markers:
            continue
        frames = sorted(set(value for marker in markers for value in range(int(marker['tm']), int(marker['tm'] + marker['dr']))))
        # All fixtures at startup resolution; first two also qualify rectangular/detail surfaces.
        sizes = [(96, 96)] + ([(16, 32), (256, 128), (512, 512)] if len(cases) < 8 else [])
        for width, height in sizes:
            animation, surface = ctypes.c_void_p(), ctypes.c_void_p()
            pixels = (ctypes.c_ubyte * (width * height * 4))()
            try:
                if library.lottie_load_from_data(data, b'', ctypes.byref(animation)) != 0:
                    raise RuntimeError('Native animation load failed')
                if library.lottie_allocate_render_data(ctypes.byref(surface)) != 0:
                    raise RuntimeError('Native surface allocation failed')
                header = ctypes.cast(surface, ctypes.POINTER(Surface)).contents
                header.buffer, header.width, header.height, header.stride = ctypes.addressof(pixels), width, height, width * 4
                hashes = []
                for frame in frames:
                    if library.lottie_render_immediately(animation, surface, frame, True, False) != 0:
                        raise RuntimeError('Native rendering failed')
                    hashes.append(hashlib.sha256(bytes(pixels)).hexdigest())
                cases.append({'json': str(asset.resolve()), 'sha256': hashlib.sha256(data).hexdigest(),
                              'width': width, 'height': height, 'frames': frames, 'pixelHashes': hashes})
            finally:
                if surface: library.lottie_dispose_render_data(ctypes.byref(surface))
                if animation: library.lottie_dispose_wrapper(ctypes.byref(animation))
    if not cases or not any(case['frames'] for case in cases):
        raise RuntimeError('No marker frames found; empty qualification is not success')
    request = output / 'cases.json'
    request.write_text(json.dumps({'nativeSha256': hashlib.sha256(native.read_bytes()).hexdigest(),
                                  'worker': str(worker.resolve()), 'workerProvenance': receipt, 'cases': cases}, indent=2)+'\n')
    subprocess.run(['node', str(Path(__file__).with_name('qualify-raster-pixels.mjs')),
                    str(request), str(output/'result.json')], check=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--package', type=Path, required=True)
    parser.add_argument('--fixtures', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    qualify(args.package, args.fixtures, args.output)
