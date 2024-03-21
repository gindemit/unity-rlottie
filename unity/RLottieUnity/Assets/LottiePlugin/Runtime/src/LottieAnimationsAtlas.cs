using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace LottiePlugin
{
    public sealed class LottieAnimationsAtlas : IDisposable
    {
        public Texture2D Texture { get; private set; }
        public int[] CurrentFrame { get; private set; }
        //public double FrameRate => _animationWrappers.frameRate;
        //public long TotalFramesCount => _animationWrappers.totalFrames;
        //public double DurationSeconds => _animationWrappers.duration;
        public bool IsPlaying { get; private set; }

        private IntPtr[] _animationWrapperIntPtrs;
        private LottieAnimationWrapper[] _animationWrappers;

        private IntPtr[] _lottieRenderDataIntPtrs;
        private LottieRenderData[] _lottieRenderDatas;
        private NativeArray<byte>[] _pixelDatas;
        private float[] _timesSinceLastRenderCall;
        private double[] _frameDeltas;
        private bool[] _asyncDrawsWasCalled;

        private Action<int, int> DrawOneFrameCached;
        private Action<int, int> DrawOneFrameAsyncPrepareCached;

        //private LottieAnimationsAtlas(string jsonData, string resourcesPath, uint width, uint height)
        //{
        //    for (int i = 0; i < file)
        //    _animationWrappers = NativeBridge.LoadFromData(jsonData, resourcesPath, out _animationWrapperIntPtr);
        //    _frameDelta = _animationWrappers.duration / _animationWrappers.totalFrames;
        //    CreateRenderDataTexture2DMarshalToNative(width, height);
        //    IsPlaying = true;
        //    DrawOneFrameCached = DrawOneFrame;
        //    DrawOneFrameAsyncPrepareCached = DrawOneFrameAsyncPrepare;
        //}
        private LottieAnimationsAtlas(string[] jsonFilePaths, uint width, uint height)
        {
            int animationsCount = jsonFilePaths.Length;
            CurrentFrame = new int[animationsCount];
            _animationWrappers = new LottieAnimationWrapper[animationsCount];
            _animationWrapperIntPtrs = new IntPtr[animationsCount];
            _lottieRenderDataIntPtrs = new IntPtr[animationsCount];
            _lottieRenderDatas = new LottieRenderData[animationsCount];
            _pixelDatas = new NativeArray<byte>[animationsCount];
            _timesSinceLastRenderCall = new float[animationsCount];
            _frameDeltas = new double[animationsCount];
            _asyncDrawsWasCalled = new bool[animationsCount];
            for (int i = 0; i < jsonFilePaths.Length; ++i)
            {
                string jsonFilePath = jsonFilePaths[i];
                LottieAnimationWrapper animationWrapper = NativeBridge.LoadFromFile(jsonFilePath, out IntPtr animationWrapperIntPtr);
                _animationWrappers[i] = animationWrapper;
                _animationWrapperIntPtrs[i] = animationWrapperIntPtr;
                _frameDeltas[i] = animationWrapper.duration / animationWrapper.totalFrames;
            }
            CreateRenderDataAtlasTexture2DMarshalToNative(jsonFilePaths.Length, width, height);
            IsPlaying = true;
            DrawOneFrameCached = DrawOneFrame;
            DrawOneFrameAsyncPrepareCached = DrawOneFrameAsyncPrepare;
        }
        public void Dispose()
        {
            for (int i = 0; i < _pixelDatas.Length; ++i)
            {
                _pixelDatas[i].Dispose();
                NativeBridge.Dispose(_animationWrappers[i]);
                NativeBridge.LottieDisposeRenderData(ref _lottieRenderDataIntPtrs[i]);
            }
            _pixelDatas = null;
            _animationWrappers = null;
            _animationWrapperIntPtrs = null;
            UnityEngine.Object.DestroyImmediate(Texture);
            Texture = null;
        }
        public void Update(float animationSpeed = 1f)
        {
            UpdateInternal(animationSpeed, DrawOneFrameCached);
        }
        public void UpdateAsync(float animationSpeed = 1f)
        {
            UpdateInternal(animationSpeed, DrawOneFrameAsyncPrepareCached);
        }
        public void TogglePlay()
        {
            IsPlaying = !IsPlaying;
        }
        public void Play()
        {
            IsPlaying = true;
            for (int i = 0; i < CurrentFrame.Length; ++i)
            {
                DrawOneFrame(CurrentFrame[i], i);
            }
        }
        public void Pause()
        {
            IsPlaying = false;
        }
        public void Stop()
        {
            Pause();
            for (int i = 0; i < CurrentFrame.Length; ++i)
            {
                CurrentFrame[i] = 0;
            }
        }
        public void DrawOneFrame(int frameNumber, int index)
        {
            NativeBridge.LottieRenderImmediately(_animationWrapperIntPtrs[index], _lottieRenderDataIntPtrs[index], frameNumber, true);
            CurrentFrame[index] = frameNumber;
            Texture.Apply();
        }
        public void DrawOneFrameAsyncPrepare(int frameNumber, int index)
        {
            NativeBridge.LottieRenderCreateFutureAsync(_animationWrapperIntPtrs[index], _lottieRenderDataIntPtrs[index], frameNumber, true);
        }
        public void DrawOneFrameAsyncGetResult()
        {
            bool needToApplyTexture = false;
            for (int i = 0; i < _asyncDrawsWasCalled.Length; ++i)
            {
                if (_asyncDrawsWasCalled[i])
                {
                    NativeBridge.LottieRenderGetFutureResult(_animationWrapperIntPtrs[i], _lottieRenderDataIntPtrs[i]);
                    _asyncDrawsWasCalled[i] = false;
                    needToApplyTexture = true;
                }
            }
            if (needToApplyTexture)
            {
                Texture.Apply();
            }
        }

        private unsafe void CreateRenderDataAtlasTexture2DMarshalToNative(int animationCount, uint width, uint height)
        {
            _lottieRenderDatas = new LottieRenderData[animationCount];
            int halfOfAnimationsCount = animationCount / 2;
            Texture = new Texture2D(
                (int)width * halfOfAnimationsCount,
                (int)height * halfOfAnimationsCount,
                TextureFormat.BGRA32,
                0,
                false);

            NativeArray<byte> pixelData = Texture.GetRawTextureData<byte>();
            uint bytesPerLine = width * sizeof(uint);
            int oneTextureSizeBytes = (int)(width * height * sizeof(uint));
            void* firstTextureInAtlas = pixelData.GetUnsafePtr();
            for (int i = 0; i < animationCount; ++i)
            {
                void* shiftedPointer = ((byte*)firstTextureInAtlas) + oneTextureSizeBytes * i;

                LottieRenderData lottieRenderData = new LottieRenderData {
                    width = width,
                    height = height,
                    bytesPerLine = bytesPerLine,
                    buffer = shiftedPointer
                };

                IntPtr lottieRenderDataIntPtr = IntPtr.Zero;
                NativeBridge.LottieAllocateRenderData(ref lottieRenderDataIntPtr);
                Marshal.StructureToPtr(lottieRenderData, lottieRenderDataIntPtr, false);

                _lottieRenderDatas[i] = lottieRenderData;
                _lottieRenderDataIntPtrs[i] = lottieRenderDataIntPtr;
            }
        }
        private void UpdateInternal(float animationSpeed, Action<int, int> drawOneFrameMethod)
        {
            for (int i = 0; i < _timesSinceLastRenderCall.Length; ++i)
            {
                if (IsPlaying)
                {
                    _timesSinceLastRenderCall[i] += Time.deltaTime * animationSpeed;
                }
                if (_timesSinceLastRenderCall[i] >= _frameDeltas[i])
                {
                    int framesDelta = Mathf.RoundToInt(_timesSinceLastRenderCall[i] / (float)_frameDeltas[i]);
                    CurrentFrame[i] += framesDelta;
                    if (CurrentFrame[i] >= _animationWrappers[i].totalFrames)
                    {
                        CurrentFrame[i] = 0;
                    }
                    drawOneFrameMethod(CurrentFrame[i], i);
                    _asyncDrawsWasCalled[i] = true;
                    _timesSinceLastRenderCall[i] = 0;
                }
            }
        }

        public static LottieAnimationsAtlas LoadFromJsonFiles(string[] filePaths, uint width, uint height)
        {
            if (filePaths == null || filePaths.Length == 0)
            {
                throw new System.ArgumentException($"File at paths array is null or empty");

            }
            for (int i = 0; i < filePaths.Length; ++i)
            {
                string filePath = filePaths[i];
                if (!System.IO.File.Exists(filePath))
                {
                    throw new System.ArgumentException($"Can not find file at path: \"{filePath}\", at position \"{i.ToString()}\"");
                }
            }
            return new LottieAnimationsAtlas(filePaths, width, height);
        }
        //public static LottieAnimationsAtlas LoadFromJsonData(string jsonData, string resourcesPath, uint width, uint height)
        //{
        //    if (string.IsNullOrWhiteSpace(jsonData))
        //    {
        //        throw new System.ArgumentException($"The provided json animation file is empty");
        //    }
        //    return new LottieAnimationsAtlas(jsonData, resourcesPath, width, height);
        //}
    }
}
