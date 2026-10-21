import fs from 'node:fs';
import path from 'node:path';
import {createHash} from 'node:crypto';
import {createRequire} from 'node:module';
const request = JSON.parse(fs.readFileSync(process.argv[2]));
const create = createRequire(import.meta.url)(path.join(request.worker, 'raster.js'));
const module = await create({wasmBinary:fs.readFileSync(path.join(request.worker, 'raster.wasm'))});
const result = {nativeSha256:request.nativeSha256, workerProvenance:request.workerProvenance, frames:0, cases:[], exact:true};
for (const row of request.cases) {
  const data = fs.readFileSync(row.json, 'utf8'), length = module.lengthBytesUTF8(data)+1, json = module._malloc(length);
  const size = row.width * row.height * 4, pixels = module._malloc(size);
  if (!json || !pixels) throw Error('Allocation failed');
  module.stringToUTF8(data,json,length);
  let animation = 0;
  try {
    animation = module._lottie_worker_create(json);
    if (!animation) throw Error('WASM animation load failed');
    let differences = 0;
    row.frames.forEach((frame,index) => {
      if (module._lottie_worker_render(animation,frame,pixels,row.width,row.height) !== 1) throw Error('Invalid frame');
      const hash = createHash('sha256').update(module.HEAPU8.subarray(pixels,pixels+size)).digest('hex');
      if (hash !== row.pixelHashes[index]) differences++;
    });
    result.frames += row.frames.length;
    result.cases.push({json:row.json,sha256:row.sha256,width:row.width,height:row.height,frames:row.frames.length,differentFrames:differences});
    result.exact &&= differences === 0;
  } finally { module._free(json); module._free(pixels); if (animation) module._lottie_worker_destroy(animation); }
}
fs.writeFileSync(process.argv[3],JSON.stringify(result,null,2)+'\n');
console.log(JSON.stringify({frames:result.frames,cases:result.cases.length,exact:result.exact}));
if (!result.exact) process.exitCode = 1;
