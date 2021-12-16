using UnityEngine;

namespace Presentation.UI
{
    internal sealed class AnimationsHomeScreen : MonoBehaviour, System.IDisposable
    {
        [SerializeField] private LottieAnimationsPreview _lottieAnimationsPreview;

        internal void Init(string[] animationPaths)
        {
            _lottieAnimationsPreview.Init(animationPaths);
        }
        public void Dispose()
        {
            _lottieAnimationsPreview.Dispose();
        }
    }
}
