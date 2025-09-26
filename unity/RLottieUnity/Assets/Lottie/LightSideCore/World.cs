using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Threading;
using LSCore.Extensions;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor.Build.Reporting;
using UnityEditor;
using UnityEditor.Build;
#endif

namespace LSCore
{
    [DefaultExecutionOrder(-999)]
    public class World : MonoBehaviour
    {
        public static event Action ApplicationPaused;
        public static event Action ApplicationResumed;
        public static event Action Updated;
        public static event Action FixedUpdated;
        public static event Action PreRendering;
        public static event Action CanvasPreRendering;
        
        private static readonly SynchronizationContext synchronizationContext = SynchronizationContext.Current;
        private static bool isCreated;
        private static World instance;
        public static float FrameRate => 1f / Time.unscaledDeltaTime;
        public static float DeltaTime { get; set; }

#if UNITY_EDITOR
        public class BuildHooks : BuildPlayerProcessor
        {
            public int callbackOrder => 0;

            public override void PrepareForBuild(BuildPlayerContext buildPlayerContext)
            {
                IsBuilding = true;
                Building.SafeInvoke();
                EditorApplication.update += OnUpdate;
            }

            private void OnUpdate()
            {
                EditorApplication.update -= OnUpdate;
                IsBuilding = false;
            }
        }
        
        public sealed class BuildDoneHook : IPostprocessBuildWithReport
        {
            public int callbackOrder => 0;
            
            public void OnPostprocessBuild(BuildReport report)
            {
                Built.SafeInvoke();
            }
        }
        
        public static event Action Creating;
        public static event Action Created;
        public static event Action Destroyed;
        public static event Action Building;
        public static event Action Built;
        public static int InstanceId => instance.GetInstanceID(); 
        public static bool IsPlaying { get; private set; }
        public static bool IsBuilding { get; private set; }
        public static bool IsCompiling => EditorApplication.isCompiling;
        public static bool IsEditMode => !IsPlaying;
        public static bool IsPlayModeDisabling { get; private set; }
#endif
        
        static World()
        {
            Camera.onPreRender += OnPreRendering;
            Canvas.preWillRenderCanvases += OnCanvasPreRendering;
#if UNITY_EDITOR
            EditorApplication.playModeStateChanged += change =>
            {
                if (change == PlayModeStateChange.ExitingPlayMode)
                {
                    IsPlayModeDisabling = true;
                }
                else if (change == PlayModeStateChange.ExitingEditMode)
                {
                    IsPlayModeDisabling = false;
                }
            };
#endif
        }

        private static int renderedFrame = -1;
        
        private static void OnPreRendering(Camera _)
        {
            var frame = Time.frameCount;
            if(renderedFrame == frame) return;
            renderedFrame = frame;
            PreRendering?.Invoke();
        }

        private static void OnCanvasPreRendering() => CanvasPreRendering?.Invoke();
        
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Init()
        {
            renderedFrame = -1;
            Application.targetFrameRate = 120;
#if UNITY_EDITOR
            Creating.SafeInvoke();
#endif
            var go = new GameObject(nameof(World));
            instance = go.AddComponent<World>();
            DontDestroyOnLoad(go);
#if UNITY_EDITOR
            IsPlaying = true;
            Created.SafeInvoke();
#endif
        }

        private void Update()
        {
            DeltaTime = Time.deltaTime;
            Updated?.Invoke();
            CallActions();
        }
        
        private void FixedUpdate()
        {
            FixedUpdated?.Invoke();
        }
        
        private void OnDestroy()
        {
#if UNITY_EDITOR
            IsPlaying = false;
            Destroyed.SafeInvoke();
#endif
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                ApplicationPaused.SafeInvoke();
            }
            else
            {
                ApplicationResumed.SafeInvoke();
            }
        }

        private void OnApplicationQuit()
        {
#if UNITY_EDITOR
            IsPlaying = false;
#endif
            OnApplicationPause(true);
        }
        
        
        private static readonly ConcurrentQueue<Action> executionQueue = new();
        
        public static void CallInMainThread(Action action)
        {
            executionQueue.Enqueue(action);
        }
        
        private static void CallActions()
        {
            while (executionQueue.Count > 0)
            {
                if (executionQueue.TryDequeue(out var action))
                {
                    action();
                }
            }
        }
#if UNITY_EDITOR
        public static bool IsDiff(ref int id)
        {
            bool diff = id != InstanceId;
            id = InstanceId;
            return diff;
        }
#endif

        public static Coroutine BeginCoroutine(IEnumerator routine)
        {
            if (instance == null) return null;
            return instance.StartCoroutine(routine);
        }
    }
}