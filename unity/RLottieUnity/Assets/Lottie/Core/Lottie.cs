using System;
using System.Runtime.CompilerServices;
using LSCore;
using LSCore.Extensions;
using UnityEngine;


public sealed partial class Lottie
{
    private static LSList<Lottie> toPack = new();
    private static bool toPackIsDirty;

    private static bool ToPackIsDirty
    {
        get => toPackIsDirty;
        set
        {
            if(toPackIsDirty == value) return;
            toPackIsDirty = value;
            if (value)
            {
                LottieUpdater.PreRendering[1] += Pack;
                LottieUpdater.CanvasPreRendering[1] += Pack;
            }
            else
            {
                LottieUpdater.PreRendering[1] -= Pack;
                LottieUpdater.CanvasPreRendering[1] -= Pack;
            }
        }
    }
    
    private static void Pack()
    {
        ToPackIsDirty = false;
        Atlas.Pack(toPack);
    }
    
    public static Vector2Int GetSize(string json)
    {
        var wh = JTokenExtensions.FindByPath(json, "w", "h");
        return new Vector2Int(wh[0].ToInt(), wh[1].ToInt());
    }
    
    public double FrameRate => animationWrapper.frameRate;
    public long TotalFramesCount => animationWrapper.totalFrames;
    public double DurationSeconds => animationWrapper.duration;

    private Sprite sprite;
    public Sprite Spritee
    {
        get => sprite;
        set
        {
            if(sprite == value) return;
            sprite?.Destroy();
            sprite = value;
            SpriteChanged?.Invoke(sprite);
        }
    }

    public event Action<Sprite> SpriteChanged;

    public int currentFrame;
    public bool loop = true;

    private IntPtr animationWrapperIntPtr;
    private LottieAnimationWrapper animationWrapper;
    
    internal int maxSide;
    private float timeSinceLastRenderCall;
    private float frameDelta;
    public bool hasRendererData;
    private float aspect;
    private readonly bool canPack;
    
    private readonly Action<int> drawOneFrameSyncCached;
    private readonly Action<int> drawOneFrameAsyncPrepareCached;
    
    public Lottie(BaseLottieAsset asset, string resourcesPath, uint maxSide, bool canPack)
    {
        this.maxSide = (int)maxSide;
        this.canPack = canPack;
        aspect = asset.Aspect;
        
        animationWrapper = NativeBridge.LoadFromData(asset.Json, resourcesPath, out animationWrapperIntPtr);
        frameDelta = (float)animationWrapper.duration / animationWrapper.totalFrames;

        AllowToRender();
        drawOneFrameSyncCached = DrawOneFrameSyncInternal;
        drawOneFrameAsyncPrepareCached = DrawOneFrameAsyncPrepareInternal;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Update(float animationSpeed = 1f) => UpdateInternal(animationSpeed * Time.deltaTime, drawOneFrameSyncCached);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UpdateAsync(float animationSpeed = 1f) => UpdateInternal(animationSpeed * Time.deltaTime, drawOneFrameAsyncPrepareCached);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UpdateDelta(float deltaTime) => UpdateInternal(deltaTime, drawOneFrameSyncCached);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UpdateDeltaAsync(float deltaTime) => UpdateInternal(deltaTime, drawOneFrameAsyncPrepareCached);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void FetchTexture() => sprite.FetchTexture();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DrawOneFrame(int frameNumber)
    {
        currentFrame = frameNumber;
        DrawCurrentFrame(drawOneFrameSyncCached);
    }

    private void UpdateInternal(float deltaTime, Action<int> drawMethod)
    {
        timeSinceLastRenderCall += deltaTime;

        if (timeSinceLastRenderCall < frameDelta)
            return;

        int framesDelta = Mathf.FloorToInt(timeSinceLastRenderCall / frameDelta);
        currentFrame += framesDelta;
        
        DrawCurrentFrame(drawMethod);

        timeSinceLastRenderCall -= framesDelta * frameDelta;
        if (timeSinceLastRenderCall < 0) timeSinceLastRenderCall = 0;
    }

    private void DrawCurrentFrame(Action<int> drawMethod)
    {
        int total = (int)animationWrapper.totalFrames;
        if (loop)
        {
            if (currentFrame >= total) currentFrame %= total;
        }
        else
        {
            if (currentFrame >= total) currentFrame = total - 1;
        }
        
        drawMethod(currentFrame);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void DrawOneFrameSyncInternal(int frameNumber)
    {
        sprite.DrawOneFrameSyncInternal(animationWrapperIntPtr, frameNumber);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void DrawOneFrameAsyncPrepareInternal(int frameNumber)
    {
        sprite.DrawOneFrameAsyncPrepareInternal(animationWrapperIntPtr, frameNumber);
    }
    
    public void Resize(uint maxSide)
    {
        if(this.maxSide == maxSide) return;
        this.maxSide = (int)maxSide;
        
        if (canPack)
        {
            ToPackIsDirty = true;
        }
        else
        {
            Spritee = new Sprite(this);
        }
    }
    
    public void AllowToRender()
    {
        if(hasRendererData) return;
        hasRendererData = true;

        if (canPack)
        {
            toPack.Add(this);
            ToPackIsDirty = true;
        }
        else
        {
            Spritee = new Sprite(this);
        }
    }

    public void DisallowToRender()
    {
        if(!hasRendererData) return;
        hasRendererData = false;
        
        sprite.Destroy();
        sprite = null;
        
        if (canPack)
        {
            toPack.Remove(this);
            ToPackIsDirty = true;
        }
    }
    
    public void Destroy()
    {
        DisallowToRender();
        NativeBridge.Dispose(animationWrapper);
    }
}
