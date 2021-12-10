using LottiePlugin.UI;
using UnityEngine;

namespace Presentation.UI
{
    internal sealed class MainMenu : MonoBehaviour, System.IDisposable
    {
        [SerializeField] private LottiePlayerScreen _lottiePlayerScreen;
        [SerializeField] private LottieAnimationsPreview _lottieAnimationsPreview;
        [SerializeField] private AnimatedButton _homeButton;
        [SerializeField] private AnimatedButton _playerButton;
        [SerializeField] private AnimatedButton _exploreButton;

        internal void Init(string[] animations)
        {
            _homeButton.OnClick.AddListener(OnHomeButtonClick);
            _playerButton.OnClick.AddListener(OnPlayerButtonClick);
            _exploreButton.OnClick.AddListener(OnExploreButtonClick);
            _lottieAnimationsPreview.Init(animations);
            _lottiePlayerScreen.Init(animations);
            _lottiePlayerScreen.gameObject.SetActive(false);
        }
        public void Dispose()
        {
            _homeButton.OnClick.RemoveListener(OnHomeButtonClick);
            _playerButton.OnClick.RemoveListener(OnPlayerButtonClick);
            _exploreButton.OnClick.RemoveListener(OnExploreButtonClick);
            _lottieAnimationsPreview.Dispose();
            _lottiePlayerScreen.Dispose();
        }

        private void OnHomeButtonClick(int stateIndex, AnimatedButton.State state)
        {
            _lottiePlayerScreen.gameObject.SetActive(false);
            _lottieAnimationsPreview.gameObject.SetActive(true);
        }
        private void OnPlayerButtonClick(int stateIndex, AnimatedButton.State state)
        {
            _lottiePlayerScreen.gameObject.SetActive(true);
            _lottieAnimationsPreview.gameObject.SetActive(false);
        }
        private void OnExploreButtonClick(int stateIndex, AnimatedButton.State state)
        {

        }
    }
}
