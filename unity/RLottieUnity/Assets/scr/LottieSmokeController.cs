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
    private const float AnimationTimeoutSeconds = 5f;
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

    private SmokeResult _result;
    private string _resultPath;
    private bool _quitWhenComplete;

    private IEnumerator Start()
    {
        string[] arguments = Environment.GetCommandLineArgs();
        if (HasArgument(arguments, "-lottieBenchmarkMatrix"))
        {
            yield break;
        }

        _resultPath = GetArgument(arguments, ResultArgument, string.Empty);
        if (string.IsNullOrEmpty(_resultPath) && Application.platform == RuntimePlatform.Android)
        {
            _resultPath = Path.Combine(Application.persistentDataPath, DefaultResultFileName);
        }
        if (string.IsNullOrEmpty(_resultPath))
        {
            yield break;
        }

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

        PixelSignature buttonInitial;
        bool buttonInitialCaptured = TryCapture(animatedButton.Animation.OutputTexture, out buttonInitial, out string buttonCaptureError);
        var imageInitialCapture = new PixelCapture();
        yield return CaptureTexture(imageAnimation.OutputTexture, imageAnimation.TextureUploadBackend, imageInitialCapture);
        if (!buttonInitialCaptured || !imageInitialCapture.Succeeded)
        {
            Record("initialPixelsReadable", false, buttonCaptureError ?? imageInitialCapture.Error);
            Complete("Could not read the initial rendered textures.");
            yield break;
        }
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

        PixelSignature buttonLater;
        bool buttonCaptured = TryCapture(buttonAnimation.OutputTexture, out buttonLater, out string buttonLaterError);
        Record("animatedButtonAcceptsPress", clickReceived, "onClickInvoked=" + clickReceived.ToString(CultureInfo.InvariantCulture));
        Record("animatedButtonAdvancesAfterPress", buttonAnimation.CurrentFrame != buttonStartFrame,
            "from=" + buttonStartFrame.ToString(CultureInfo.InvariantCulture) +
            ", to=" + buttonAnimation.CurrentFrame.ToString(CultureInfo.InvariantCulture));
        Record("animatedButtonPixelsChangeAfterPress", buttonCaptured && buttonInitial.Hash != buttonLater.Hash,
            buttonCaptured ? DescribeTransition(buttonInitial, buttonLater) : buttonLaterError);

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
        if (backend == LottieTextureUploadBackend.NativeVulkan)
        {
            yield return CaptureTextureAsync(source, capture);
            yield break;
        }

        capture.Succeeded = TryCapture(source, out capture.Signature, out capture.Error);
    }

    private static IEnumerator CaptureTextureAsync(Texture source, PixelCapture capture)
    {
        if (source == null)
        {
            capture.Error = "OutputTexture is null.";
            yield break;
        }

        AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(source, 0, TextureFormat.RGBA32);
        while (!request.done)
        {
            yield return null;
        }
        if (request.hasError)
        {
            capture.Error = "Async GPU texture readback failed.";
            yield break;
        }

        var pixels = request.GetData<Color32>();
        ulong hash = 14695981039346656037UL;
        int visiblePixels = 0;
        for (int sampleY = 0; sampleY < 64; sampleY++)
        {
            int y = sampleY * (source.height - 1) / 63;
            for (int sampleX = 0; sampleX < 64; sampleX++)
            {
                int x = sampleX * (source.width - 1) / 63;
                Color32 pixel = pixels[y * source.width + x];
                if (pixel.a > 8)
                {
                    visiblePixels++;
                }
                hash = HashByte(hash, pixel.r);
                hash = HashByte(hash, pixel.g);
                hash = HashByte(hash, pixel.b);
                hash = HashByte(hash, pixel.a);
            }
        }

        capture.Signature = new PixelSignature
        {
            Hash = hash.ToString("x16", CultureInfo.InvariantCulture),
            VisiblePixels = visiblePixels
        };
        capture.Succeeded = true;
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
