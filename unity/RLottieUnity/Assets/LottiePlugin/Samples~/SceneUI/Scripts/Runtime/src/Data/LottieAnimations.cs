using UnityEngine;

namespace LottiePlugin.Sample.SceneUI.Data
{
    [CreateAssetMenu(fileName ="LottieAnimationsArray", menuName = "Data/Lottie Animations")]
    public sealed class LottieAnimations : ScriptableObject
    {
        internal TextAsset[] Animations => _animations;

        [SerializeField] private TextAsset[] _animations;
    }
}
