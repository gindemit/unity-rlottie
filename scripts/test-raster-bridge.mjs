import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import test from 'node:test';

function setup(blocked = false) {
  const workers = [];
  class Worker {
    constructor(url) { if (blocked) throw Error('blocked'); this.url = url; this.messages = []; workers.push(this); }
    postMessage(data) { this.messages.push(data); }
    terminate() { this.terminated = true; }
  }
  const context = vm.createContext({Worker, URL, Uint8Array, Map, Array, document:{baseURI:'https://host.test/sub/game/'},
    UTF8ToString:()=>'{"plant":true}', HEAP32:new Int32Array([1,2,3,4]), HEAPU8:new Uint8Array(64*32*4*8),
    LibraryManager:{library:{}}, mergeInto:(library,items)=>Object.assign(library,items)});
  vm.runInContext(fs.readFileSync(new URL('../unity/RLottieUnity/Assets/LottiePlugin/Plugins/WebGL/LottieRaster.jslib', import.meta.url), 'utf8'), context);
  const api = context.LibraryManager.library;
  context.LottieRaster = api.$LottieRaster;
  const pool = api.LottieRasterPoolCreate(2);
  return {api, workers, context, pool};
}
test('construction denial falls back without leaking workers', () => {
  const {pool, workers} = setup(true); assert.equal(pool, 0); assert.equal(workers.length, 0);
});
test('palette shares explicit pool and subdirectory host paths', () => {
  const {api,workers,pool,context} = setup();
  const ids = Array.from({length:7}, () => api.LottieRasterCreate(pool, 1, 64, 32));
  ids.forEach(id => { assert.equal(api.LottieRasterStart(id,0,4),1); assert.equal(api.LottieRasterStart(id,0,4),-1); });
  assert.equal(workers.length,2);
  assert.equal(workers[0].url,'https://host.test/sub/game/LottieRaster/worker.js');
  ids.forEach(id => api.LottieRasterDispose(id));
  api.LottieRasterPoolDispose(pool);
  assert.ok(workers.every(worker=>worker.terminated));
  assert.equal(context.LottieRaster.owners.size,0);
  assert.equal(context.LottieRaster.pools.size,0);
});
test('rectangular pixels transfer once with increasing request identity', () => {
  const {api,workers,pool,context} = setup(); const id = api.LottieRasterCreate(pool,1,64,32);
  api.LottieRasterStart(id,0,1);
  const bytes = new Uint8Array(64*32*4); bytes[9]=42;
  assert.equal(api.LottieRasterPoll(id,0,bytes.length),0);
  workers[0].onmessage({data:{id,request:1,bytes}});
  assert.equal(api.LottieRasterPoll(id,0,bytes.length),1);
  assert.equal(context.HEAPU8[9],42);
  assert.equal(api.LottieRasterPoll(id,0,bytes.length),0);
  api.LottieRasterStart(id,0,1);
  assert.equal(workers[0].messages.at(-1).request,2);
});
for (const bad of ['length','request','malformed','duplicate','wrong-slot']) test('rejects '+bad+' replies and terminates worker', () => {
  const {api,workers,pool} = setup(); const id = api.LottieRasterCreate(pool,1,64,32);
  api.LottieRasterStart(id,0,1);
  const response = {id,request:1,bytes:new Uint8Array(bad==='length'?1:64*32*4)};
  if (bad==='request') response.request=99;
  if (bad==='duplicate') workers[0].onmessage({data:response});
  const slot = workers[bad==='wrong-slot'?1:0];
  slot.onmessage({data:bad==='malformed'?null:response});
  assert.ok(slot.terminated);
  // Wrong-slot corruption terminates that slot; the legitimate owner is still pending.
  assert.equal(api.LottieRasterPoll(id,0,64*32*4),bad==='wrong-slot'?0:-1);
});
test('pending retirement fences late replies and fails other owners on stalled slot', () => {
  const {api,workers,pool,context}=setup();
  const ids=Array.from({length:3},()=>api.LottieRasterCreate(pool,1,64,32));
  ids.forEach(id=>api.LottieRasterStart(id,0,1));
  api.LottieRasterDispose(ids[0]);
  assert.ok(workers[0].terminated);
  workers[0].onmessage({data:{id:ids[0],request:1,bytes:new Uint8Array(64*32*4)}});
  assert.equal(api.LottieRasterPoll(ids[2],0,64*32*4),-1);
  assert.equal(context.LottieRaster.owners.size,2);
});
test('rejects oversized dimensions and batch before posting', () => {
  const {api,workers,pool}=setup();
  assert.equal(api.LottieRasterCreate(pool,1,4097,1),0);
  const id=api.LottieRasterCreate(pool,1,4096,4096);
  assert.equal(api.LottieRasterStart(id,0,2),-1);
  assert.equal(workers[0].messages.length,0);
});
