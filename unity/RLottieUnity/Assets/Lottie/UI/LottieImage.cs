using System.Reflection;
using LSCore;
using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
public sealed class LottieImage : LSRawImage
{
    public LottieImageManager manager = new();
    
    public static LottieImage Create(BaseLottieAsset asset, RectTransform transform, float animationSpeed = 1f, bool loop = true)
    {
        var image = new GameObject(nameof(LottieImage)).AddComponent<LottieImage>();
        var manager = new LottieImageManager();
        manager.renderer = image;
        image.manager = manager;
        manager.Asset = asset;
        transform.SetParent(transform, false);
        manager.Speed = animationSpeed;
        manager.Loop = loop;
        return image;
    }
    
    public bool IsVisible => !canvasRenderer.cull && IsActive();
    
    internal uint Size
    {
        get
        {
            var size = MeshRect.size;
            var min = (int)(Mathf.Max(size.x, size.y) * 0.5f);
            min = Mathf.ClosestPowerOfTwo(min);
            var newSize = (uint)Mathf.Clamp(min, 64, 2048);
            return newSize;
        }
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        manager.renderer = this;
    }
#endif
    
    protected override void Awake()
    {
        manager.renderer = this;
#if UNITY_EDITOR
        LottieUpdater.RefreshUpdatingState();
#endif
        base.Awake();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        manager.UpdatePlayState();
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        manager.UpdatePlayState();
    }

    public override void OnCullingChanged()
    {
        base.OnCullingChanged();
        manager.UpdatePlayState();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        manager.DestroyLottie();
#if UNITY_EDITOR
        LottieUpdater.RefreshUpdatingState();
#endif
    }

    protected override void OnRectTransformDimensionsChange()
    {
        base.OnRectTransformDimensionsChange();
        manager.renderer = this;
        manager.ResizeIfNeeded();
    }

    private Lottie.Sprite sprite;

    public Lottie.Sprite Sprite
    {
        get => sprite;
        set
        {
            if(sprite == value) return;
            if(sprite != null) sprite.TextureChanged -= OnTextureChanged;
            sprite = value;
            sprite.TextureChanged += OnTextureChanged;
            SetVerticesDirty();
        }
    }
    
    internal void OnSpriteChanged(Lottie.Sprite sprite)
    {
        Sprite = sprite;
    }

    protected override float Aspect
    {
        get
        {
            if (manager.asset != null)
            {
                var size = Lottie.GetSize(manager.asset.Json);
                return (float)size.x / size.y;;
            }
            
            return base.Aspect;
        }
    }

    protected override void OnMeshFilled(Mesh mesh)
    {
        if (sprite != null)
        {
            var v = mesh.uv;
            v[0] = sprite.UvMin;
            v[1] = new Vector2(sprite.UvMin.x, sprite.UvMax.y);
            v[2] = sprite.UvMax;
            v[3] = new Vector2(sprite.UvMax.x, sprite.UvMin.y);
            mesh.uv = v;
        }
    }

    private static readonly FieldInfo textureField = typeof(RawImage).GetField("m_Texture", BindingFlags.NonPublic | BindingFlags.Instance);
    private void OnTextureChanged()
    {
        textureField.SetValue(this, sprite.Texture);
        canvasRenderer.SetTexture(textureField.GetValue(this) as Texture2D);
    }
}
