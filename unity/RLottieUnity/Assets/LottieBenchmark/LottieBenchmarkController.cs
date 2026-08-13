using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using LottiePlugin;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

/// <summary>
/// Runtime rlottie benchmark UI. It intentionally uses IMGUI so the same scene can run
/// without additional UI dependencies in the Editor, standalone players, and device builds.
/// </summary>
public sealed class LottieBenchmarkController : MonoBehaviour
{
    private const string AndroidArgumentsExtra = "lottieBenchmarkArguments";
    private const double SixtyFpsBudgetMs = 1000.0 / 60.0;
    private const int MaxVisiblePreviews = 12;

    private static readonly string[] AnimationResourcePaths =
    {
        "LottieBenchmark/patch-pal-iteration-a-stitches",
        "LottieBenchmark/patch-pal-iteration-b-vector-felt",
        "LottieBenchmark/patch-pal-iteration-c-hybrid-felt"
    };

    private static readonly string[] AnimationDisplayNames =
    {
        "A - Stitches",
        "B - Vector felt",
        "C - Hybrid felt PNG"
    };

    private static readonly int[] DefaultMatrixResolutions = { 128, 256, 512, 1024 };
    private static readonly ProfilerMarker RenderBatchMarker = new ProfilerMarker("LottieBenchmark.RenderBatch");
    private static readonly ProfilerMarker RenderInstanceMarker = new ProfilerMarker("LottieBenchmark.RenderInstance");

    private readonly List<LottieAnimation> _instances = new List<LottieAnimation>();
    private readonly List<BenchmarkResult> _results = new List<BenchmarkResult>();
    private readonly Stopwatch _stopwatch = new Stopwatch();

    private int _selectedAnimation;
    private string _instanceCountText = "1";
    private string _widthText = "512";
    private string _heightText = "512";
    private string _warmupFramesText = "30";
    private string _sampleFramesText = "180";
    private bool _showPreviews = true;
    private bool _livePlayback;
    private bool _benchmarkRunning;
    private bool _cancelRequested;
    private bool _quitAfterAutomaticRun;
    private bool _validatePixels;
    private bool _validationFailed;
    private bool _holdAfterWarmup;
    private bool _useManagedUpload;
    private bool _panelOpen = true;
    private int[] _matrixResolutions = DefaultMatrixResolutions;
    private int _liveFrame;
    private double _liveBatchMs;
    private string _status = "Choose a configuration, then Spawn Live or Run Benchmark.";
    private string _lastCsvPath = string.Empty;
    private Vector2 _controlScroll;
    private Vector2 _resultsScroll;
    private GUIStyle _headingStyle;
    private GUIStyle _smallStyle;
    private GUIStyle _panelStyle;

