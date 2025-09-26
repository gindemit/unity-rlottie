using LSCore;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed partial class LottieRenderer : MonoBehaviour
{
    private const int MinSize = 64;
    private const int MaxSize = 2048;

    public LottieRendererManager manager = new();
    
    private bool isVisible;

    public bool IsVisible
    {
        get => enabled && gameObject.activeInHierarchy && isVisible;
        private set
        {
            if(isVisible == value) return;
            isVisible = value;
            manager.UpdatePlayState();
        }
    }

    internal uint Size
    {
        get
        {
            var s = transform.localScale;
            var maxAxis = Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y));
            var px = Mathf.RoundToInt(maxAxis * pixelsPerUnit);
            var clamped = Mathf.Clamp(px, MinSize, MaxSize);
            var newSize = (uint)Mathf.ClosestPowerOfTwo(clamped);
            return newSize;
        }
    }
    
    private new GameObject gameObject;

    private void Awake()
    {
        gameObject = base.gameObject;
        Init();
#if UNITY_EDITOR
        LottieUpdater.RefreshUpdatingState();
#endif
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if(World.IsBuilding) return;
        gameObject = base.gameObject;
        if (AssetDatabase.Contains(gameObject))
        {
            return;
        }
        MarkMeshAsDirty();
        MarkColorAsDirty();
        EditorApplication.update += Update;

        void Update()
        {
            EditorApplication.update -= Update;
            if(this) Init();
        }
    }
#endif
    
    private void Init()
    {
        manager.renderer = this;
        mr = GetComp<MeshRenderer>();
        mr.allowOcclusionWhenDynamic = false;
        mr.lightProbeUsage = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mf = GetComp<MeshFilter>();
        mpb ??= new MaterialPropertyBlock();

        if (quad == null) BuildUnitQuad();
        if (unlitMat == null)
        {
            unlitMat = new Material(Shader.Find("Sprites/Default"));
#if UNITY_EDITOR
            unlitMat.hideFlags = HideFlags.DontSave;
#endif
        }
        if (!Material) Material = unlitMat;
        if(mr.sharedMaterial == null) mr.sharedMaterial = Material;

        T GetComp<T>() where T : Component
        {
            T ret;
#if UNITY_EDITOR
            ret = GetComponent<T>();
            if (ret == null)
            {
                ret = gameObject.AddComponent<T>();
                ret.hideFlags = HideFlags.HideAndDontSave | HideFlags.HideInInspector;
            }
#else
            ret = gameObject.AddComponent<T>();
#endif
            return ret;
        }
    }

    private void OnEnable()
    {
#if UNITY_EDITOR
        if (mpb == null) Init();
#endif
        manager.UpdatePlayState();
    }
    

    private void OnDisable() => manager.UpdatePlayState();
    private void OnDestroy()
    {
        manager.DestroyLottie();
        LottieUpdater.PreRendering[0] += DestroyDeps;
        void DestroyDeps()
        {
            LottieUpdater.PreRendering[0] -= DestroyDeps;
            if (gameObject)
            {
#if UNITY_EDITOR
                if (World.IsEditMode)
                {
                    EditorApplication.update += OnUpdate;

                    void OnUpdate()
                    {
                        EditorApplication.update -= OnUpdate;
                        DestroyImmediate(mr);
                        DestroyImmediate(mf);
                    }
                }
                else
                {
                    Destroy(mr);
                    Destroy(mf);
                }
#else
                Destroy(mr);
                Destroy(mf);
#endif
            }
        }
#if UNITY_EDITOR
        LottieUpdater.RefreshUpdatingState();
#endif
    }

    private void OnBecameVisible() => IsVisible = true;
    private void OnBecameInvisible() => IsVisible = false;

    internal void TryRefresh()
    {
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
            MarkMeshAsDirty();
        }
    }

    private bool isMeshDirty;
    internal void MarkMeshAsDirty()
    {
        if(isColorDirty) return;
        isMeshDirty  = true;
        LottieUpdater.PreRendering[3] += Rebuild;
        void Rebuild()
        {
            isMeshDirty = false;
            LottieUpdater.PreRendering[3] -= Rebuild;
            if(!this) return;
            BuildUnitQuad();
        }
    }
    
    private bool isColorDirty;
    internal void MarkColorAsDirty()
    {
        if(isColorDirty) return;
        isColorDirty  = true;
        LottieUpdater.PreRendering[3] += Rebuild;
        void Rebuild()
        {
            isColorDirty = false;
            LottieUpdater.PreRendering[3] -= Rebuild;
            if(!this) return;
            UpdateColor();
        }
    }
    
    internal void OnSpriteChanged(Lottie.Sprite sprite)
    {
#if UNITY_EDITOR
        if (sprite.Texture == null) return;
#endif
        Sprite = sprite;
    }

    private void OnTextureChanged()
    {
        mr.GetPropertyBlock(mpb);
        mpb.SetTexture(mainTexId, sprite.Texture);
        mr.SetPropertyBlock(mpb);
    }
}
