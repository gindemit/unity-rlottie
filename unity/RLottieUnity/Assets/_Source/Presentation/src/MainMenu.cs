using UnityEngine;

namespace Presentation
{
    internal sealed class MainMenu : MonoBehaviour
    {
        [SerializeField] private LottiePlayerScreen _lottiePlayerScreen;

        private void Awake()
        {
            _lottiePlayerScreen.Init();
        }
        private void OnDestroy()
        {
            _lottiePlayerScreen.Dispose();
        }
    }
}
