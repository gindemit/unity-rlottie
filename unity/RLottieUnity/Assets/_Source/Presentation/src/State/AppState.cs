using UnityEngine;

namespace Presentation.State
{
    internal class AppState : MonoBehaviour
    {
        [SerializeField] private Data.LottieAnimations _lottieAnimations;
        [SerializeField] private UI.MainMenu _mainMenu;

        private void Start()
        {
            Utility.FilesHelper.CopyAnimationsJsonsFromStreamingAssetsToPersistentData(
                _lottieAnimations.Animations);
            _mainMenu.Init(
                Utility.FilesHelper.GetPersistentAnimationsPaths(_lottieAnimations.Animations),
                _lottieAnimations.Animations);
        }
        private void OnDestroy()
        {
            _mainMenu.Dispose();
        }
    }
}
