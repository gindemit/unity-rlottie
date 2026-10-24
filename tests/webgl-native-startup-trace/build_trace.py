"""Explicit isolated native trace build; never stages Unity assets or publishes.

ROOT must review/commit this producer and grant the native-build lease before use.
Requires fresh disjoint external --build/--output, installed tools and clean main.
"""
import argparse
import ctypes
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import shlex
import subprocess
import sys

sys.dont_write_bytecode = True
from native_contract import external_fresh

ROOT = Path(__file__).resolve().parents[2]
PREFIX = 'webgl-native-state-trace.'
BASE_PLUGIN = '088d14b0909acdc6076e1ff1525e8a6d69094d284d4151fc03b6a6ee75504a51'
BASE_RLOTTIE = 'b17cda443208f9c746a6610e8edee79257684fdd8ae39f93f8df3a1263d908da'
ADDED = sorted('lottie_webgl_trace_' + name for name in ('arm', 'copy', 'issue', 'register', 'request'))
NEW_UNDEFINED = {'emscripten_webgl_get_current_context', 'emscripten_webgl_get_context_attributes',
                 'emscripten_is_webgl_context_lost', 'emscripten_get_heap_size'}
ABI_FLAGS = ['-fwasm-exceptions', '-sSUPPORT_LONGJMP=wasm', '-sWASM_LEGACY_EXCEPTIONS=1']
# Actual Unity LLVM v2 observations: empty/platform-disabled WebGL units.
EMPTY_MEMBERS = {'InstanceRegistry.cpp.o', 'UploadQueue.cpp.o', 'UploadPipeline.cpp.o',
                 'TextureBackend.cpp.o', 'VulkanBackend.cpp.o', 'UnityIntegration.cpp.o',
                 'UnityExports.cpp.o', 'lottieproxymodel.cpp.o', 'vdebug.cpp.o',
                 'vdrawhelper_neon.cpp.o', 'vdrawhelper_sse2.cpp.o'}


def sha(data):
    return hashlib.sha256(data).hexdigest()


def descriptor(path, relative=None, allow_empty=False):
    path = Path(path)
    data = path.read_bytes()
    if (not data and not allow_empty) or len(data) > 2147483647:
        raise ValueError('Empty or oversized input: ' + str(path))
    return {'path': path.as_posix() if relative is None else relative,
            'sha256': sha(data), 'bytes': len(data)}


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False) + '\n', encoding='utf-8', newline='\n')
    return descriptor(path, path.name)


def inside(path, parent):
    return path == parent or parent in path.parents


def safe_external(path, protected):
    external_fresh(path)
    if any(inside(path, root) or inside(root, path) for root in protected):
        raise ValueError('Output overlaps protected input: ' + str(path))
    if any(part.lower() in {'assets', 'library', 'packages', 'packagecache', 'dependency', 'sysroot', 'cache'}
           for part in path.parts):
        raise ValueError('Output crosses protected directory')


def tokens(command):
    if os.name != 'nt':
        return shlex.split(command)
    count = ctypes.c_int()
    parse = ctypes.windll.shell32.CommandLineToArgvW
    parse.argtypes = [ctypes.c_wchar_p, ctypes.POINTER(ctypes.c_int)]
    parse.restype = ctypes.POINTER(ctypes.c_wchar_p)
    result = parse(command, ctypes.byref(count))
    if not result:
        raise ValueError('Cannot parse actual compiler command')
    try:
        return [result[index] for index in range(count.value)]
    finally:
        ctypes.windll.kernel32.LocalFree(ctypes.cast(result, ctypes.c_void_p))


def normalize_symbols(text, allow_empty=False):
    names = set()
    for line in text.splitlines():
        line = line.strip()
        if not line or line.endswith(':'):
            continue
        fields = line.split()
        if len(fields) < 2 or len(fields[1]) != 1 or not fields[1].isalpha():
            raise ValueError('Unknown llvm-nm POSIX output: ' + line)
        names.add(fields[0])
    if not names and not allow_empty:
        raise ValueError('Empty defined-symbol inventory')
    return sorted(names)


def inventory_hash(symbols):
    return sha(('\n'.join(symbols) + ('\n' if symbols else '')).encode('utf-8'))


