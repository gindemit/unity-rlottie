"""Compile actual backend + export slice with labelled host stubs, never Unity/Wasm.

Usage: python native_contract.py --compiler <installed clang++ or cl.exe> --output <fresh external directory>
No CMake, native archive, package, dependency, cache or browser operation is performed.
"""
from pathlib import Path
import argparse
import ctypes
import hashlib
import json
import os
import re
import subprocess


def digest(data):
    return hashlib.sha256(data).hexdigest()


def external_fresh(path):
    if not path.is_absolute() or os.path.lexists(path):
        raise ValueError('Output must be an absolute fresh external directory')
    for parent in (path, *path.parents):
        if (parent / '.git').exists() or parent.is_symlink():
            raise ValueError('Output crosses a Git worktree or symlink')
        if os.name == 'nt' and parent.exists():
            attributes = ctypes.windll.kernel32.GetFileAttributesW(str(parent))
            if attributes != -1 and attributes & 0x400:
                raise ValueError('Output crosses a reparse point')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--compiler', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--host-include', action='append', type=Path, default=[], help='Installed host C++/SDK include (repeatable)')
    parser.add_argument('--host-lib', action='append', type=Path, default=[], help='Installed host SDK library directory (repeatable)')
    parser.add_argument('--host-linker', type=Path, help='Installed host linker, never the Wasm linker')
    args = parser.parse_args()
    external_fresh(args.output)
    compiler = args.compiler.resolve(strict=True)
    msvc = compiler.name.lower() == 'cl.exe'
    if msvc and args.host_linker:
        raise ValueError('cl.exe selects its sibling linker; do not supply --host-linker')
    includes = [directory.resolve(strict=True) for directory in args.host_include]
    libraries = [directory.resolve(strict=True) for directory in args.host_lib]
    environment = os.environ.copy()
    if msvc:
        # cl launches its matching link.exe by name. Keep the installed host tool
        # directory first, without mutating the parent shell or Unity toolchain.
        environment['PATH'] = str(compiler.parent) + os.pathsep + environment.get('PATH', '')
    repo = Path(__file__).resolve().parents[2]
    names = ['src/WebGLBackend.cpp', 'src/WebGLBackend.h', 'src/LottiePlugin.h',
             'src/LottiePluginApi.cpp', 'tests/native/NativeWebGLTraceTests.cpp']
    inputs = {name: (repo / name).read_bytes() for name in names}
    output = args.output
    output.mkdir(parents=True)
    for name in ('src/WebGLBackend.cpp', 'src/WebGLBackend.h', 'tests/native/NativeWebGLTraceTests.cpp'):
        (output / Path(name).name).write_bytes(inputs[name])
    header = inputs['src/LottiePlugin.h'].decode('utf-8')
    structures = []
    offsets = []
    for name, count in [('LottieWebGLTraceHeader', 32), ('LottieWebGLTraceRecord', 48)]:
        match = re.search(r'    struct ' + name + r'\s*\{(.*?)\n    \};', header, re.S)
        if not match:
            raise ValueError('Actual ABI declaration not found: ' + name)
        structures.append(match.group(0))
        fields = [field.strip() for group in re.findall(r'uint32_t\s+([^;]+);', match.group(1))
                  for field in group.split(',')]
        assert len(fields) == count
        offsets += [f'static_assert(offsetof({name}, {field}) == {index*4}, "{name}.{field}");'
                    for index, field in enumerate(fields)]
    (output / 'native_offsets.inc').write_text(
        '#if defined(LOTTIE_WEBGL_NATIVE_STATE_TRACE)\n' + '\n'.join(offsets) + '\n#endif\n', encoding='utf-8')
    api = inputs['src/LottiePluginApi.cpp'].decode('utf-8')
    first = api.index('    EXPORT_API int32_t lottie_webgl_trace_arm(')
    start = api.rfind('#if defined(LOTTIE_WEBGL_NATIVE_STATE_TRACE)', 0, first)
    end = api.index('#endif', first) + len('#endif')
    export_slice = 'extern "C" {\n' + api[start:end] + '\n}\n'
    assert export_slice.count('EXPORT_API int32_t lottie_webgl_trace_') == 5
    pending_export = re.search(
        r'    EXPORT_API int32_t lottie_get_webgl_pending_upload_count\(void\)\s*\{[^}]*\}', api)
    pending_declaration = re.search(
        r'    EXPORT_API int32_t lottie_get_webgl_pending_upload_count\(void\);', header)
    if not pending_export or not pending_declaration:
        raise ValueError('Actual WebGL pending-count ABI not found')
    export_slice += 'extern "C" {\n' + pending_export.group(0) + '\n}\n'
    (output / 'native_exports.inc').write_text(export_slice, encoding='utf-8')
    stub = '''// HOST STUB: not Unity's full LottiePlugin.h. ABI structs below are exact source slices.
#pragma once
#include <cstdint>
#include <cstddef>
#define UNITY_INTERFACE_API
#define EXPORT_API
struct lottie_animation_wrapper {};
struct lottie_render_data { uint32_t* buffer; uint32_t width, height, bytesPerLine; };
typedef void (*UnityRenderingEvent)(int);
#if defined(LOTTIE_WEBGL_NATIVE_STATE_TRACE)
''' + '\n'.join(structures) + '\n#endif\n'
    (output / 'LottiePlugin.h').write_text(stub, encoding='utf-8')
    gl = '''// HOST STUB: call-order/value oracle only; no GL implementation.
#pragma once
#include <cstdint>
using GLenum=uint32_t; using GLuint=uint32_t; using GLint=int32_t; using GLsizei=int32_t; using GLboolean=uint8_t;
constexpr GLenum GL_TEXTURE_BINDING_2D=0x8069, GL_UNPACK_ALIGNMENT=0x0cf5, GL_TEXTURE_2D=0x0de1;
constexpr GLenum GL_PIXEL_UNPACK_BUFFER_BINDING=0x88ef, GL_UNPACK_ROW_LENGTH=0x0cf2;
constexpr GLenum GL_UNPACK_SKIP_ROWS=0x0cf3, GL_UNPACK_SKIP_PIXELS=0x0cf4;
constexpr GLenum GL_RGBA=0x1908, GL_UNSIGNED_BYTE=0x1401, GL_NO_ERROR=0;
constexpr GLboolean GL_FALSE=0, GL_TRUE=1;
GLboolean glIsTexture(GLuint); void glGetIntegerv(GLenum,GLint*); GLenum glGetError();
void glBindTexture(GLenum,GLuint); void glPixelStorei(GLenum,GLint);
void glTexSubImage2D(GLenum,GLint,GLint,GLint,GLsizei,GLsizei,GLenum,GLenum,const void*);
'''
    ems = '''// HOST STUB: context facts supplied by tests, not a real browser.
#pragma once
using EMSCRIPTEN_WEBGL_CONTEXT_HANDLE=int;
struct EmscriptenWebGLContextAttributes { int majorVersion, minorVersion; };
constexpr int EMSCRIPTEN_RESULT_SUCCESS=0;
int emscripten_webgl_get_current_context();
int emscripten_webgl_get_context_attributes(int,EmscriptenWebGLContextAttributes*);
bool emscripten_is_webgl_context_lost(int);
'''
    for directory, filename, body in [('GLES2', 'gl2.h', gl), ('GLES3', 'gl3.h', '#include <GLES2/gl2.h>\n'),
                                      ('emscripten', 'html5_webgl.h', ems),
                                      ('emscripten', 'heap.h', '#include <cstddef>\nsize_t emscripten_get_heap_size();\n')]:
        (output / directory).mkdir(exist_ok=True)
        (output / directory / filename).write_text(body, encoding='utf-8')
    receipt = {'kind': 'actual-source-host-stub-contracts-not-WebGL',
               'compiler': {'path': str(compiler), 'sha256': digest(compiler.read_bytes())},
               'harnessSha256': digest(Path(__file__).read_bytes()),
               'inputs': {name: {'sha256': digest(body), 'bytes': len(body)} for name, body in inputs.items()},
               'runs': []}
    if msvc:
        linker = compiler.with_name('link.exe').resolve(strict=True)
        receipt['linker'] = {'path': str(linker), 'sha256': digest(linker.read_bytes())}
    try:
        for mode, define in [('selected', []), ('compile-off', ['-DLOTTIE_HOST_TRACE_OFF=1']),
                             ('threads-unsupported', ['-D__EMSCRIPTEN_PTHREADS__=1'])]:
            executable = output / (mode + ('.exe' if os.name == 'nt' else ''))
            if msvc:
                command = [str(compiler), '/nologo', '/std:c++17', '/EHsc', '/W4', '/MD',
                           '/I' + str(output), *['/D' + value[2:] for value in define],
                           *['/I' + str(directory) for directory in includes],
                           str(output / 'NativeWebGLTraceTests.cpp'), '/Fe:' + str(executable),
                           '/Fo:' + str(output / (mode + '.obj')), '/link',
                           *['/LIBPATH:' + str(directory) for directory in libraries]]
            else:
                command = [str(compiler), '-std=c++17', '-Wall', '-Wextra', '-I', str(output),
                           *define, str(output / 'NativeWebGLTraceTests.cpp'), '-o', str(executable)]
                if os.name == 'nt':
                    command.insert(1, '--target=x86_64-pc-windows-msvc')
                if args.host_linker:
                    command.insert(1, '-fuse-ld=' + str(args.host_linker.resolve(strict=True)))
                for directory in includes:
                    command += ['-isystem', str(directory)]
                for directory in libraries:
                    command += ['-L', str(directory)]
            build = subprocess.run(command, cwd=output, env=environment, capture_output=True, text=True, timeout=90)
            run = {'mode': mode, 'compileCommand': command, 'compileExit': build.returncode,
                   'compileOutput': build.stdout + build.stderr}
            receipt['runs'].append(run)
            (output / (mode + '.compile.log')).write_text(run['compileOutput'], encoding='utf-8')
            if build.returncode != 0:
                raise RuntimeError(mode + ' host compile failed; see external log')
            completed = subprocess.run([str(executable)], cwd=output, capture_output=True, text=True, timeout=20)
            run.update({'exit': completed.returncode, 'output': completed.stdout + completed.stderr,
                        'executableSha256': digest(executable.read_bytes())})
            if completed.returncode != 0:
                raise RuntimeError(mode + ' host contracts failed: ' + run['output'])
        receipt['passed'] = True
    finally:
        receipt['canonicalInputsUnchanged'] = all((repo / name).read_bytes() == body for name, body in inputs.items())
        (output / 'native-host-receipt.json').write_text(json.dumps(receipt, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({'passed': receipt['passed'],
                      'canonicalInputsUnchanged': receipt['canonicalInputsUnchanged'],
                      'runs': [{'mode': run['mode'], 'output': run['output'].strip()} for run in receipt['runs']],
                      'receipt': str(output / 'native-host-receipt.json')}, indent=2))


if __name__ == '__main__':
    main()
