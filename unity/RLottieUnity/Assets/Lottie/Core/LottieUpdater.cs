using System;
using System.Collections.Generic;
using System.Linq;
using LSCore;

#if !UNITY_EDITOR
using UnityEngine;
#endif

#if UNITY_EDITOR
using UnityEditor;
#endif

#if UNITY_EDITOR
[InitializeOnLoad]
#endif
internal static class LottieUpdater
{
    public static List<BaseLottieManager> managers = new(64);
    public static Action[] PreRendering = new Action[4];
    public static Action[] CanvasPreRendering = new Action[4];
    public static event Action TextureApplyTime;
    
#if UNITY_EDITOR
    static LottieUpdater()
    {
        Init();
    }
#endif
    
#if !UNITY_EDITOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
#endif
    private static void Init()
    {
#if UNITY_EDITOR
        Selection.selectionChanged += RefreshUpdatingState;
        EditorApplication.update += OnEditorUpdate;

        void OnEditorUpdate()
        {
            EditorApplication.update -= OnEditorUpdate;
            RefreshUpdatingState();
        }
#endif
        World.PreRendering += OnPreRendering;
        World.CanvasPreRendering += OnCanvasPreRendering;
        World.Updated += OnUpdate;
        PreRendering[2] += OnTextureApplyTime;
        CanvasPreRendering[2] += OnTextureApplyTime;
    }
    
#if UNITY_EDITOR
    internal static void RefreshUpdatingState()
    {
        EditorWorld.Updated -= OnUpdate;
        if (Selection.gameObjects.Any(go => go && (
                go.GetComponent<LottieRenderer>() || go.GetComponent<LottieImage>())))
        {
            EditorWorld.Updated += OnUpdate;
        }
    }

    internal static void ForceApplyTexture()
    {
        TextureApplyTime?.Invoke();
    }
#endif

    private static void OnTextureApplyTime()
    {
        TextureApplyTime?.Invoke();
    }
    
    private static void OnPreRendering()
    {
        for (int i = 0; i < PreRendering.Length; i++)
        {
            PreRendering[i]?.Invoke();
        }
    }

    private static void OnCanvasPreRendering()
    {
        for (int i = 0; i < CanvasPreRendering.Length; i++)
        {
            CanvasPreRendering[i]?.Invoke();
        }
    }

    private static void OnUpdate()
    {
        Update();
    }

    private static void Update()
    {
        for (int i = 0; i < managers.Count; i++)
        {
            managers[i].PreUpdate();
        }
        TextureApplyTime?.Invoke();
        for (int i = 0; i < managers.Count; i++)
        {
            managers[i].Update();
        }
    }
}