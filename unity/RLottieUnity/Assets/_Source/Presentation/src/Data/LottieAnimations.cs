using UnityEngine;

namespace Presentation.Data
{
    [CreateAssetMenu(fileName ="LottieAnimationsArray", menuName = "Data/Lottie Animations")]
    public sealed class LottieAnimations : ScriptableObject
    {
        internal string[] Animations => _animations;

        [SerializeField] private string[] _animations;

        public void SetAnimationsEditorCall(string[] animations)
        {
            _animations = animations;
        }
    }
}
