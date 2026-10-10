mergeInto(LibraryManager.library, {
  $LottieRaster: {
    next: 1, pools: new Map(), owners: new Map(),
    fail: function(slot) {
      if (slot.failed) return;
      slot.failed = true;
      slot.worker.terminate();
      for (var owner of LottieRaster.owners.values()) if (owner.slot === slot) owner.failed = true;
    },
    create: function() {
      var slot = {failed:false, worker:new Worker(new URL('LottieRaster/worker.js', document.baseURI).href)};
      slot.worker.onmessage = function(event) {
        var data = event.data;
        if (!data || typeof data !== 'object') { LottieRaster.fail(slot); return; }
        var owner = LottieRaster.owners.get(data.id);
        if (!owner) return; // Retired owner; never publish late pixels.
        if (owner.slot !== slot || !owner.pending || data.request !== owner.request) { LottieRaster.fail(slot); return; }
        if (data.error || !(data.bytes instanceof Uint8Array) || data.bytes.length !== owner.expected || owner.bytes) {
          LottieRaster.fail(slot); return;
        }
        owner.bytes = data.bytes;
      };
      slot.worker.onerror = function(event) { event.preventDefault(); LottieRaster.fail(slot); };
      slot.worker.onmessageerror = function() { LottieRaster.fail(slot); };
      return slot;
    }
  },
  LottieRasterPoolCreate__deps: ['$LottieRaster'],
  LottieRasterPoolCreate: function(count) {
    var workers = [];
    try {
      if (typeof Worker === 'undefined' || count < 1 || count > 8) return 0;
      for (var i = 0; i < count; i++) workers.push(LottieRaster.create());
      var id = LottieRaster.next++;
      LottieRaster.pools.set(id, {workers:workers, next:0});
      return id;
    } catch (_) { for (var slot of workers) LottieRaster.fail(slot); return 0; }
  },
  LottieRasterPoolDispose__deps: ['$LottieRaster'],
  LottieRasterPoolDispose: function(id) {
    var pool = LottieRaster.pools.get(id);
    if (!pool) return;
    for (var slot of pool.workers) LottieRaster.fail(slot);
    for (var entry of LottieRaster.owners) if (entry[1].pool === id) LottieRaster.owners.delete(entry[0]);
    LottieRaster.pools.delete(id);
  },
  LottieRasterCreate__deps: ['$LottieRaster'],
  LottieRasterCreate: function(poolId, json, width, height) {
    var pool = LottieRaster.pools.get(poolId);
    if (!pool || width < 1 || height < 1 || width > 4096 || height > 4096) return 0;
    var slot = pool.workers[pool.next++ % pool.workers.length];
    if (slot.failed) return 0;
    var id = LottieRaster.next++;
    LottieRaster.owners.set(id, {pool:poolId, slot:slot, json:UTF8ToString(json), width:width, height:height,
      request:0, pending:false, bytes:null, failed:false});
    return id;
  },
  LottieRasterStart__deps: ['$LottieRaster'],
  LottieRasterStart: function(id, frames, count) {
    var owner = LottieRaster.owners.get(id);
    if (!owner || owner.failed || owner.pending || count < 1 || count > 32) return -1;
    var expected = count * owner.width * owner.height * 4;
    if (!Number.isSafeInteger(expected) || expected > 67108864) return -1;
    try {
      owner.expected = expected;
      owner.pending = true;
      owner.request++;
      owner.slot.worker.postMessage({id:id, request:owner.request, json:owner.json, width:owner.width, height:owner.height,
        frames:Array.from(HEAP32.subarray(frames >> 2, (frames >> 2) + count))});
      return 1;
    } catch (_) { LottieRaster.fail(owner.slot); return -1; }
  },
  LottieRasterPoll__deps: ['$LottieRaster'],
  LottieRasterPoll: function(id, pixels, count) {
    var owner = LottieRaster.owners.get(id);
    if (!owner || owner.failed) return -1;
    if (!owner.bytes) return 0;
    if (count !== owner.expected) { LottieRaster.fail(owner.slot); return -1; }
    HEAPU8.set(owner.bytes, pixels);
    owner.bytes = null;
    owner.pending = false;
    return 1;
  },
  LottieRasterDispose__deps: ['$LottieRaster'],
  LottieRasterDispose: function(id) {
    var owner = LottieRaster.owners.get(id);
    if (!owner) return;
    LottieRaster.owners.delete(id);
    // A pending owner was abandoned (including timeout). Stop its shared slot and
    // make other owners recover, rather than leave a stalled worker alive.
    if (owner.pending) LottieRaster.fail(owner.slot);
    else if (!owner.slot.failed) {
      try { owner.slot.worker.postMessage({id:id, dispose:true}); }
      catch (_) { LottieRaster.fail(owner.slot); }
    }
  }
});
