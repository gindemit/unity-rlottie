using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using LottiePlugin;
using LottiePlugin.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;

[DefaultExecutionOrder(10000)]
public sealed class LottieSmokeController : MonoBehaviour
{
    private const string ResultArgument = "-lottieSmokeResult";
    private const string QuitArgument = "-lottieSmokeQuit";
    private const string DefaultResultFileName = "lottie-smoke-result.json";
    private const string AndroidRequestExtra = "lottieSmoke";
    private const string FpsOnlyArgument = "-lottieFpsOnly";
    private const string AndroidFpsOnlyExtra = "lottieFpsOnly";
    private const float AnimationTimeoutSeconds = 5f;
    private const float ReadbackTimeoutSeconds = 5f;
    private const int MinimumChangedFrames = 3;

    [Serializable]
    private sealed class SmokeResult
    {
        public int schemaVersion = 1;
        public bool passed;
        public string platform;
        public string graphicsApi;
        public string graphicsDevice;
        public string animatedImageUploadBackend;
        public string animatedButtonUploadBackend;
        public string error;
        public List<SmokeCheck> checks = new List<SmokeCheck>();
    }

    [Serializable]
    private sealed class SmokeCheck
    {
        public string name;
        public bool passed;
        public string details;
    }

    private struct PixelSignature
    {
        public string Hash;
        public int VisiblePixels;
    }

    private sealed class PixelCapture
    {
        public bool Succeeded;
        public PixelSignature Signature;
        public string Error;
    }

    private sealed class PendingReadbackCleanup
    {
        public AsyncGPUReadbackRequest Request;
        public RenderTexture Texture;
    }

    private SmokeResult _result;
    private string _resultPath;
    private bool _quitWhenComplete;
    private bool _showFps;
    private float _fpsElapsed;
    private float _fpsLogElapsed;
    private float _fpsFrameTime;
    private int _fpsFrameCount;
    private float _displayFps;
    private float _displayFrameMilliseconds;
    private GUIStyle _fpsBoxStyle;
    private GUIStyle _fpsLabelStyle;
    private static readonly List<PendingReadbackCleanup> sPendingReadbackCleanup =
        new List<PendingReadbackCleanup>();

    private void Update()
    {
        if (_showFps)
        {
            float deltaTime = Time.unscaledDeltaTime;
            _fpsElapsed += deltaTime;
            _fpsLogElapsed += deltaTime;
            _fpsFrameTime += deltaTime;
            _fpsFrameCount++;
            if (_fpsElapsed >= 0.5f)
            {
                _displayFps = _fpsFrameCount / _fpsFrameTime;
                _displayFrameMilliseconds = 1000f * _fpsFrameTime / _fpsFrameCount;
                _fpsElapsed = 0f;
                _fpsFrameTime = 0f;
                _fpsFrameCount = 0;
            }
            if (_fpsLogElapsed >= 2f)
            {
                Debug.Log(string.Format(CultureInfo.InvariantCulture,
                    "[LottieSmoke] FPS {0:F1}, frame time {1:F1} ms",
                    _displayFps,
                    _displayFrameMilliseconds));
                _fpsLogElapsed = 0f;
            }
        }

        for (int i = sPendingReadbackCleanup.Count - 1; i >= 0; i--)
        {
            PendingReadbackCleanup pending = sPendingReadbackCleanup[i];
            bool done;
            try
            {
                done = pending.Request.done;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[LottieSmoke] Pending readback cleanup failed: " + exception.Message);
                done = true;
            }
            if (!done)
            {
                continue;
            }
            if (pending.Texture != null)
            {
                Destroy(pending.Texture);
            }
            sPendingReadbackCleanup.RemoveAt(i);
        }
    }

