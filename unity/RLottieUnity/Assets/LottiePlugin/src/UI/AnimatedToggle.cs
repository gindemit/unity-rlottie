using UnityEngine;
using UnityEngine.UI;

namespace LottiePlugin.UI
{
    public sealed class AnimatedToggle : Toggle
    {
        internal TextAsset AnimationJson => _animationJson;
        internal uint TextureWidth => _textureWidth;
        internal uint TextureHeight => _textureHeight;

        [SerializeField] private TextAsset _animationJson;
        [SerializeField] private uint _textureWidth;
        [SerializeField] private uint _textureHeight;
    }
}
