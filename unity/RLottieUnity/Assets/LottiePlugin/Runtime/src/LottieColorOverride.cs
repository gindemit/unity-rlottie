using UnityEngine;

namespace LottiePlugin
{
    /// <summary>The rlottie paint property changed by a color override.</summary>
    public enum LottieColorProperty
    {
        FillColor = 0,
        StrokeColor = 1
    }

    /// <summary>A keypath-based RGB color override applied before a frame is rendered.</summary>
    public struct LottieColorOverride
    {
        public string KeyPath { get; private set; }
        public LottieColorProperty Property { get; private set; }
        public Color Color { get; private set; }

        public LottieColorOverride(string keyPath, LottieColorProperty property, Color color)
        {
            KeyPath = keyPath;
            Property = property;
            Color = color;
        }

        public static LottieColorOverride Fill(string keyPath, Color color)
        {
            return new LottieColorOverride(keyPath, LottieColorProperty.FillColor, color);
        }

        public static LottieColorOverride Stroke(string keyPath, Color color)
        {
            return new LottieColorOverride(keyPath, LottieColorProperty.StrokeColor, color);
        }
    }
}