    private void OnGUI()
    {
        if (!_showFps)
        {
            return;
        }

        int fontSize = Mathf.Clamp(Screen.height / 48, 22, 48);
        if (_fpsBoxStyle == null || _fpsBoxStyle.fontSize != fontSize)
        {
            _fpsBoxStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = fontSize,
                alignment = TextAnchor.MiddleCenter
            };
            _fpsLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            _fpsLabelStyle.normal.textColor = Color.white;
        }

        float width = Mathf.Clamp(Screen.width * 0.3f, 260f, 520f);
        float height = fontSize * 2.2f;
        var rect = new Rect(Screen.width - width - 16f, 16f, width, height);
        GUI.Box(rect, GUIContent.none, _fpsBoxStyle);
        GUI.Label(rect,
            string.Format(CultureInfo.InvariantCulture, "FPS {0:F1}  |  {1:F1} ms", _displayFps, _displayFrameMilliseconds),
            _fpsLabelStyle);
    }

    private IEnumerator Start()
    {
        string[] arguments = Environment.GetCommandLineArgs();
        if (HasArgument(arguments, "-lottieBenchmarkMatrix"))
        {
            yield break;
        }

        bool fpsOnly = HasArgument(arguments, FpsOnlyArgument) || IsAndroidFpsOnlyRequested();
        if (fpsOnly)
        {
            _showFps = true;
            yield break;
        }

        _resultPath = GetArgument(arguments, ResultArgument, string.Empty);
        if (string.IsNullOrEmpty(_resultPath) && Application.platform == RuntimePlatform.Android)
        {
            if (!IsAndroidSmokeRequested())
            {
                yield break;
            }
            _resultPath = Path.Combine(Application.persistentDataPath, DefaultResultFileName);
        }
        if (string.IsNullOrEmpty(_resultPath))
        {
            yield break;
        }

        _showFps = true;
        _quitWhenComplete = HasArgument(arguments, QuitArgument);
        _result = new SmokeResult
        {
            platform = Application.platform.ToString(),
            graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
            graphicsDevice = SystemInfo.graphicsDeviceName
        };

        AnimatedButton animatedButton = FindObjectOfType<AnimatedButton>();
        AnimatedImage animatedImage = FindObjectOfType<AnimatedImage>();
        Record("animatedButtonFound", animatedButton != null, animatedButton == null ? "Missing AnimatedButton." : animatedButton.name);
        Record("animatedImageFound", animatedImage != null, animatedImage == null ? "Missing AnimatedImage." : animatedImage.name);
        if (animatedButton == null || animatedImage == null)
        {
            Complete("The smoke scene is missing a required component.");
            yield break;
        }

        LottieAnimation buttonAnimation = animatedButton.Animation;
        LottieAnimation imageAnimation = animatedImage.Animation;
        Record("animatedButtonLoaded", buttonAnimation != null, DescribeAnimation(buttonAnimation));
        Record("animatedImageLoaded", imageAnimation != null, DescribeAnimation(imageAnimation));
        if (buttonAnimation == null || imageAnimation == null)
        {
            Complete("A scene animation did not load during component startup.");
            yield break;
        }

        _result.animatedButtonUploadBackend = buttonAnimation.TextureUploadBackend.ToString();
        _result.animatedImageUploadBackend = imageAnimation.TextureUploadBackend.ToString();
        Record("animatedButtonStartsAtFirstFrame", buttonAnimation.CurrentFrame == 0,
            "currentFrame=" + buttonAnimation.CurrentFrame.ToString(CultureInfo.InvariantCulture));
        Record("animatedImageStartsAtFirstFrame", imageAnimation.CurrentFrame == 0,
            "currentFrame=" + imageAnimation.CurrentFrame.ToString(CultureInfo.InvariantCulture));
        Record("animatedImagePlayOnAwake", imageAnimation.IsPlaying,
            "isPlaying=" + imageAnimation.IsPlaying.ToString(CultureInfo.InvariantCulture));

        yield return new WaitForEndOfFrame();

        Debug.Log("[LottieSmoke] Capturing initial textures.");
        var buttonInitialCapture = new PixelCapture();
        yield return CaptureTexture(buttonAnimation.OutputTexture, buttonAnimation.TextureUploadBackend, buttonInitialCapture);
        var imageInitialCapture = new PixelCapture();
        yield return CaptureTexture(imageAnimation.OutputTexture, imageAnimation.TextureUploadBackend, imageInitialCapture);
        if (!buttonInitialCapture.Succeeded || !imageInitialCapture.Succeeded)
        {
            Record("initialPixelsReadable", false, buttonInitialCapture.Error ?? imageInitialCapture.Error);
            Complete("Could not read the initial rendered textures.");
            yield break;
        }
        PixelSignature buttonInitial = buttonInitialCapture.Signature;
        PixelSignature imageInitial = imageInitialCapture.Signature;

        Record("animatedButtonFirstFrameVisible", buttonInitial.VisiblePixels > 4,
            DescribeSignature(buttonInitial));
        Record("animatedImageFirstFrameVisible", imageInitial.VisiblePixels > 4,
            DescribeSignature(imageInitial));

        int imageStartFrame = imageAnimation.CurrentFrame;
        float imageDeadline = Time.realtimeSinceStartup + AnimationTimeoutSeconds;
        while (FrameDistance(imageStartFrame, imageAnimation.CurrentFrame, imageAnimation.TotalFramesCount) < MinimumChangedFrames &&
               Time.realtimeSinceStartup < imageDeadline)
        {
            yield return new WaitForEndOfFrame();
        }

        PixelSignature imageLater = default;
        string imageLaterError = null;
        bool imageCaptured = false;
        float imagePixelDeadline = Time.realtimeSinceStartup + AnimationTimeoutSeconds;
        do
        {
            var capture = new PixelCapture();
            yield return CaptureTexture(imageAnimation.OutputTexture, imageAnimation.TextureUploadBackend, capture);
            imageCaptured = capture.Succeeded;
            imageLater = capture.Signature;
            imageLaterError = capture.Error;
            if (imageCaptured && imageInitial.Hash != imageLater.Hash)
            {
                break;
            }

            yield return new WaitForEndOfFrame();
        }
        while (Time.realtimeSinceStartup < imagePixelDeadline);

        Record("animatedImageAdvances", imageAnimation.CurrentFrame != imageStartFrame,
            "from=" + imageStartFrame.ToString(CultureInfo.InvariantCulture) +
            ", to=" + imageAnimation.CurrentFrame.ToString(CultureInfo.InvariantCulture));
        Record("animatedImagePixelsChange", imageCaptured && imageInitial.Hash != imageLater.Hash,
            imageCaptured ? DescribeTransition(imageInitial, imageLater) : imageLaterError);

        int buttonStartFrame = buttonAnimation.CurrentFrame;
        bool clickReceived = false;
        animatedButton.OnClick.AddListener((_, __) => clickReceived = true);
        animatedButton.OnPointerClick(new PointerEventData(EventSystem.current)
        {
            button = PointerEventData.InputButton.Left
        });

        float buttonDeadline = Time.realtimeSinceStartup + AnimationTimeoutSeconds;
        while (FrameDistance(buttonStartFrame, buttonAnimation.CurrentFrame, buttonAnimation.TotalFramesCount) < MinimumChangedFrames &&
               Time.realtimeSinceStartup < buttonDeadline)
        {
            yield return new WaitForEndOfFrame();
        }

        Debug.Log("[LottieSmoke] Capturing post-click button texture.");
        var buttonLaterCapture = new PixelCapture();
        yield return CaptureTexture(buttonAnimation.OutputTexture, buttonAnimation.TextureUploadBackend, buttonLaterCapture);
        PixelSignature buttonLater = buttonLaterCapture.Signature;
        Record("animatedButtonAcceptsPress", clickReceived, "onClickInvoked=" + clickReceived.ToString(CultureInfo.InvariantCulture));
        Record("animatedButtonAdvancesAfterPress", buttonAnimation.CurrentFrame != buttonStartFrame,
            "from=" + buttonStartFrame.ToString(CultureInfo.InvariantCulture) +
            ", to=" + buttonAnimation.CurrentFrame.ToString(CultureInfo.InvariantCulture));
        Record("animatedButtonPixelsChangeAfterPress", buttonLaterCapture.Succeeded && buttonInitial.Hash != buttonLater.Hash,
            buttonLaterCapture.Succeeded ? DescribeTransition(buttonInitial, buttonLater) : buttonLaterCapture.Error);

        Debug.Log("[LottieSmoke] Smoke checks complete; writing result.");
        Complete(null);
    }

    private void Record(string name, bool passed, string details)
    {
        _result.checks.Add(new SmokeCheck
        {
            name = name,
            passed = passed,
            details = details ?? string.Empty
        });
    }

    private void Complete(string error)
    {
        _result.error = error ?? string.Empty;
        _result.passed = string.IsNullOrEmpty(error);
        for (int i = 0; i < _result.checks.Count; i++)
        {
            if (!_result.checks[i].passed)
            {
                _result.passed = false;
                break;
            }
        }

        try
        {
            WriteResultAtomically(_resultPath, JsonUtility.ToJson(_result, true));
        }
        catch (Exception exception)
        {
            _result.passed = false;
            Debug.LogException(exception);
        }

        if (_quitWhenComplete)
        {
            Application.Quit(_result.passed ? 0 : 1);
        }
    }

    private static bool TryCapture(Texture source, out PixelSignature signature, out string error)
    {
        signature = default;
        error = null;
        if (source == null)
        {
            error = "OutputTexture is null.";
            return false;
        }

        LottieAnimation owner = null;
        AnimatedImage image = FindObjectOfType<AnimatedImage>();
        if (image != null && image.Animation != null && image.Animation.OutputTexture == source)
        {
            owner = image.Animation;
        }
        if (owner == null)
        {
            AnimatedButton button = FindObjectOfType<AnimatedButton>();
            if (button != null && button.Animation != null && button.Animation.OutputTexture == source)
            {
                owner = button.Animation;
            }
        }
        if (owner != null && owner.TextureUploadBackend == LottieTextureUploadBackend.ManagedTextureUpload &&
            source is Texture2D managedTexture)
        {
            return TryCaptureManagedTexture(managedTexture, out signature, out error);
        }

        RenderTexture temporary = null;
        Texture2D readable = null;
        RenderTexture previous = RenderTexture.active;
        try
        {
            temporary = RenderTexture.GetTemporary(64, 64, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(source, temporary);
            RenderTexture.active = temporary;
            readable = new Texture2D(64, 64, TextureFormat.RGBA32, false, true);
            readable.ReadPixels(new Rect(0, 0, 64, 64), 0, 0, false);
            readable.Apply(false, false);
            Color32[] pixels = readable.GetPixels32();
            ulong hash = 14695981039346656037UL;
            int visiblePixels = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 pixel = pixels[i];
                if (pixel.a > 8)
                {
                    visiblePixels++;
                }
                hash = HashByte(hash, pixel.r);
                hash = HashByte(hash, pixel.g);
                hash = HashByte(hash, pixel.b);
                hash = HashByte(hash, pixel.a);
            }
            signature = new PixelSignature
            {
                Hash = hash.ToString("x16", CultureInfo.InvariantCulture),
                VisiblePixels = visiblePixels
            };
            return true;
        }
        catch (Exception exception)
        {
            error = exception.GetType().Name + ": " + exception.Message;
            return false;
        }
        finally
        {
            RenderTexture.active = previous;
            if (readable != null)
            {
                Destroy(readable);
            }
            if (temporary != null)
            {
                RenderTexture.ReleaseTemporary(temporary);
            }
        }
    }

    private static IEnumerator CaptureTexture(
        Texture source,
        LottieTextureUploadBackend backend,
        PixelCapture capture)
    {
        bool nativeWritten = backend == LottieTextureUploadBackend.NativeVulkan ||
            backend == LottieTextureUploadBackend.NativeExternalTexture;
        if (nativeWritten && SystemInfo.supportsAsyncGPUReadback)
        {
            yield return CaptureTextureAsync(source, capture);
            yield break;
        }

        capture.Succeeded = TryCapture(source, out capture.Signature, out capture.Error);
    }

    private static IEnumerator CaptureTextureAsync(
        Texture source,
        PixelCapture capture)
    {
        if (source == null)
        {
            capture.Error = "OutputTexture is null.";
            yield break;
        }

        const int readbackSize = 64;
        if (!TryBeginReadback(source, readbackSize, out RenderTexture intermediate,
                out AsyncGPUReadbackRequest request, out string beginError))
        {
            capture.Error = beginError;
            yield break;
        }
        float deadline = Time.realtimeSinceStartup + ReadbackTimeoutSeconds;
        while (!request.done)
        {
            if (Time.realtimeSinceStartup >= deadline)
            {
                // Keep the dedicated RT alive until the outstanding request
                // completes, then release it from Update without blocking JSON.
                sPendingReadbackCleanup.Add(new PendingReadbackCleanup
                {
                    Request = request,
                    Texture = intermediate
                });
                capture.Error = "Async GPU texture readback timed out after " +
                    ReadbackTimeoutSeconds.ToString(CultureInfo.InvariantCulture) + " seconds.";
                yield break;
            }
            yield return null;
        }
        if (!TryBuildSignature(request, readbackSize, readbackSize,
                out PixelSignature signature, out string signatureError))
        {
            Destroy(intermediate);
            capture.Error = signatureError;
            yield break;
        }
        capture.Signature = signature;
        capture.Succeeded = true;
        Destroy(intermediate);
    }

    private static bool TryBeginReadback(
        Texture source,
        int readbackSize,
        out RenderTexture intermediate,
        out AsyncGPUReadbackRequest request,
        out string error)
    {
        intermediate = null;
        request = default;
        error = null;
        try
        {
            // Normalize native-written BGRA/external textures through a small
            // RGBA render target shared by every native graphics API.
            intermediate = new RenderTexture(
                readbackSize, readbackSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            intermediate.hideFlags = HideFlags.HideAndDontSave;
            intermediate.Create();
            Graphics.Blit(source, intermediate);
            request = AsyncGPUReadback.Request(intermediate, 0, TextureFormat.RGBA32);
            return true;
        }
        catch (Exception exception)
        {
            if (intermediate != null)
            {
                Destroy(intermediate);
                intermediate = null;
            }
            error = "Could not start async GPU texture readback: " +
                exception.GetType().Name + ": " + exception.Message;
            return false;
        }
    }

    private static bool TryBuildSignature(
        AsyncGPUReadbackRequest request,
        int width,
        int height,
        out PixelSignature signature,
        out string error)
    {
        signature = default;
        error = null;
        try
        {
            if (request.hasError)
            {
                error = "Async GPU texture readback failed.";
                return false;
            }
            var pixels = request.GetData<Color32>();
            int requiredPixels = width * height;
            if (pixels.Length < requiredPixels)
            {
                error = "Async GPU texture readback returned " +
                    pixels.Length.ToString(CultureInfo.InvariantCulture) +
                    " pixels; expected at least " +
                    requiredPixels.ToString(CultureInfo.InvariantCulture) + ".";
                return false;
            }
            ulong hash = 14695981039346656037UL;
            int visiblePixels = 0;
            for (int i = 0; i < requiredPixels; i++)
            {
                Color32 pixel = pixels[i];
                if (pixel.a > 8)
                {
                    visiblePixels++;
                }
                hash = HashByte(hash, pixel.r);
                hash = HashByte(hash, pixel.g);
                hash = HashByte(hash, pixel.b);
                hash = HashByte(hash, pixel.a);
            }
            signature = new PixelSignature
            {
                Hash = hash.ToString("x16", CultureInfo.InvariantCulture),
                VisiblePixels = visiblePixels
            };
            return true;
        }
        catch (Exception exception)
        {
            error = "Could not consume async GPU texture readback: " +
                exception.GetType().Name + ": " + exception.Message;
            return false;
        }
    }

    private static bool TryCaptureManagedTexture(Texture2D texture, out PixelSignature signature, out string error)
    {
        signature = default;
        error = null;
        try
        {
            var pixels = texture.GetRawTextureData<byte>();
            int expectedLength = texture.width * texture.height * 4;
            if (pixels.Length < expectedLength)
            {
                error = "Texture pixel buffer is smaller than expected.";
                return false;
            }

            ulong hash = 14695981039346656037UL;
            int visiblePixels = 0;
            for (int sampleY = 0; sampleY < 64; sampleY++)
            {
                int y = sampleY * (texture.height - 1) / 63;
                for (int sampleX = 0; sampleX < 64; sampleX++)
                {
                    int x = sampleX * (texture.width - 1) / 63;
                    int offset = (y * texture.width + x) * 4;
                    byte first = pixels[offset];
                    byte second = pixels[offset + 1];
                    byte third = pixels[offset + 2];
                    byte alpha = pixels[offset + 3];
                    if (alpha > 8)
                    {
                        visiblePixels++;
                    }
                    hash = HashByte(hash, first);
                    hash = HashByte(hash, second);
                    hash = HashByte(hash, third);
                    hash = HashByte(hash, alpha);
                }
            }

            signature = new PixelSignature
            {
                Hash = hash.ToString("x16", CultureInfo.InvariantCulture),
                VisiblePixels = visiblePixels
            };
            return true;
        }
        catch (Exception exception)
        {
            error = exception.GetType().Name + ": " + exception.Message;
            return false;
        }
    }

    private static ulong HashByte(ulong hash, byte value)
    {
        return (hash ^ value) * 1099511628211UL;
    }

    private static long FrameDistance(int start, int current, long totalFrames)
    {
        if (current >= start)
        {
            return current - start;
        }
        return Math.Max(0L, totalFrames - start + current);
    }

    private static string DescribeAnimation(LottieAnimation animation)
    {
        if (animation == null)
        {
            return "null";
        }
        Texture texture = animation.OutputTexture;
        return string.Format(CultureInfo.InvariantCulture,
            "frames={0}, fps={1:F2}, texture={2}x{3}, backend={4}",
            animation.TotalFramesCount,
            animation.FrameRate,
            texture == null ? 0 : texture.width,
            texture == null ? 0 : texture.height,
            animation.TextureUploadBackend);
    }

    private static string DescribeSignature(PixelSignature signature)
    {
        return "hash=" + signature.Hash + ", visiblePixels=" +
            signature.VisiblePixels.ToString(CultureInfo.InvariantCulture);
    }

    private static string DescribeTransition(PixelSignature first, PixelSignature second)
    {
        return "first=" + DescribeSignature(first) + ", later=" + DescribeSignature(second);
    }

    private static void WriteResultAtomically(string path, string contents)
    {
        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        string temporaryPath = fullPath + ".tmp";
        File.WriteAllText(temporaryPath, contents);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
        File.Move(temporaryPath, fullPath);
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

    private static bool IsAndroidSmokeRequested()
    {
        return GetAndroidBooleanExtra(AndroidRequestExtra);
    }

    private static bool IsAndroidFpsOnlyRequested()
    {
        return GetAndroidBooleanExtra(AndroidFpsOnlyExtra);
    }

    private static bool GetAndroidBooleanExtra(string name)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (AndroidJavaObject intent = activity.Call<AndroidJavaObject>("getIntent"))
            {
                return intent.Call<bool>("getBooleanExtra", name, false);
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Could not read Android launch extra '" + name + "': " + exception.Message);
        }
#endif
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
}
