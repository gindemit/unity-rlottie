using UnityEngine;
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;

public sealed partial class Lottie
{
    public class Sprite
    {
        internal struct RenderData
        {
            public LottieRenderData renderData;
            public IntPtr renderDataPtr;
        }

        public Texture2D Texture => atlas.Texture;
        private Atlas atlas;

        private Vector2 uvMin;
        private Vector2 uvMax;

        private Vector2Int atlasSize;
        private Vector2Int size;
        private Vector2Int pixelStart;

        private RenderData[] renderData = new RenderData[2];

        private bool isSetup;
        public Vector2 UvMin => uvMin;
        public Vector2 UvMax => uvMax;
        public float Aspect { get; set; }

        public event Action TextureChanged;

        public Sprite(Atlas atlas, RectInt rect, float aspect)
        {
            Aspect = aspect;
            this.atlas = atlas;
            atlasSize = atlas.size;

            var rAspect = (float)rect.width / rect.height;
            float fitW, fitH;

            if (rAspect > aspect)
            {
                fitH = rect.height;
                fitW = fitH * aspect;
            }
            else
            {
                fitW = rect.width;
                fitH = fitW / aspect;
            }

            var x = rect.x + (rect.width - fitW) * 0.5f;
            var y = rect.y + (rect.height - fitH) * 0.5f;

            uvMin = new Vector2(x / atlasSize.x, y / atlasSize.y);
            uvMax = new Vector2((x + fitW) / atlasSize.x, (y + fitH) / atlasSize.y);

            atlas.TextureChanged += OnTextureChanged;
            Init();
        }

        public Sprite(Lottie lottie) : this(Atlas.Get(lottie.maxSide), new RectInt(Vector2Int.zero, new Vector2Int(lottie.maxSide, lottie.maxSide)),
            lottie.aspect)
        {
            
        }

        private void OnTextureChanged()
        {
            TextureChanged?.Invoke();
        }

        private void Init()
        {
            var atlasWidth = atlasSize.x;
            var atlasHeight = atlasSize.y;

            float u0 = uvMin.x, v0 = uvMin.y;
            float u1 = uvMax.x, v1 = uvMax.y;

            var x = Mathf.FloorToInt(u0 * atlasWidth);
            var y = Mathf.FloorToInt(v0 * atlasHeight);
            var w = Mathf.CeilToInt((u1 - u0) * atlasWidth);
            var h = Mathf.CeilToInt((v1 - v0) * atlasHeight);

            x = Mathf.Clamp(x, 0, atlasWidth - 1);
            y = Mathf.Clamp(y, 0, atlasHeight - 1);
            pixelStart = new Vector2Int(x, y);
            var width = Mathf.Clamp(w, 1, atlasWidth - x);
            var height = Mathf.Clamp(h, 1, atlasHeight - y);
            size = new Vector2Int(width, height);
            SetupRenderData();
        }

        public void Destroy()
        {
            ReleaseRenderData();
            atlas.TextureChanged -= OnTextureChanged;
            atlas = null;
        }

        private unsafe void SetupRenderData()
        {
            if (isSetup) return;
            isSetup = true;
            const int bpp = 4;

            for (var i = 0; i < renderData.Length; i++)
            {
                ref var data = ref renderData[i];
                var basePtr = (byte*)atlas.textures[i].GetRawTextureData<byte>().GetUnsafePtr();
                var atlasStride = atlasSize.x * bpp;
                var startOffset = pixelStart.y * atlasStride + pixelStart.x * bpp;
                var regionPtr = basePtr + startOffset;

                data.renderData.width = (uint)size.x;
                data.renderData.height = (uint)size.y;
                data.renderData.bytesPerLine = (uint)atlasStride;
                data.renderData.buffer = regionPtr;

                NativeBridge.LottieAllocateRenderData(ref data.renderDataPtr);
                Marshal.StructureToPtr(data.renderData, data.renderDataPtr, false);
            }
        }

        private void ReleaseRenderData()
        {
            if (!isSetup) return;
            isSetup = false;
            CompletePending();
            for (var i = 0; i < renderData.Length; i++)
            {
                ref var data = ref renderData[i];
                NativeBridge.LottieDisposeRenderData(ref data.renderDataPtr);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void DrawOneFrameSyncInternal(IntPtr animationWrapperIntPtr, int frameNumber)
        {
            NativeBridge.LottieRenderImmediately(animationWrapperIntPtr, renderData[atlas.textureIndex].renderDataPtr,
                frameNumber, true);
            animationWrapperPtr = animationWrapperIntPtr;
            atlas.IsTextureDirty = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void DrawOneFrameAsyncPrepareInternal(IntPtr animationWrapperIntPtr, int frameNumber)
        {
            if (hasPending) return;
            hasPending = true;
            animationWrapperPtr = animationWrapperIntPtr;
            pendingRenderDataPrt = renderData[atlas.textureIndex].renderDataPtr;
            NativeBridge.LottieRenderCreateFutureAsync(animationWrapperIntPtr, pendingRenderDataPrt, frameNumber, true);
        }

        public void FetchTexture()
        {
            if (!hasPending) return;

            NativeBridge.LottieRenderGetFutureResult(animationWrapperPtr, pendingRenderDataPrt);
            atlas.IsTextureDirty = true;
            hasPending = false;
        }

        private bool hasPending;
        private IntPtr animationWrapperPtr;
        private IntPtr pendingRenderDataPrt;

        private void CompletePending()
        {
            if (!hasPending) return;

            NativeBridge.LottieRenderGetFutureResult(animationWrapperPtr, pendingRenderDataPrt);
            hasPending = false;
        }
    }
}