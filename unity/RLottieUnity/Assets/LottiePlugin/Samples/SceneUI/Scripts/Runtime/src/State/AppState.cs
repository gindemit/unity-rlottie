using LottiePlugin.Support;
using UnityEngine;

namespace LottiePlugin.Sample.SceneUI.State
{
    internal class AppState : MonoBehaviour
    {
        [SerializeField] private Data.LottieAnimations _lottieAnimations;
        [SerializeField] private UI.MainMenu _mainMenu;

        private void Start()
        {
            FilesHelper.CopyAnimationsJsonsFromStreamingAssetsToPersistentData(
                _lottieAnimations.Animations);
            _mainMenu.Init(
                FilesHelper.GetPersistentAnimationsPaths(_lottieAnimations.Animations),
                _lottieAnimations.Animations);
        }
        private void OnDestroy()
        {
            _mainMenu.Dispose();
        }
    }
}