def validate_nm_stderr(stderr, archive):
    expected = {Path(archive).as_posix() + '[' + member + ']: no symbols' for member in EMPTY_MEMBERS}
    if any(line not in expected for line in stderr.splitlines() if line):
        raise ValueError('Unexpected llvm-nm diagnostic; preserve external log')


def unreviewed_external(undefined, defined, baseline):
    # Per-member undefined references can resolve in another archive member.
    return set(undefined) - set(defined) - set(baseline) - NEW_UNDEFINED


def argv_strings(argv):
    return [value.as_posix() if isinstance(value, Path) else str(value) for value in argv]


def git(*args):
    return subprocess.check_output(['git', *args], cwd=ROOT, text=True).strip()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--unity-editor', required=True, type=Path, help='Installed Editor directory containing Unity.exe')
    parser.add_argument('--cmake', required=True, type=Path)
    parser.add_argument('--ninja', required=True, type=Path)
    parser.add_argument('--build', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--execute', action='store_true', help='Explicit execution, only after ROOT grants build lease')
    args = parser.parse_args()
    if not args.execute:
        parser.error('--execute requires the separately granted ROOT native-build lease')
    if git('branch', '--show-current') != 'main' or git('status', '--porcelain', '--untracked-files=all'):
        raise ValueError('Build requires clean canonical main, including submodules')
    source_commit = git('rev-parse', 'HEAD')
    rlottie_commit = git('rev-parse', 'HEAD:dependency/rlottie')
    if git('-C', 'dependency/rlottie', 'rev-parse', 'HEAD') != rlottie_commit:
        raise ValueError('rlottie checkout differs from immutable source commit')
    for key in ('CFLAGS', 'CXXFLAGS', 'LDFLAGS', 'EMCC_CFLAGS', 'CMAKE_GENERATOR', 'EMCC_FORCE_STDLIBS'):
        if os.environ.get(key):
            raise ValueError('Ambiguous inherited build setting: ' + key)
    editor = args.unity_editor.resolve(strict=True)
    if editor.parent.name != '6000.5.3f1' or not (editor / 'Unity.exe').is_file():
        raise ValueError('Expected installed Unity6000.5.3f1 Editor')
    sdk = editor / 'Data/PlaybackEngines/WebGLSupport/BuildTools/Emscripten'
    ems = sdk / 'emscripten'
    tool_paths = {'emcc': ems / 'emcc.bat', 'emar': ems / 'emar.bat',
                  'llvmNm': sdk / 'llvm/llvm-nm.exe', 'cmake': args.cmake.resolve(strict=True),
                  'ninja': args.ninja.resolve(strict=True),
                  'toolchainFile': ems / 'cmake/Modules/Platform/Emscripten.cmake'}
    approved = [editor, Path(os.environ.get('ProgramFiles', 'C:/Program Files')) / 'CMake',
                Path(os.environ.get('ProgramFiles', 'C:/Program Files')) / 'Microsoft Visual Studio']
    for key, path in tool_paths.items():
        path.resolve(strict=True)
        if not any(inside(path, root) for root in approved):
            raise ValueError('Tool outside approved installed roots: ' + key)
    tools = {key: descriptor(path) for key, path in tool_paths.items()}
    config = sdk / '.emscripten'
    python = sdk / 'python/python.exe'
    compiler = sdk / 'llvm/clang++.exe'
    sysroot = ems / 'cache/sysroot'
    if not re.search(r'^FROZEN_CACHE\s*=\s*True\s*$', config.read_text(), re.M):
        raise ValueError('Installed Emscripten configuration must freeze cache')
    protected = [ROOT.resolve(), editor, *[path.parent for path in tool_paths.values()]]
    safe_external(args.build, protected)
    safe_external(args.output, protected)
    if inside(args.build, args.output) or inside(args.output, args.build):
        raise ValueError('Build and payload output must not overlap')
    baseline = ROOT / 'unity/RLottieUnity/Assets/LottiePlugin/Editor/WebGLArchives/WasmExceptions'
    plugin_baseline, rlottie_baseline = baseline / 'libLottiePlugin.a.bytes', baseline / 'librlottie.a.bytes'
    if descriptor(plugin_baseline)['sha256'] != BASE_PLUGIN or descriptor(rlottie_baseline)['sha256'] != BASE_RLOTTIE:
        raise ValueError('Immutable production archive pair changed')
    env = os.environ.copy()
    env.update(EM_CONFIG=str(config), EM_CACHE=str(ems / 'cache'), EM_FROZEN_CACHE='1',
               EMSDK_PYTHON=str(python), PYTHONDONTWRITEBYTECODE='1')
    env['PATH'] = str(sdk / 'python') + os.pathsep + str(ems) + os.pathsep + env.get('PATH', '')
    args.output.mkdir(parents=True)
    args.build.mkdir(parents=True)

    def run(argv, label, cwd=args.build, nm_archive=None):
        argv = argv_strings(argv)
        completed = subprocess.run(argv, cwd=cwd, env=env,
                                   capture_output=True, text=True, timeout=1200)
        log = args.output / (PREFIX + label + '.log')
        log.write_text(completed.stdout + completed.stderr, encoding='utf-8', newline='\n')
        if completed.returncode:
            raise RuntimeError('Failed ' + label + '; preserve external log: ' + str(log))
        if nm_archive is not None:
            validate_nm_stderr(completed.stderr, nm_archive)
        return completed.stdout, {'argv': argv, 'exit': 0,
                                  'output': descriptor(log, log.name)}

    em_version, _ = run([tool_paths['emcc'], '--version'], 'emcc-version')
    if not re.search(r'\b4\.0\.20-git\b', em_version):
        raise ValueError('Installed Emscripten version differs from required 4.0.20-git')
    cm_version, _ = run([tool_paths['cmake'], '--version'], 'cmake-version')
    ninja_version, _ = run([tool_paths['ninja'], '--version'], 'ninja-version')
    configure_argv = [tool_paths['cmake'], '-S', ROOT / 'projects/CMake', '-B', args.build, '-G', 'Ninja',
                      '-DCMAKE_MAKE_PROGRAM=' + str(tool_paths['ninja']),
                      '-DCMAKE_TOOLCHAIN_FILE=' + str(tool_paths['toolchainFile']),
                      '-DCMAKE_BUILD_TYPE=Release', '-DCMAKE_EXPORT_COMPILE_COMMANDS=ON', '-DBUILD_TESTING=OFF',
                      '-DRLOTTIE_WEB_ASSEMBLY=ON', '-DRLOTTIE_WEBGL_WASM_EXCEPTIONS=ON',
                      '-DLOTTIE_WEBGL_NATIVE_STATE_TRACE=ON', '-DLOTTIE_WEBGL_TRACE_OUTPUT_ROOT=' + str(args.output / 'payload')]
    # CMake requires its trace output absent at configure time; payload is moved
    # into the receipt root only after the isolated target succeeds.
    _, configure = run(configure_argv, 'configure')
    _, build_command = run([tool_paths['cmake'], '--build', args.build, '--target', 'LottiePlugin', '--parallel', '2'], 'build')
    compiled_plugin = args.output / 'payload/WasmExceptions/libLottiePlugin.trace.a'
    (args.output / 'WasmExceptions').mkdir()
    shutil.copyfile(compiled_plugin, args.output / 'WasmExceptions/libLottiePlugin.trace.a')
    shutil.copyfile(rlottie_baseline, args.output / 'WasmExceptions/librlottie.baseline.a')
    plugin = descriptor(args.output / 'WasmExceptions/libLottiePlugin.trace.a', 'WasmExceptions/libLottiePlugin.trace.a')
    rlottie = descriptor(args.output / 'WasmExceptions/librlottie.baseline.a', 'WasmExceptions/librlottie.baseline.a')
    commands = json.loads((args.build / 'compile_commands.json').read_text())
    plugin_commands = [entry for entry in commands if inside(Path(entry['file']).resolve(), ROOT / 'src')]
    if not plugin_commands:
        raise ValueError('Missing actual plugin compile commands')
    plugin_tokens = [tokens(entry['command']) for entry in plugin_commands]
    flags = sorted({token for command in plugin_tokens for token in command
                    if token.startswith('-') and token not in {'-o', '-c'}})
    if any(not all(flag in command for flag in ABI_FLAGS) or
           not any('LOTTIE_WEBGL_NATIVE_STATE_TRACE=1' in token for token in command)
           for command in plugin_tokens):
        raise ValueError('Actual plugin commands lack frozen ABI/trace flags')
    if any('pthread' in token.lower() or 'USE_PTHREADS' in token for entry in commands for token in tokens(entry['command'])):
        raise ValueError('Unexpected threaded effective compiler command')
    deps_text, _ = run([tool_paths['ninja'], '-C', args.build, '-t', 'deps'], 'ninja-deps')
    dependencies = set(Path(entry['file']).resolve() for entry in commands)
    for line in deps_text.splitlines():
        if line.startswith('    '):
            path = Path(line.strip())
            dependencies.add((path if path.is_absolute() else args.build / path).resolve(strict=True))
    if not any(path.suffix == '.h' for path in dependencies):
        raise ValueError('Ninja did not retain compiled header dependencies')
    # Include owning CMake and all package source/meta identities, so the consumer
    # can independently verify the portion present in its immutable package.
    tracked = git('ls-files').splitlines()
    rlottie_tracked = git('-C', 'dependency/rlottie', 'ls-files').splitlines()
    source_paths = {path.relative_to(ROOT).as_posix() for path in dependencies if inside(path, ROOT)}
    source_paths.update(name for name in tracked if name.endswith('CMakeLists.txt') or name.startswith('projects/CMake/'))
    source_paths.update('dependency/rlottie/' + name for name in rlottie_tracked
                        if name.endswith('CMakeLists.txt') or Path(name).suffix in {'.cmake', '.in'})
    source_paths.update(name for name in tracked if name.startswith('unity/RLottieUnity/Assets/LottiePlugin/') and
                        Path(name).suffix in {'.cs', '.meta', '.asmdef', '.json'})
    source_inputs = [descriptor(ROOT / name, name, allow_empty=True) for name in sorted(source_paths)]
    compile_descriptor = write_json(args.output / (PREFIX + 'compile-commands.json'), commands)
    deps_descriptor = write_json(args.output / (PREFIX + 'dependencies.json'),
                                 [descriptor(path, allow_empty=True) for path in sorted(dependencies)])
    cache_name = PREFIX + 'cmake-cache.txt'
    shutil.copyfile(args.build / 'CMakeCache.txt', args.output / cache_name)
    configuration = {'schema': 'lottie.webgl.native-state-trace.configuration/1', 'sourceCommit': source_commit,
                     'rlottieCommit': rlottie_commit, 'sourceInputs': source_inputs, 'compilerFlags': flags,
                     'tools': tools, 'unityVersion': '6000.5.3f1', 'emscriptenVersion': '4.0.20-git',
                     'cmakeVersion': cm_version.splitlines()[0], 'ninjaVersion': ninja_version.strip(),
                     'emConfig': descriptor(config), 'compiler': descriptor(compiler), 'python': descriptor(python),
                     'compilerWrapper': descriptor(ems / 'emcc.py'), 'cacheRoot': (ems / 'cache').as_posix(),
                     'sysroot': sysroot.as_posix(), 'frozenCache': True, 'compileCommands': compile_descriptor,
                     'dependencies': deps_descriptor, 'cmakeCache': descriptor(args.output / cache_name, cache_name),
                     'configure': configure}
    configuration_descriptor = write_json(args.output / (PREFIX + 'configuration.json'), configuration)

    def nm(path, undefined, label):
        argv = [tool_paths['llvmNm'], '--undefined-only' if undefined else '--defined-only',
                '--extern-only', '--format=posix', path]
        text, _ = run(argv, label, nm_archive=path)
        symbols = normalize_symbols(text, allow_empty=undefined)
        return symbols, {'archiveSha256': descriptor(path)['sha256'], 'argv': argv_strings(argv),
                         'exit': 0, 'inventorySha256': inventory_hash(symbols), **({'symbols': symbols} if undefined else {})}

    diag_path = args.output / plugin['path']
    defined_base, run_base = nm(plugin_baseline, False, 'baseline-defined')
    defined_diag, run_diag = nm(diag_path, False, 'diagnostic-defined')
    if not set(defined_base) <= set(defined_diag) or any(name.startswith('lottie_webgl_trace_') for name in defined_base):
        raise ValueError('Baseline symbols changed or unexpectedly contain trace API')
    if sorted(name for name in defined_diag if name.startswith('lottie_webgl_trace_')) != ADDED:
        raise ValueError('Diagnostic export set differs from frozen ABI')
    undefined = [nm(path, True, label)[1] for path, label in
                 [(plugin_baseline, 'baseline-undefined'), (diag_path, 'diagnostic-undefined'),
                  (rlottie_baseline, 'rlottie-undefined')]]
    if unreviewed_external(undefined[1]['symbols'], defined_diag,
                           set(undefined[0]['symbols']) | set(undefined[2]['symbols'])):
        raise ValueError('Unexpected new native dependency; retain output for review')
    emitted_archives = [descriptor(path) for path in sorted(args.build.rglob('*.a'))]
    if not emitted_archives or not any('rlottie' in item['path'] for item in emitted_archives):
        raise ValueError('Missing emitted dependency archive inventory')
    native_descriptor = write_json(args.output / (PREFIX + 'native-build-receipt.json'),
                                  {'schema': 'lottie.webgl.native-state-trace.native-build/1', 'sourceCommit': source_commit,
                                   'rlottieCommit': rlottie_commit, 'configurationReceipt': configuration_descriptor,
                                   'plugin': plugin, 'rlottie': rlottie, 'emittedArchives': emitted_archives,
                                   'build': build_command, 'undefinedSymbols': undefined})
    exports = {'added': ADDED, 'baselineInventorySha256': run_base['inventorySha256'],
               'diagnosticInventorySha256': run_diag['inventorySha256']}
    exports_descriptor = write_json(args.output / (PREFIX + 'exports.json'),
                                   {'schema': 'lottie.webgl.native-state-trace.exports/1', 'sourceCommit': source_commit,
                                    'llvmNm': tools['llvmNm'], **exports, 'runs': [run_base, run_diag]})
    header_inputs = [entry for entry in source_inputs if entry['path'].startswith('dependency/unity-pluginapi/')]
    if not header_inputs:
        raise ValueError('Missing actual Unity PluginAPI header inventory')
    manifest = {'schema': 'lottie.webgl.native-state-trace/1', 'abiVersion': 1, 'headerBytes': 128,
                'recordBytes': 192, 'capacity': 64, 'nativeDefine': 'LOTTIE_WEBGL_NATIVE_STATE_TRACE',
                'managedDefine': 'LOTTIE_WEBGL_NATIVE_STATE_TRACE', 'target': 'WebGL', 'variant': 'WasmExceptions',
                'configuration': 'Development', 'threading': 'single-main-thread', 'sourceCommit': source_commit,
                'rlottieCommit': rlottie_commit, 'unityPluginApiHeaders': {'ownerCommit': source_commit, 'files': header_inputs},
                'sourceInputs': source_inputs, 'toolchain': {'unityVersion': '6000.5.3f1', 'emscriptenVersion': '4.0.20-git',
                    'cmakeVersion': configuration['cmakeVersion'], 'ninjaVersion': configuration['ninjaVersion'],
                    **tools, 'configurationReceipt': configuration_descriptor},
                'compilerFlags': flags, 'plugin': plugin, 'rlottie': rlottie, 'baselinePluginSha256': BASE_PLUGIN,
                'baselineRlottieSha256': BASE_RLOTTIE, 'exports': exports,
                'buildReceipt': native_descriptor, 'exportsReceipt': exports_descriptor}
    # No passing manifest can be emitted if a build modified canonical inputs.
    if git('rev-parse', 'HEAD') != source_commit or git('status', '--porcelain', '--untracked-files=all'):
        raise ValueError('Canonical source changed during build')
    if descriptor(plugin_baseline)['sha256'] != BASE_PLUGIN or descriptor(rlottie_baseline)['sha256'] != BASE_RLOTTIE:
        raise ValueError('Production archive changed during build')
    manifest_descriptor = write_json(args.output / (PREFIX + 'manifest.json'), manifest)
    print(json.dumps({'manifest': manifest_descriptor, 'sourceCommit': source_commit,
                      'kind': 'native-build-only-not-Unity-or-release-qualification'}, indent=2))


if __name__ == '__main__':
    main()
