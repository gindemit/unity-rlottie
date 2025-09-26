#if UNITY_EDITOR
using System;
using LSCore;
using LSCore.Extensions;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

[InitializeOnLoad]
[ExecuteInEditMode]
public class EditorWorld : MonoBehaviour
{
    private static float targetFps = -1;

    public static float TargetFps
    {
        get => targetFps;
        set
        {
            if (Mathf.Approximately(value, targetFps)) return;
            next = 0;
            targetFps = value;
            if (targetFps == 0)
            {
                interval = double.PositiveInfinity;
            }
            else if (targetFps > 0)
            { 
                interval = 1.0 / targetFps;
            }
        }
    }
    
    private static double interval;
    private static double next;
    
    private static EditorWorld instance;
    public static event Action Updated;
    
    static EditorWorld()
    {
        CompilationPipeline.compilationFinished += x =>
        {
            Destroy();
        };

        World.Built += () =>
        {
            Destroy();
            Create();
        };
        
        EditorApplication.update += Update;

        void Update()
        {
            EditorApplication.update -= Update;
            Create();
            World.Creating += Destroy;
            World.Destroyed += Create;
        }
    }

    private static double lastUpdateTime;
    
    private void Update()
    {
        if (targetFps >= 0)
        {
            var now = EditorApplication.timeSinceStartup;
            if (now < next) return;
            next = now + interval;
        }
        
        World.DeltaTime = (float)(EditorApplication.timeSinceStartup - lastUpdateTime);
        Updated.SafeInvoke();
        lastUpdateTime = EditorApplication.timeSinceStartup;;
    }
    
    private static void EditorUpdate()
    {
        if (Updated != null)
        {
            ForceUpdate();
        }
    }
    
    public static void ForceUpdate()
    {
        EditorApplication.QueuePlayerLoopUpdate();
    }
    
    private static void Create()
    {
        var go = new GameObject("EditorWorld");
        go.hideFlags = HideFlags.HideAndDontSave;
        instance = go.AddComponent<EditorWorld>();
        EditorApplication.update += EditorUpdate;
    }

    private static void Destroy()
    {
        if (instance != null)
        { 
            DestroyImmediate(instance.gameObject);
        }
        instance = null;
        EditorApplication.update -= EditorUpdate;
    }
}
#endif