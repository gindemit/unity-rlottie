import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import {createRequire} from 'node:module';
import test from 'node:test';
const root = process.env.LOTTIE_WORKER_ROOT || new URL('../unity/RLottieUnity/Assets/LottiePlugin/Editor/WebGLArchives/WasmExceptions/RasterWorker~/', import.meta.url).pathname.replace(/^\/C:/,'C:');
function animation(color) {
  return JSON.stringify({v:'5.7.4',fr:30,ip:0,op:30,w:16,h:16,ddd:0,assets:[],layers:[
    {ddd:0,ind:1,ty:4,sr:1,ks:{o:{a:0,k:100},r:{a:0,k:0},p:{a:0,k:[0,0,0]},a:{a:0,k:[0,0,0]},s:{a:0,k:[100,100,100]}},
      shapes:[{ty:'rc',p:{a:0,k:[8,8]},s:{a:0,k:[16,16]},r:{a:0,k:0}},
        {ty:'fl',c:{a:0,k:color},o:{a:0,k:100},r:1}]}],markers:[]});
}
test('real WASM content keys separate animations, rectangular stride and frame bounds', async () => {
  const create = createRequire(import.meta.url)(path.resolve(root,'raster.js'));
  const module = await create({wasmBinary:fs.readFileSync(path.resolve(root,'raster.wasm'))});
  const handles = [];
  const pixels = module._malloc(16*32*4);
  try {
    for (const color of [[1,0,0,1],[0,1,0,1]]) {
      const value = animation(color), size = module.lengthBytesUTF8(value)+1, json = module._malloc(size);
      module.stringToUTF8(value,json,size);
      try { handles.push(module._lottie_worker_create(json)); } finally { module._free(json); }
    }
    assert.ok(handles.every(Boolean));
    const snapshots = [];
    for (const handle of [handles[0],handles[1],handles[0]]) {
      assert.equal(module._lottie_worker_render(handle,0,pixels,16,32),1);
      snapshots.push(module.HEAPU8.slice(pixels,pixels+16*32*4));
    }
    assert.notDeepEqual(snapshots[0],snapshots[1]);
    assert.deepEqual(snapshots[0],snapshots[2]);
    const center = (16*16+8)*4;
    assert.deepEqual(Array.from(snapshots[0].slice(center,center+4)),[0,0,255,255]);
    assert.deepEqual(Array.from(snapshots[1].slice(center,center+4)),[0,255,0,255]);
    assert.equal(module._lottie_worker_render(handles[0],9999,pixels,16,32),0);
    assert.equal(module._lottie_worker_render(handles[0],0,pixels,4097,1),0);
  } finally { module._free(pixels); for (const handle of handles) if (handle) module._lottie_worker_destroy(handle); }
});
