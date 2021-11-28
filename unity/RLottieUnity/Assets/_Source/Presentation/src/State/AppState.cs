using UnityEngine;

namespace Presentation.State
{
    internal class AppState : MonoBehaviour
    {
        [SerializeField] private Data.LottieAnimations _lottieAnimations;
        [SerializeField] private UI.MainMenu _mainMenu;

        private void Awake()
        {
            _mainMenu.Init(_lottieAnimations);
        }
        private void OnDestroy()
        {
            _mainMenu.Dispose();
        }
    }
}
