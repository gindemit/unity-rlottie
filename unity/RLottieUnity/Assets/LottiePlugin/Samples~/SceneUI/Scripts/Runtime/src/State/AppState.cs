using UnityEngine;

namespace LottiePlugin.Sample.SceneUI.State
{
    internal class AppState : MonoBehaviour
    {
        [SerializeField] private Data.LottieAnimations _lottieAnimations;
        [SerializeField] private UI.MainMenu _mainMenu;

        private void Start()
        {
            _mainMenu.Init(_lottieAnimations.Animations);
        }
        private void OnDestroy()
        {
            _mainMenu.Dispose();
        }
    }
}