    private void Start()
    {
        string[] arguments = GetBenchmarkArguments();
        if (!HasArgument(arguments, "-lottieBenchmarkMatrix"))
        {
            return;
        }

        bool vulkanDiagnostics = HasArgument(arguments, "-lottieBenchmarkVulkanDiagnostics");
        LottieAnimation.SetGlobalLogLevel(vulkanDiagnostics ? LottieLogLevel.Info : LottieLogLevel.None);

        _instanceCountText = GetArgument(arguments, "-lottieBenchmarkInstances", _instanceCountText);
        _warmupFramesText = GetArgument(arguments, "-lottieBenchmarkWarmup", _warmupFramesText);
        _sampleFramesText = GetArgument(arguments, "-lottieBenchmarkSamples", _sampleFramesText);
        _lastCsvPath = GetArgument(arguments, "-lottieBenchmarkOutput", string.Empty);
        _quitAfterAutomaticRun = HasArgument(arguments, "-lottieBenchmarkQuit");
        _validatePixels = HasArgument(arguments, "-lottieBenchmarkValidatePixels");
        _holdAfterWarmup = HasArgument(arguments, "-lottieBenchmarkHoldAfterWarmup");
        _useManagedUpload = HasArgument(arguments, "-lottieBenchmarkManagedUpload");
        string resolutions = GetArgument(arguments, "-lottieBenchmarkResolutions", string.Empty);
        if (!string.IsNullOrEmpty(resolutions))
        {
            _matrixResolutions = ParseResolutions(resolutions);
        }
        if (HasArgument(arguments, "-lottieBenchmarkUncapped"))
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
        }
        StartCoroutine(StartAutomaticMatrixNextFrame());
    }

    private static string[] GetBenchmarkArguments()
    {
        string[] arguments = Environment.GetCommandLineArgs();
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (AndroidJavaObject intent = activity.Call<AndroidJavaObject>("getIntent"))
            {
                string extra = intent.Call<string>("getStringExtra", AndroidArgumentsExtra);
                if (!string.IsNullOrEmpty(extra))
                {
                    string[] extraArguments = extra.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    var combined = new string[arguments.Length + extraArguments.Length];
                    Array.Copy(arguments, combined, arguments.Length);
                    Array.Copy(extraArguments, 0, combined, arguments.Length, extraArguments.Length);
                    return combined;
                }
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Could not read Android benchmark arguments: " + exception.Message);
        }
#endif
        return arguments;
    }

    private void Update()
    {
        if (!_livePlayback || _benchmarkRunning || _instances.Count == 0)
        {
            return;
        }

        int totalFrames = Mathf.Max(1, (int)_instances[0].TotalFramesCount);
        RenderFrameBatch(_liveFrame % totalFrames, out _liveBatchMs);
        _liveFrame = (_liveFrame + 1) % totalFrames;
    }

    private void OnDestroy()
    {
        DisposeInstances();
    }

    private void OnGUI()
    {
        EnsureStyles();
        float scale = GetUiScale();
        Matrix4x4 oldMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        Rect safeArea = Screen.safeArea;
        Rect logicalSafeArea = new Rect(
            safeArea.xMin / scale,
            (Screen.height - safeArea.yMax) / scale,
            safeArea.width / scale,
            safeArea.height / scale);
        const float margin = 12f;
        if (!_panelOpen)
        {
            // Draw the launcher in screen coordinates. Keeping the scaled IMGUI
            // matrix here can place this small control outside some mobile safe
            // areas even though the full logical panel remains visible.
            GUI.matrix = oldMatrix;
            float screenMargin = margin * scale;
            Rect openButtonArea = new Rect(
                safeArea.xMin + screenMargin,
                Screen.height - safeArea.yMax + screenMargin,
                Mathf.Min(190f * scale, safeArea.width - screenMargin * 2f),
                42f * scale);
            GUI.Box(openButtonArea, GUIContent.none, _panelStyle);
            Rect openButton = new Rect(
                openButtonArea.x + 4f,
                openButtonArea.y + 4f,
                openButtonArea.width - 8f,
                openButtonArea.height - 8f);
            if (GUI.Button(openButton, "Open performance lab"))
            {
                _panelOpen = true;
            }
            return;
        }

        float panelWidth = Mathf.Min(610f, logicalSafeArea.width - margin * 2f);
        Rect panelArea = new Rect(
            logicalSafeArea.xMin + margin,
            logicalSafeArea.yMin + margin,
            panelWidth,
            logicalSafeArea.height - margin * 2f);
        float sidePreviewX = panelArea.xMax + margin;
        float sidePreviewWidth = logicalSafeArea.xMax - sidePreviewX - margin;
        bool hasSidePreview = sidePreviewWidth >= 100f;

        GUILayout.BeginArea(panelArea, _panelStyle);
        _controlScroll = GUILayout.BeginScrollView(_controlScroll);
        GUILayout.BeginHorizontal();
        GUILayout.Label("rlottie performance lab", _headingStyle);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Close", GUILayout.Width(72f), GUILayout.Height(26f)))
        {
            _panelOpen = false;
        }
        GUILayout.EndHorizontal();
        GUILayout.Label("Deterministic render batches plus observed Unity frame timing. Runs unchanged in Editor, Windows, iOS, and other device players.", _smallStyle);

        GUILayout.Space(8f);
        GUILayout.Label("Animation");
        GUILayout.BeginHorizontal();
        for (int i = 0; i < AnimationDisplayNames.Length; i++)
        {
            GUI.enabled = !_benchmarkRunning;
            if (GUILayout.Toggle(_selectedAnimation == i, AnimationDisplayNames[i], GUI.skin.button))
            {
                _selectedAnimation = i;
            }
        }
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        GUILayout.Space(6f);
        GUILayout.BeginHorizontal();
        LabeledTextField("Instances", ref _instanceCountText, 96f);
        LabeledTextField("Width", ref _widthText, 82f);
        LabeledTextField("Height", ref _heightText, 82f);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Square presets", GUILayout.Width(96f));
        foreach (int resolution in _matrixResolutions)
        {
            GUI.enabled = !_benchmarkRunning;
            if (GUILayout.Button(resolution.ToString(CultureInfo.InvariantCulture)))
            {
                _widthText = resolution.ToString(CultureInfo.InvariantCulture);
                _heightText = _widthText;
            }
        }
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        LabeledTextField("Warm-up", ref _warmupFramesText, 96f);
        LabeledTextField("Samples", ref _sampleFramesText, 96f);
        _showPreviews = GUILayout.Toggle(_showPreviews, "Show previews (max 12)");
        GUILayout.EndHorizontal();

        GUILayout.Space(8f);
        GUILayout.BeginHorizontal();
        GUI.enabled = !_benchmarkRunning;
        if (GUILayout.Button("Spawn Live", GUILayout.Height(30f)))
        {
            SpawnLive();
        }
        if (GUILayout.Button("Run Selected", GUILayout.Height(30f)))
        {
            StartSelectedBenchmark();
        }
        if (GUILayout.Button("Run 3 x 4 Matrix", GUILayout.Height(30f)))
        {
            StartMatrixBenchmark();
        }
        GUI.enabled = true;
        if (GUILayout.Button(_benchmarkRunning ? "Cancel" : "Clear", GUILayout.Height(30f)))
        {
            StopAndClear();
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(6f);
        GUILayout.Label(_status, _smallStyle);
        if (_instances.Count > 0)
        {
            GUILayout.Label(string.Format(CultureInfo.InvariantCulture,
                "Active: {0} x {1}x{2} | live render batch {3:F3} ms",
                _instances.Count, _widthText, _heightText, _liveBatchMs), _smallStyle);
        }

        if (_showPreviews && _instances.Count > 0 && !hasSidePreview)
        {
            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            float previewSize = Mathf.Min(280f, panelArea.width - 16f);
            Rect inlinePreview = GUILayoutUtility.GetRect(
                previewSize,
                previewSize,
                GUILayout.Width(previewSize),
                GUILayout.Height(previewSize));
            DrawPreviews(inlinePreview);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        GUILayout.Space(10f);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Results", _headingStyle);
        GUILayout.FlexibleSpace();
        GUI.enabled = _results.Count > 0;
        if (GUILayout.Button("Export CSV", GUILayout.Width(100f)))
        {
            ExportCsv(true);
        }
        if (GUILayout.Button("Copy latest", GUILayout.Width(100f)))
        {
            GUIUtility.systemCopyBuffer = _results[_results.Count - 1].ToSummary();
        }
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        _resultsScroll = GUILayout.BeginScrollView(_resultsScroll, GUILayout.MinHeight(170f));
        if (_results.Count == 0)
        {
            GUILayout.Label("No completed benchmark yet.", _smallStyle);
        }
        else
        {
            for (int i = _results.Count - 1; i >= 0; i--)
            {
                GUILayout.TextArea(_results[i].ToSummary(), _smallStyle);
                GUILayout.Space(3f);
            }
        }
        GUILayout.EndScrollView();
        if (!string.IsNullOrEmpty(_lastCsvPath))
        {
            GUILayout.Label("CSV: " + _lastCsvPath, _smallStyle);
        }
        GUILayout.EndScrollView();
        GUILayout.EndArea();

        if (_showPreviews && _instances.Count > 0 && hasSidePreview)
        {
            DrawPreviews(new Rect(
                sidePreviewX,
                panelArea.y,
                sidePreviewWidth,
                panelArea.height));
        }

        GUI.matrix = oldMatrix;
    }

    private static float GetUiScale()
    {
        if (Application.isMobilePlatform)
        {
            // Target a 720-pixel short edge so controls remain touch-friendly
            // in both portrait and landscape, independent of device DPI.
            float shortEdge = Mathf.Min(Screen.width, Screen.height);
            return Mathf.Clamp(shortEdge / 720f, 1f, 3f);
        }

        return Mathf.Clamp(Screen.width / 1280f, 0.7f, 1.35f);
    }

    private void DrawPreviews(Rect area)
    {
        if (area.width < 100f || area.height < 100f)
        {
            return;
        }

        GUI.Box(area, GUIContent.none, _panelStyle);
        int visible = Mathf.Min(MaxVisiblePreviews, _instances.Count);
        int columns = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(visible * area.width / Mathf.Max(1f, area.height))));
        int rows = Mathf.CeilToInt(visible / (float)columns);
        float cellWidth = area.width / columns;
        float cellHeight = area.height / rows;
        float size = Mathf.Max(1f, Mathf.Min(cellWidth, cellHeight) - 8f);

        for (int i = 0; i < visible; i++)
        {
            int column = i % columns;
            int row = i / columns;
            Rect rect = new Rect(area.x + column * cellWidth + (cellWidth - size) * 0.5f,
                area.y + row * cellHeight + (cellHeight - size) * 0.5f, size, size);
            Texture texture = _instances[i].OutputTexture;
            if (texture != null)
            {
                // Match the UI RawImages in Main.unity, which present rlottie
                // output with a Y scale of -1.
                Rect textureRect = FitTextureInside(rect, texture);
                GUI.DrawTextureWithTexCoords(textureRect, texture, new Rect(0f, 1f, 1f, -1f), true);
            }
        }

        if (_instances.Count > visible)
        {
            GUI.Label(new Rect(area.x + 8f, area.yMax - 25f, area.width - 16f, 20f),
                string.Format(CultureInfo.InvariantCulture, "Showing {0} of {1}; all {1} are rendered.", visible, _instances.Count), _smallStyle);
        }
    }

    private static Rect FitTextureInside(Rect area, Texture texture)
    {
        float textureAspect = texture.width / (float)Mathf.Max(1, texture.height);
        float areaAspect = area.width / Mathf.Max(1f, area.height);
        if (textureAspect > areaAspect)
        {
            float height = area.width / textureAspect;
            return new Rect(area.x, area.y + (area.height - height) * 0.5f, area.width, height);
        }

        float width = area.height * textureAspect;
        return new Rect(area.x + (area.width - width) * 0.5f, area.y, width, area.height);
    }

    private static void LabeledTextField(string label, ref string value, float labelWidth)
    {
        GUILayout.Label(label, GUILayout.Width(labelWidth));
        value = GUILayout.TextField(value, GUILayout.Width(70f));
    }

    private void SpawnLive()
    {
        if (!TryReadConfiguration(out BenchmarkCase benchmarkCase, out string error))
        {
            _status = error;
            return;
        }

        if (CreateInstances(benchmarkCase, out double loadMs, out long memoryDelta))
        {
            _livePlayback = true;
            _liveFrame = 0;
            _status = string.Format(CultureInfo.InvariantCulture,
                "Spawned {0} instances in {1:F1} ms (reported allocated-memory delta {2}).",
                benchmarkCase.InstanceCount, loadMs, FormatBytes(memoryDelta));
        }
    }

    private void StartSelectedBenchmark()
    {
        if (!TryReadConfiguration(out BenchmarkCase benchmarkCase, out string error))
        {
            _status = error;
            return;
        }

        StartCoroutine(RunCases(new[] { benchmarkCase }));
    }

    private void StartMatrixBenchmark()
    {
        if (!TryReadPositive(_instanceCountText, "instance count", out int count, out string error) ||
            !TryReadPositive(_warmupFramesText, "warm-up frames", out int warmup, out error) ||
            !TryReadPositive(_sampleFramesText, "sample frames", out int samples, out error))
        {
            _status = error;
            return;
        }

        var cases = new List<BenchmarkCase>();
        for (int animation = 0; animation < AnimationDisplayNames.Length; animation++)
        {
            foreach (int resolution in _matrixResolutions)
            {
                cases.Add(new BenchmarkCase(animation, count, resolution, resolution, warmup, samples));
            }
        }
        StartCoroutine(RunCases(cases));
    }

    private IEnumerator StartAutomaticMatrixNextFrame()
    {
        yield return null;
        StartMatrixBenchmark();
    }

    private IEnumerator RunCases(IList<BenchmarkCase> cases)
    {
        _benchmarkRunning = true;
        _cancelRequested = false;
        _livePlayback = false;

        for (int caseIndex = 0; caseIndex < cases.Count && !_cancelRequested; caseIndex++)
        {
            BenchmarkCase benchmarkCase = cases[caseIndex];
            _selectedAnimation = benchmarkCase.AnimationIndex;
            _widthText = benchmarkCase.Width.ToString(CultureInfo.InvariantCulture);
            _heightText = benchmarkCase.Height.ToString(CultureInfo.InvariantCulture);
            _status = string.Format(CultureInfo.InvariantCulture, "Preparing case {0}/{1}: {2}, {3} x {4}x{5}",
                caseIndex + 1, cases.Count, AnimationDisplayNames[benchmarkCase.AnimationIndex],
                benchmarkCase.InstanceCount, benchmarkCase.Width, benchmarkCase.Height);

            if (!CreateInstances(benchmarkCase, out double loadMs, out long memoryDelta))
            {
                break;
            }

            yield return null;
            int totalFrames = Mathf.Max(1, (int)_instances[0].TotalFramesCount);
            for (int frame = 0; frame < benchmarkCase.WarmupFrames && !_cancelRequested; frame++)
            {
                RenderFrameBatch(frame % totalFrames, out _);
                _status = string.Format(CultureInfo.InvariantCulture, "Warm-up {0}/{1} - case {2}/{3}",
                    frame + 1, benchmarkCase.WarmupFrames, caseIndex + 1, cases.Count);
                yield return null;
            }

            if (_cancelRequested)
            {
                break;
            }

            if (_holdAfterWarmup)
            {
                if (_validatePixels)
                {
                    var heldValidation = new PixelValidation();
                    yield return ValidatePixels(_instances[0].OutputTexture,
                        _instances[0].TextureUploadBackend, heldValidation);
                    _validationFailed |= !heldValidation.IsValid;
                }
                _status = "Holding after warm-up without queueing more texture uploads.";
                while (!_cancelRequested)
                {
                    yield return null;
                }
                break;
            }

            GC.Collect();
            yield return null;
            var batchSamples = new double[benchmarkCase.SampleFrames];
            var observedFrameSamples = new double[benchmarkCase.SampleFrames];
            for (int sample = 0; sample < benchmarkCase.SampleFrames && !_cancelRequested; sample++)
            {
                RenderFrameBatch((sample + benchmarkCase.WarmupFrames) % totalFrames, out batchSamples[sample]);
                _status = string.Format(CultureInfo.InvariantCulture, "Sampling {0}/{1} - case {2}/{3}",
                    sample + 1, benchmarkCase.SampleFrames, caseIndex + 1, cases.Count);
                yield return null;
                observedFrameSamples[sample] = Time.unscaledDeltaTime * 1000.0;
            }

            if (_cancelRequested)
            {
                break;
            }

            var pixelValidation = new PixelValidation();
            if (_validatePixels)
            {
                yield return ValidatePixels(_instances[0].OutputTexture,
                    _instances[0].TextureUploadBackend, pixelValidation);
                _validationFailed |= !pixelValidation.IsValid;
            }

            BenchmarkResult result = BenchmarkResult.Create(benchmarkCase, loadMs, memoryDelta,
                batchSamples, observedFrameSamples, pixelValidation,
                _instances[0].TextureUploadBackend.ToString());
            _results.Add(result);
            _status = "Completed: " + result.ToSummary();
            Debug.Log("[LottieBenchmark] " + result.ToSummary());
            ExportCsv(false);
            yield return null;
        }

        _benchmarkRunning = false;
        DisposeInstances();
        _status = _cancelRequested ? "Benchmark cancelled." : "Benchmark run complete.";
        if (_quitAfterAutomaticRun)
        {
            ExportCsv(false);
            Application.Quit(_results.Count > 0 && !_cancelRequested && !_validationFailed ? 0 : 1);
        }
    }

    private static IEnumerator ValidatePixels(
        Texture texture,
        LottieTextureUploadBackend uploadBackend,
        PixelValidation validation)
    {
        if (texture == null)
        {
            validation.Error = "Output texture is null.";
            yield break;
        }
        if (!SystemInfo.supportsAsyncGPUReadback)
        {
            validation.Error = "Async GPU readback is not supported.";
            yield break;
        }

        // Let LateUpdate queue the native upload and allow two full presentation
        // frames before sampling. A plugin event issued in LateUpdate can be
        // submitted after commands recorded earlier in that frame.
        yield return new WaitForEndOfFrame();
        yield return null;
        yield return new WaitForEndOfFrame();
        const int readbackSize = 64;
        bool sampleThroughBlit = uploadBackend != LottieTextureUploadBackend.NativeVulkan;
        RenderTexture sampledTexture = null;
        AsyncGPUReadbackRequest request;
        if (sampleThroughBlit)
        {
            sampledTexture = RenderTexture.GetTemporary(
                readbackSize, readbackSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(texture, sampledTexture);
            request = AsyncGPUReadback.Request(sampledTexture, 0, TextureFormat.RGBA32);
        }
        else
        {
            // Graphics.Blit of Unity-owned BGRA textures returns the clear color
            // on affected Vulkan drivers even when normal shader sampling works.
            request = AsyncGPUReadback.Request(texture, 0, TextureFormat.RGBA32);
        }
        while (!request.done)
        {
            yield return null;
        }
        if (request.hasError)
        {
            if (sampledTexture != null)
            {
                RenderTexture.ReleaseTemporary(sampledTexture);
            }
            validation.Error = "Async GPU readback failed.";
            yield break;
        }

        var pixels = request.GetData<Color32>();
        ulong hash = 14695981039346656037UL;
        var colors = new HashSet<uint>();
        int sampleWidth = sampleThroughBlit ? sampledTexture.width : Mathf.Min(readbackSize, texture.width);
        int sampleHeight = sampleThroughBlit ? sampledTexture.height : Mathf.Min(readbackSize, texture.height);
        for (int sampleY = 0; sampleY < sampleHeight; sampleY++)
        {
            int sourceY = sampleThroughBlit || sampleHeight == 1
                ? sampleY
                : sampleY * (texture.height - 1) / (sampleHeight - 1);
            for (int sampleX = 0; sampleX < sampleWidth; sampleX++)
            {
                int sourceX = sampleThroughBlit || sampleWidth == 1
                    ? sampleX
                    : sampleX * (texture.width - 1) / (sampleWidth - 1);
                int sourceWidth = sampleThroughBlit ? sampleWidth : texture.width;
                Color32 pixel = pixels[sourceY * sourceWidth + sourceX];
                uint packed = (uint)(pixel.r | (pixel.g << 8) | (pixel.b << 16) | (pixel.a << 24));
                colors.Add(packed);
                hash = (hash ^ pixel.r) * 1099511628211UL;
                hash = (hash ^ pixel.g) * 1099511628211UL;
                hash = (hash ^ pixel.b) * 1099511628211UL;
                hash = (hash ^ pixel.a) * 1099511628211UL;
            }
        }
        if (sampledTexture != null)
        {
            RenderTexture.ReleaseTemporary(sampledTexture);
        }

        validation.Hash = hash.ToString("x16", CultureInfo.InvariantCulture);
        validation.DistinctSampledColors = colors.Count;
        validation.IsValid = colors.Count > 1;
        if (!validation.IsValid)
        {
            validation.Error = "GPU texture is flat-colored.";
        }
        Debug.Log(string.Format(CultureInfo.InvariantCulture,
            "[LottieBenchmark] GPU validation {0}x{1}: valid={2}, hash={3}, sampled_colors={4}, error={5}",
            texture.width, texture.height, validation.IsValid, validation.Hash,
            validation.DistinctSampledColors, validation.Error ?? string.Empty));
    }

    private void RenderFrameBatch(int frame, out double elapsedMs)
    {
        using (RenderBatchMarker.Auto())
        {
            _stopwatch.Restart();
            for (int i = 0; i < _instances.Count; i++)
            {
                using (RenderInstanceMarker.Auto())
                {
                    _instances[i].DrawOneFrame(frame);
                }
            }
            _stopwatch.Stop();
        }
        elapsedMs = _stopwatch.Elapsed.TotalMilliseconds;
    }

    private bool CreateInstances(BenchmarkCase benchmarkCase, out double loadMs, out long memoryDelta)
    {
        DisposeInstances();
        loadMs = 0.0;
        memoryDelta = 0;
        TextAsset animationJson = Resources.Load<TextAsset>(AnimationResourcePaths[benchmarkCase.AnimationIndex]);
        if (animationJson == null)
        {
            _status = "Missing Resources asset: " + AnimationResourcePaths[benchmarkCase.AnimationIndex];
            return false;
        }

        long memoryBefore = Profiler.GetTotalAllocatedMemoryLong();
        _stopwatch.Restart();
        try
        {
            var options = new LottieAnimationOptions
            {
                TargetFps = 60,
                ResolutionDivider = 1,
                PauseIfCulled = false,
                LogLevel = LottieLogLevel.None,
                UseManagedTextureUpload = _useManagedUpload
            };
            for (int i = 0; i < benchmarkCase.InstanceCount; i++)
            {
                _instances.Add(LottieAnimation.LoadFromJsonData(animationJson.text, string.Empty,
                    (uint)benchmarkCase.Width, (uint)benchmarkCase.Height, options));
            }
        }
        catch (Exception exception)
        {
            _stopwatch.Stop();
            DisposeInstances();
            _status = "Could not create benchmark instances: " + exception.Message;
            Debug.LogException(exception);
            return false;
        }
        _stopwatch.Stop();
        loadMs = _stopwatch.Elapsed.TotalMilliseconds;
        memoryDelta = Profiler.GetTotalAllocatedMemoryLong() - memoryBefore;
        return true;
    }

    private bool TryReadConfiguration(out BenchmarkCase benchmarkCase, out string error)
    {
        benchmarkCase = default;
        if (!TryReadPositive(_instanceCountText, "instance count", out int count, out error) ||
            !TryReadPositive(_widthText, "width", out int width, out error) ||
            !TryReadPositive(_heightText, "height", out int height, out error) ||
            !TryReadPositive(_warmupFramesText, "warm-up frames", out int warmup, out error) ||
            !TryReadPositive(_sampleFramesText, "sample frames", out int samples, out error))
        {
            return false;
        }

        benchmarkCase = new BenchmarkCase(_selectedAnimation, count, width, height, warmup, samples);
        return true;
    }

    private static bool TryReadPositive(string text, string label, out int value, out string error)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || value <= 0)
        {
            error = "Enter a positive integer for " + label + ".";
            return false;
        }
        error = string.Empty;
        return true;
    }

    private void StopAndClear()
    {
        _cancelRequested = true;
        StopAllCoroutines();
        _benchmarkRunning = false;
        _livePlayback = false;
        DisposeInstances();
        _status = "Stopped and disposed all benchmark instances.";
    }

    private void DisposeInstances()
    {
        for (int i = 0; i < _instances.Count; i++)
        {
            _instances[i]?.Dispose();
        }
        _instances.Clear();
        _liveBatchMs = 0.0;
    }

    private void ExportCsv(bool reportStatus)
    {
        if (_results.Count == 0)
        {
            return;
        }

        try
        {
            if (string.IsNullOrEmpty(_lastCsvPath))
            {
                string fileName = "rlottie-benchmark-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".csv";
                _lastCsvPath = Path.Combine(Application.persistentDataPath, fileName);
            }
            string directory = Path.GetDirectoryName(_lastCsvPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllText(_lastCsvPath, BenchmarkResult.ToCsv(_results), Encoding.UTF8);
            if (reportStatus)
            {
                _status = "Exported benchmark CSV to " + _lastCsvPath;
            }
        }
        catch (Exception exception)
        {
            _status = "CSV export failed: " + exception.Message;
            Debug.LogException(exception);
        }
    }

    private void EnsureStyles()
    {
        if (_headingStyle != null)
        {
            return;
        }
        _headingStyle = new GUIStyle(GUI.skin.label) { fontSize = 19, fontStyle = FontStyle.Bold };
        _smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
        _panelStyle = new GUIStyle(GUI.skin.box) { padding = new RectOffset(12, 12, 12, 12) };
    }

    private static string FormatBytes(long bytes)
    {
        double magnitude = Math.Abs((double)bytes);
        if (magnitude >= 1024.0 * 1024.0)
        {
            return (bytes / (1024.0 * 1024.0)).ToString("F2", CultureInfo.InvariantCulture) + " MiB";
        }
        return (bytes / 1024.0).ToString("F1", CultureInfo.InvariantCulture) + " KiB";
    }

    private static bool HasArgument(string[] arguments, string name)
    {
        for (int i = 0; i < arguments.Length; i++)
        {
            if (string.Equals(arguments[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static string GetArgument(string[] arguments, string name, string fallback)
    {
        for (int i = 0; i < arguments.Length - 1; i++)
        {
            if (string.Equals(arguments[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return arguments[i + 1];
            }
        }
        return fallback;
    }

    private static int[] ParseResolutions(string value)
    {
        string[] parts = value.Split(',');
        var resolutions = new List<int>(parts.Length);
        foreach (string part in parts)
        {
            if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out int resolution) ||
                resolution <= 0)
            {
                throw new ArgumentException("Invalid benchmark resolution list: " + value);
            }
            resolutions.Add(resolution);
        }
        return resolutions.ToArray();
    }

    private sealed class PixelValidation
    {
        public bool IsValid;
        public string Hash = string.Empty;
        public int DistinctSampledColors;
        public string Error = string.Empty;
    }

    private readonly struct BenchmarkCase
    {
        public readonly int AnimationIndex;
        public readonly int InstanceCount;
        public readonly int Width;
        public readonly int Height;
        public readonly int WarmupFrames;
        public readonly int SampleFrames;

        public BenchmarkCase(int animationIndex, int instanceCount, int width, int height, int warmupFrames, int sampleFrames)
        {
            AnimationIndex = animationIndex;
            InstanceCount = instanceCount;
            Width = width;
            Height = height;
            WarmupFrames = warmupFrames;
            SampleFrames = sampleFrames;
        }
    }

    private sealed class BenchmarkResult
    {
        public string TimestampUtc;
        public string Animation;
        public int InstanceCount;
        public int Width;
        public int Height;
        public int WarmupFrames;
        public int SampleFrames;
        public double LoadMs;
        public long MemoryDeltaBytes;
        public double MeanBatchMs;
        public double MedianBatchMs;
        public double P95BatchMs;
        public double MaxBatchMs;
        public double MeanPerAnimationMs;
        public double RendersPerSecond;
        public int BatchesOver60FpsBudget;
        public double MeanObservedFrameMs;
        public double P95ObservedFrameMs;
        public string Platform;
        public string Device;
        public string OperatingSystem;
        public string GraphicsDevice;
        public string GraphicsApi;
        public string UnityVersion;
        public bool PixelValidationEnabled;
        public bool PixelValid;
        public string PixelHash;
        public int DistinctSampledColors;
        public string PixelValidationError;
        public string UploadBackend;

        public static BenchmarkResult Create(BenchmarkCase benchmarkCase, double loadMs, long memoryDelta,
            double[] batchSamples, double[] observedFrameSamples, PixelValidation pixelValidation,
            string uploadBackend)
        {
            double meanBatch = Mean(batchSamples);
            double totalBatch = meanBatch * batchSamples.Length;
            int overBudget = 0;
            for (int i = 0; i < batchSamples.Length; i++)
            {
                if (batchSamples[i] > SixtyFpsBudgetMs)
                {
                    overBudget++;
                }
            }

            return new BenchmarkResult
            {
                TimestampUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                Animation = AnimationDisplayNames[benchmarkCase.AnimationIndex],
                InstanceCount = benchmarkCase.InstanceCount,
                Width = benchmarkCase.Width,
                Height = benchmarkCase.Height,
                WarmupFrames = benchmarkCase.WarmupFrames,
                SampleFrames = benchmarkCase.SampleFrames,
                LoadMs = loadMs,
                MemoryDeltaBytes = memoryDelta,
                MeanBatchMs = meanBatch,
                MedianBatchMs = Percentile(batchSamples, 0.50),
                P95BatchMs = Percentile(batchSamples, 0.95),
                MaxBatchMs = Percentile(batchSamples, 1.0),
                MeanPerAnimationMs = meanBatch / benchmarkCase.InstanceCount,
                RendersPerSecond = totalBatch <= 0.0 ? 0.0 :
                    benchmarkCase.InstanceCount * batchSamples.Length * 1000.0 / totalBatch,
                BatchesOver60FpsBudget = overBudget,
                MeanObservedFrameMs = Mean(observedFrameSamples),
                P95ObservedFrameMs = Percentile(observedFrameSamples, 0.95),
                Platform = Application.platform.ToString(),
                Device = SystemInfo.deviceModel,
                OperatingSystem = SystemInfo.operatingSystem,
                GraphicsDevice = SystemInfo.graphicsDeviceName,
                GraphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                UnityVersion = Application.unityVersion,
                PixelValidationEnabled = !string.IsNullOrEmpty(pixelValidation.Hash) || !string.IsNullOrEmpty(pixelValidation.Error),
                PixelValid = pixelValidation.IsValid,
                PixelHash = pixelValidation.Hash,
                DistinctSampledColors = pixelValidation.DistinctSampledColors,
                PixelValidationError = pixelValidation.Error,
                UploadBackend = uploadBackend
            };
        }

        public string ToSummary()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "{0} | {1} x {2}x{3} | batch mean/p50/p95/max {4:F3}/{5:F3}/{6:F3}/{7:F3} ms | " +
                "per animation {8:F3} ms | {9:F0} renders/s | >16.67 ms {10}/{11} | observed frame p95 {12:F2} ms | load {13:F1} ms | memory delta {14}",
                Animation, InstanceCount, Width, Height, MeanBatchMs, MedianBatchMs, P95BatchMs, MaxBatchMs,
                MeanPerAnimationMs, RendersPerSecond, BatchesOver60FpsBudget, SampleFrames,
                P95ObservedFrameMs, LoadMs, FormatBytes(MemoryDeltaBytes));
        }

        public static string ToCsv(IList<BenchmarkResult> results)
        {
            var builder = new StringBuilder();
            builder.AppendLine("timestamp_utc,animation,instances,width,height,warmup_frames,sample_frames,load_ms,memory_delta_bytes,mean_batch_ms,p50_batch_ms,p95_batch_ms,max_batch_ms,mean_per_animation_ms,renders_per_second,batches_over_16_67ms,mean_observed_frame_ms,p95_observed_frame_ms,platform,device,operating_system,graphics_device,graphics_api,unity_version,upload_backend,pixel_validation_enabled,pixel_valid,pixel_hash,distinct_sampled_colors,pixel_validation_error");
            foreach (BenchmarkResult result in results)
            {
                AppendCsvRow(builder, result);
            }
            return builder.ToString();
        }

        private static void AppendCsvRow(StringBuilder builder, BenchmarkResult result)
        {
            string[] values =
            {
                result.TimestampUtc, result.Animation,
                result.InstanceCount.ToString(CultureInfo.InvariantCulture),
                result.Width.ToString(CultureInfo.InvariantCulture), result.Height.ToString(CultureInfo.InvariantCulture),
                result.WarmupFrames.ToString(CultureInfo.InvariantCulture), result.SampleFrames.ToString(CultureInfo.InvariantCulture),
                result.LoadMs.ToString("F4", CultureInfo.InvariantCulture), result.MemoryDeltaBytes.ToString(CultureInfo.InvariantCulture),
                result.MeanBatchMs.ToString("F4", CultureInfo.InvariantCulture), result.MedianBatchMs.ToString("F4", CultureInfo.InvariantCulture),
                result.P95BatchMs.ToString("F4", CultureInfo.InvariantCulture), result.MaxBatchMs.ToString("F4", CultureInfo.InvariantCulture),
                result.MeanPerAnimationMs.ToString("F6", CultureInfo.InvariantCulture), result.RendersPerSecond.ToString("F2", CultureInfo.InvariantCulture),
                result.BatchesOver60FpsBudget.ToString(CultureInfo.InvariantCulture), result.MeanObservedFrameMs.ToString("F4", CultureInfo.InvariantCulture),
                result.P95ObservedFrameMs.ToString("F4", CultureInfo.InvariantCulture), result.Platform, result.Device,
                result.OperatingSystem, result.GraphicsDevice, result.GraphicsApi, result.UnityVersion, result.UploadBackend,
                result.PixelValidationEnabled.ToString(CultureInfo.InvariantCulture),
                result.PixelValid.ToString(CultureInfo.InvariantCulture), result.PixelHash,
                result.DistinctSampledColors.ToString(CultureInfo.InvariantCulture), result.PixelValidationError
            };
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) builder.Append(',');
                builder.Append('"').Append((values[i] ?? string.Empty).Replace("\"", "\"\"")).Append('"');
            }
            builder.AppendLine();
        }

        private static double Mean(double[] values)
        {
            if (values.Length == 0) return 0.0;
            double sum = 0.0;
            for (int i = 0; i < values.Length; i++) sum += values[i];
            return sum / values.Length;
        }

        private static double Percentile(double[] values, double percentile)
        {
            if (values.Length == 0) return 0.0;
            var sorted = (double[])values.Clone();
            Array.Sort(sorted);
            int index = Mathf.Clamp(Mathf.CeilToInt((float)(percentile * sorted.Length)) - 1, 0, sorted.Length - 1);
            return sorted[index];
        }
    }
}
