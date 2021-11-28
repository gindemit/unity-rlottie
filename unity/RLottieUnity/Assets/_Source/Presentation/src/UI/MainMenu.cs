using LottiePlugin.UI;
using UnityEngine;

namespace Presentation.UI
{
    internal sealed class MainMenu : MonoBehaviour, System.IDisposable
    {
        [SerializeField] private LottiePlayerScreen _lottiePlayerScreen;
        [SerializeField] private AnimatedButton _homeButton;
        [SerializeField] private AnimatedButton _playerButton;
        [SerializeField] private AnimatedButton _exploreButton;

        internal void Init(Data.LottieAnimations lottieAnimations)
        {
            _homeButton.OnClick.AddListener(OnHomeButtonClick);
            _playerButton.OnClick.AddListener(OnPlayerButtonClick);
            _exploreButton.OnClick.AddListener(OnExploreButtonClick);
            _lottiePlayerScreen.Init(lottieAnimations);
        }
        public void Dispose()
        {
            _homeButton.OnClick.RemoveListener(OnHomeButtonClick);
            _playerButton.OnClick.RemoveListener(OnPlayerButtonClick);
            _exploreButton.OnClick.RemoveListener(OnExploreButtonClick);
            _lottiePlayerScreen.Dispose();
        }

        private void OnHomeButtonClick(int stateIndex, AnimatedButton.State state)
        {

        }
        private void OnPlayerButtonClick(int stateIndex, AnimatedButton.State state)
        {

        }
        private void OnExploreButtonClick(int stateIndex, AnimatedButton.State state)
        {

        }
    }
}
