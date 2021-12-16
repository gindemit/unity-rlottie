using LottiePlugin.UI;
using UnityEngine;

namespace Presentation.UI
{
    internal sealed class MainMenu : MonoBehaviour, System.IDisposable
    {
        [SerializeField] private AnimationsHomeScreen _animationsHomeScreen;
        [SerializeField] private LottiePlayerScreen _lottiePlayerScreen;
        [SerializeField] private ExploreTelegramStickers _exploreTelegramStickers;
        [SerializeField] private AnimatedButton _homeButton;
        [SerializeField] private AnimatedButton _playerButton;
        [SerializeField] private AnimatedButton _exploreButton;

        internal void Init(string[] animationPaths, string[] animations)
        {
            _homeButton.OnClick.AddListener(OnHomeButtonClick);
            _playerButton.OnClick.AddListener(OnPlayerButtonClick);
            _exploreButton.OnClick.AddListener(OnExploreButtonClick);
            _animationsHomeScreen.Init(animationPaths);
            _lottiePlayerScreen.Init(animationPaths, animations);
            _lottiePlayerScreen.gameObject.SetActive(false);
            _exploreTelegramStickers.gameObject.SetActive(false);
        }
        public void Dispose()
        {
            _homeButton.OnClick.RemoveListener(OnHomeButtonClick);
            _playerButton.OnClick.RemoveListener(OnPlayerButtonClick);
            _exploreButton.OnClick.RemoveListener(OnExploreButtonClick);
            _animationsHomeScreen.Dispose();
            _lottiePlayerScreen.Dispose();
        }

        private void OnHomeButtonClick(int stateIndex, AnimatedButton.State state)
        {
            _animationsHomeScreen.gameObject.SetActive(true);
            _lottiePlayerScreen.gameObject.SetActive(false);
            _exploreTelegramStickers.gameObject.SetActive(false);
        }
        private void OnPlayerButtonClick(int stateIndex, AnimatedButton.State state)
        {
            _animationsHomeScreen.gameObject.SetActive(false);
            _lottiePlayerScreen.gameObject.SetActive(true);
            _exploreTelegramStickers.gameObject.SetActive(false);
        }
        private void OnExploreButtonClick(int stateIndex, AnimatedButton.State state)
        {
            _animationsHomeScreen.gameObject.SetActive(false);
            _lottiePlayerScreen.gameObject.SetActive(false);
            _exploreTelegramStickers.gameObject.SetActive(true);
        }
    }
}
