using System;
using LSCore;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
[Serializable]
public abstract class BaseLottieManager
#if UNITY_EDITOR
    : ISerializationCallbackReceiver
#endif
{
    public Texture Texture
    {
        get
        {
            var t = lottie?.Spritee?.Texture;
            return t;
        }
    }

    private bool isAssetDirty;
    public BaseLottieAsset asset;
    public BaseLottieAsset Asset
    {
        get => asset;
        set
        {
            if (value == asset) return;
            asset = value;
            
            if (!isAssetDirty)
            {
                isAssetDirty = true;
                QueuePreRenderCall(ForceUpdateAsset, 0);
            }
        }
    }
    
    [SerializeField] private bool shouldPlay = true;
    public bool ShouldPlay
    {
        get => shouldPlay;
        set
        {
            if (value == shouldPlay) return;
            shouldPlay = value;
            UpdatePlayState();
        }
    }
    
    [SerializeField] private bool loop = true;
    public bool Loop
    {
        get => loop;
        set
        {
            if(loop == value) return;
            if(lottie != null) lottie.loop = value;
            loop = value;
            if (value)
            {
                UpdatePlayState();
            }
        }
    }

    [SerializeField] private float speed = 1f;
    public float Speed
    {
        get => speed;
        set
        {
            if(Mathf.Abs(speed - value) < 0.01f) return;
            speed = Mathf.Max(0f, value);
            UpdatePlayState();
        }
    }
    
    internal Lottie lottie;
    
    public abstract void SetupByAsset();
    protected abstract void OnSpriteChanged(Lottie.Sprite sprite);
    
    public bool IsEnded =>
        !loop
        && lottie != null
        && lottie.currentFrame >= lottie.TotalFramesCount - 1;
    
    internal bool IsPlaying { get; set; }
    public abstract bool IsVisible { get; }
    
    private uint lastSize;
    
    public uint Size
    {
        get
        {
            var newSize = Size_Internal;
            lastSize = newSize;
            return newSize;
        }
    }

    protected abstract uint Size_Internal { get; }
    
    private bool isUpdatePlayStateQueued;
    
    internal void UpdatePlayState()
    {
        if (!isUpdatePlayStateQueued)
        {
            isUpdatePlayStateQueued = true;
            QueuePreRenderCall(ForceUpdatePlayState, 0);
        }
    }
    
    protected abstract void QueuePreRenderCall(Action action, int order);
    protected abstract void DequeuePreRenderCall(Action action, int order);

    private void ForceUpdateAsset()
    {
        if (isAssetDirty)
        {
            isAssetDirty = false;
            DequeuePreRenderCall(ForceUpdateAsset, 0);
        }
        
        DestroyLottie();
        ForceUpdatePlayState();
    }
    
    protected void ForceUpdatePlayState()
    {
        if (isUpdatePlayStateQueued)
        {
            isUpdatePlayStateQueued = false;
            DequeuePreRenderCall(ForceUpdatePlayState, 0);
        }
        
        var lastIsPlaying = IsPlaying;
        var isVisible = IsVisible;
        IsPlaying = isVisible && shouldPlay && asset != null && !IsEnded && speed > 0;
        
        if (!isVisible && lottie != null)
        { 
            lottie.DisallowToRender();
        }
        
        if (IsPlaying == lastIsPlaying) return;
        
        if (IsPlaying)
        {
            if (lottie == null)
            {
                CreateLottie();
                QueuePreRenderCall(Draw, 1);

                void Draw()
                {
                    DequeuePreRenderCall(Draw, 1);
                    lottie.DrawOneFrame(0);
                    QueuePreRenderCall(UpdateAsync, 3);

                    void UpdateAsync()
                    {
                        DequeuePreRenderCall(UpdateAsync, 3);
                        lottie.UpdateDeltaAsync(speed * Time.deltaTime);
                    }
                }
            }
            else
            {
                lottie.AllowToRender();
            }
            
            LottieUpdater.managers.Add(this);
        }
        else
        {
            LottieUpdater.managers.Remove(this);
        }
    }
    

    protected abstract void Tick();
    
    internal void ResizeIfNeeded()
    {
        if (lottie == null || !IsVisible) return;
        if (lastSize == Size) return;
        lottie.Resize(Size);
    }
    
    private void CreateLottie()
    {
        lottie = new Lottie(asset, string.Empty, Size, true);
        lottie.SpriteChanged += OnSpriteChanged;
        lottie.loop = Loop;
        SetupByAsset();
    }
    
    internal void DestroyLottie()
    {
        isAssetDirty = false;
        isUpdatePlayStateQueued = false;
        IsPlaying = false;
        DequeuePreRenderCall(ForceUpdatePlayState, 0);
        DequeuePreRenderCall(ForceUpdateAsset, 0);
        if (lottie == null) return;
        lottie.DisallowToRender();
        LottieUpdater.managers.Remove(this);
        lottie.Destroy();
        lottie = null;
    }

    internal void PreUpdate()
    {
        Tick();
        lottie.FetchTexture();
    }
    
    internal void Update()
    {
        lottie.UpdateDeltaAsync(speed * World.DeltaTime);
        if (IsEnded) UpdatePlayState();
    }
    
#if UNITY_EDITOR
    private BaseLottieAsset lastAsset;
    private bool lastShouldPlay;
    private bool lastLoop;
    private float lastSpeed;
    
    public void OnBeforeSerialize()
    {
        lastAsset = asset;
        lastShouldPlay = shouldPlay;
        lastSpeed = speed;
        lastLoop = loop;
    }
    
    public void OnAfterDeserialize()
    {
        if (lottie == null)
        {
            IsPlaying = false;
            isUpdatePlayStateQueued = false;
            DequeuePreRenderCall(ForceUpdatePlayState, 0);
        }
        
        if (lastAsset != asset)
        {
            var l = lastAsset;
            lastAsset = asset;
            asset = asset == null ? l : null;
            Asset = lastAsset;
        }

        if (lastShouldPlay != shouldPlay)
        {
            lastShouldPlay = shouldPlay;
            shouldPlay = !shouldPlay;
            ShouldPlay = lastShouldPlay;
        }

        if (lastLoop != loop)
        {
            lastLoop = loop;
            loop = !loop;
            Loop = lastLoop;
        }

        if (!Mathf.Approximately(lastSpeed, speed))
        {
            lastSpeed = speed;
            speed = -1;
            Speed = lastSpeed;
        }
    }
#endif
}

