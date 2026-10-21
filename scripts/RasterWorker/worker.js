/* Independent CPU-only rlottie runtime. One serial queue per ordinary worker. */
importScripts('raster.js');
const runtime = LottieRasterModule();
const owners = new Map();
let queue = Promise.resolve();
self.onmessage = ({data}) => {
  queue = queue.then(async () => {
    const module = await runtime;
    if (data.dispose) {
      const owner = owners.get(data.id);
      if (owner) module._lottie_worker_destroy(owner.handle);
      owners.delete(data.id);
      return;
    }
    let pixels = 0;
    try {
      const stride = data.width * data.height * 4;
      if (!Number.isSafeInteger(data.id) || data.id < 1 || !Number.isSafeInteger(data.request) || data.request < 1 ||
          !Number.isInteger(data.width) || data.width < 1 || data.width > 4096 ||
          !Number.isInteger(data.height) || data.height < 1 || data.height > 4096 ||
          !Array.isArray(data.frames) || data.frames.length < 1 || data.frames.length > 32 ||
          !Number.isSafeInteger(stride * data.frames.length) || stride * data.frames.length > 67108864 ||
          data.frames.some(frame => !Number.isInteger(frame) || frame < 0) || typeof data.json !== 'string')
        throw Error('Invalid raster batch');
      let owner = owners.get(data.id);
      if (owner && (owner.json !== data.json || owner.width !== data.width || owner.height !== data.height || data.request <= owner.request))
        throw Error('Invalid owner/request identity');
      if (!owner) {
        const length = module.lengthBytesUTF8(data.json) + 1;
        const json = module._malloc(length);
        if (!json) throw Error('JSON allocation failed');
        module.stringToUTF8(data.json, json, length);
        let handle;
        try { handle = module._lottie_worker_create(json); } finally { module._free(json); }
        if (!handle) throw Error('Invalid animation');
        owner = {handle, json:data.json, width:data.width, height:data.height, request:0};
        owners.set(data.id, owner);
      }
      owner.request = data.request;
      pixels = module._malloc(stride * data.frames.length);
      if (!pixels) throw Error('Pixel allocation failed');
      for (let index = 0; index < data.frames.length; index++) {
        if (module._lottie_worker_render(owner.handle, data.frames[index], pixels + index * stride, data.width, data.height) !== 1)
          throw Error('Invalid frame');
      }
      const bytes = module.HEAPU8.slice(pixels, pixels + stride * data.frames.length);
      self.postMessage({id:data.id, request:data.request, bytes, memoryBytes:module.HEAPU8.length}, [bytes.buffer]);
    } catch (error) {
      self.postMessage({id:data.id, request:data.request, error:String(error)});
    } finally { if (pixels) module._free(pixels); }
  }).catch(error => self.postMessage({id:data.id, request:data.request, error:String(error)}));
};
