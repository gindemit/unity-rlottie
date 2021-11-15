using UnityEngine;
using UnityEngine.UI;

namespace LottiePlugin.UI
{
    public sealed class AnimatedToggle : Toggle
    {
        internal TextAsset AnimationJson => _animationJson;
        [SerializeField] private TextAsset _animationJson;
    }
}