[Serializable]
public class LottieImageManager : BaseLottieManager
{
    internal LottieImage renderer;

    public override bool IsVisible => renderer.IsVisible;
    protected override uint Size_Internal => renderer.Size;
    protected override void QueuePreRenderCall(Action action, int order) => LottieUpdater.CanvasPreRendering[order] += action;
    protected override void DequeuePreRenderCall(Action action, int order) => LottieUpdater.CanvasPreRendering[order] -= action;

    protected override void Tick() { }
    public override void SetupByAsset() => asset.SetupImage(renderer);
    protected override void OnSpriteChanged(Lottie.Sprite sprite) => renderer.OnSpriteChanged(sprite);
}

[Serializable]
public class LottieRendererManager : BaseLottieManager
{
    internal LottieRenderer renderer;
    
    public override bool IsVisible => renderer.IsVisible;
    protected override uint Size_Internal => renderer.Size;
    protected override void QueuePreRenderCall(Action action, int order) => LottieUpdater.PreRendering[order] += action;
    protected override void DequeuePreRenderCall(Action action, int order) => LottieUpdater.PreRendering[order] -= action;

    protected override void Tick() => renderer.TryRefresh();
    public override void SetupByAsset() => asset.SetupRenderer(renderer);
    protected override void OnSpriteChanged(Lottie.Sprite sprite) => renderer.OnSpriteChanged(sprite);
}


#if UNITY_EDITOR
[CustomPropertyDrawer(typeof(BaseLottieManager), true)]
public class BaseLottieManagerDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        var iter = property.Copy();
        var end  = iter.GetEndProperty();

        var y = position.y;
        bool enterChildren = true;

        while (iter.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iter, end))
        {
            float h = EditorGUI.GetPropertyHeight(iter, true);
            var r = new Rect(position.x, y, position.width, h);
            EditorGUI.PropertyField(r, iter, true);
            y += h + EditorGUIUtility.standardVerticalSpacing;
            enterChildren = false;
        }
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        var iter = property.Copy();
        var end  = iter.GetEndProperty();

        float h = 0f;
        bool enterChildren = true;

        while (iter.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iter, end))
        {
            h += EditorGUI.GetPropertyHeight(iter, true) + EditorGUIUtility.standardVerticalSpacing;
            enterChildren = false;
        }
        
        return Mathf.Max(0, h - EditorGUIUtility.standardVerticalSpacing);
    }
}
#endif
