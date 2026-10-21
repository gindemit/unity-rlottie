"""Link the optional CPU worker with the SAME emcc used for its renderer archive.

Existing Legacy archives require Unity 2022.3.62f3's linker (setjmp ABI).
New CI archives use the build_webgl job's pinned emsdk for both build and link.
Unity consumer builds remain independent, e.g. Unity 6000.5.3f1.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import re

ROOT = Path(__file__).resolve().parents[1]


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def build(archive, emcc, output, wasm_exceptions=False, fastcomp=False):
    source_provenance = archive.parent.parent / 'PROVENANCE.md'
    manifest = source_provenance.read_text()
    actual_archive_hash = digest(archive)
    renderer = subprocess.check_output(['git', 'rev-parse', 'HEAD:dependency/rlottie'], cwd=ROOT, text=True).strip()
    declared_renderer = re.search(r'rlottie dependency `([0-9a-f]+)`', manifest)
    if actual_archive_hash not in manifest or not declared_renderer or not renderer.startswith(declared_renderer[1]):
        raise RuntimeError('Archive source provenance does not match this renderer dependency; update/qualify archive provenance first')
    output.mkdir(parents=True, exist_ok=True)
    source = ROOT / 'scripts/RasterWorker'
    argv = [str(emcc), str(source / 'raster.c'), str(archive), '-O3',
            '-sMODULARIZE=1', '-sEXPORT_NAME=LottieRasterModule', '-sENVIRONMENT=worker,node',
            '-sALLOW_MEMORY_GROWTH=1', '-sINITIAL_MEMORY=16777216', '-sMAXIMUM_MEMORY=134217728',
            '-sFILESYSTEM=0',
            '-sEXPORTED_FUNCTIONS=["_lottie_worker_create","_lottie_worker_render","_lottie_worker_destroy","_malloc","_free"]',
            '-sEXPORTED_RUNTIME_METHODS=["lengthBytesUTF8","stringToUTF8","HEAPU8"]',
            '-o', str(output / 'raster.js')]
    environment = os.environ.copy()
    if fastcomp:
        environment['EMCC_WASM_BACKEND'] = '0'
    for config in [emcc.resolve().parents[1] / '.emscripten', emcc.resolve().parents[2] / '.emscripten']:
        if config.exists():
            environment['EM_CONFIG'] = str(config)
            break
    version = subprocess.check_output([str(emcc), '--version'], env=environment, text=True).splitlines()[0]
    if wasm_exceptions:
        argv.append('-fwasm-exceptions')
    # Stored .bytes archives are inert Unity assets; give emcc an archive suffix.
    with tempfile.TemporaryDirectory(prefix='lottie-worker-link-') as temporary:
        linked_archive = Path(temporary) / 'librlottie.a'
        shutil.copyfile(archive, linked_archive)
        argv[2] = str(linked_archive)
        subprocess.run(argv, check=True, env=environment)
    for name in ['worker.js', 'LICENSE.txt']:
        shutil.copyfile(source / name, output / name)
    provenance = {'archiveSha256': actual_archive_hash, 'rendererSourceCommit': renderer,
                  'archiveSourceProvenanceSha256': digest(source_provenance),
                  'wrapperSha256': digest(source / 'raster.c'), 'toolchain': version,
                  'initialMemoryBytes': 16777216, 'maximumMemoryBytes': 134217728,
                  'files': [{'file': name, 'sha256': digest(output / name)} for name in
                            ['worker.js', 'raster.js', 'raster.wasm', 'LICENSE.txt']]}
    (output / 'provenance.json').write_text(json.dumps(provenance, indent=2) + '\n')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--archive', type=Path, required=True)
    parser.add_argument('--emcc', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--wasm-exceptions', action='store_true')
    parser.add_argument('--fastcomp', action='store_true')
    args = parser.parse_args()
    build(args.archive, args.emcc, args.output, args.wasm_exceptions, args.fastcomp)
