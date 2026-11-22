#if UNITY_EDITOR
using System;
using UnityEngine;

namespace LottiePlugin.UI.Editor
{
    internal static class AnimationInspector
    {
        internal static int LastFrameIndex(long frameCount)
        {
            return frameCount <= 1 ? 0 : (int)Math.Min(frameCount - 1, int.MaxValue);
        }

        // A null asset permits the component's file-path source.
        internal static bool HasEmptyAsset(TextAsset asset)
        {
            return asset != null && string.IsNullOrEmpty(asset.text);
        }

        internal static string FormatInfo(LottieAnimation animation, float speed)
        {
            if (animation == null) return null;
            string playDuration = speed > 0f ? $"{animation.DurationSeconds / speed:F2} s" : "Paused";
            return $"Frame rate {animation.FrameRate:F2} fps   Frames {animation.TotalFramesCount}   Current frame {animation.CurrentFrame}\n" +
                $"Original duration {animation.DurationSeconds:F2} s   Play duration {playDuration}";
        }
    }
}
#endif
