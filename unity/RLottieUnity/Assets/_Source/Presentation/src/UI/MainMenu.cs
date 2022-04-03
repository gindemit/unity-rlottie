using LottiePlugin.UI;
using UnityEngine;

namespace Presentation.UI
{
    internal sealed class MainMenu : MonoBehaviour, System.IDisposable
    {
        [SerializeField] private AnimationsHomeScreen _animationsHomeScreen;
        [SerializeField] private LottiePlayerScreen _lottiePlayerScreen;
        [SerializeField] private ExploreTelegramStickers _exploreTelegramStickers;
        [SerializeField] private UnityEngine.UI.RawImage _activeButtonImage;
        [SerializeField] private AnimatedButton _homeButton;
        [SerializeField] private AnimatedButton _playerButton;
        [SerializeField] private AnimatedButton _exploreButton;
        [SerializeField] private AnimatedButton _3dObjectsButton;

        internal void Init(string[] animationPaths, string[] animations)
        {
            _homeButton.OnClick.AddListener(OnHomeButtonClick);
            _playerButton.OnClick.AddListener(OnPlayerButtonClick);
            _exploreButton.OnClick.AddListener(OnExploreButtonClick);
            _3dObjectsButton.OnClick.AddListener(On3dObjectsButtonClick);
            _animationsHomeScreen.Init(animationPaths);
            _lottiePlayerScreen.Init(animationPaths, animations);
            _exploreTelegramStickers.Init();
            _lottiePlayerScreen.gameObject.SetActive(false);
            _exploreTelegramStickers.gameObject.SetActive(false);
            UpdateActiveButtonPosition(_homeButton.Transform);
        }
        public void Dispose()
        {
            _homeButton.OnClick.RemoveListener(OnHomeButtonClick);
            _playerButton.OnClick.RemoveListener(OnPlayerButtonClick);
            _exploreButton.OnClick.RemoveListener(OnExploreButtonClick);
            _3dObjectsButton.OnClick.RemoveListener(On3dObjectsButtonClick);
            _animationsHomeScreen.Dispose();
            _lottiePlayerScreen.Dispose();
            _exploreTelegramStickers.Dispose();
        }

        private void UpdateActiveButtonPosition(Transform transform)
        {
            var targetXPos = transform.position.x;
            var pos = _activeButtonImage.transform.position;
            _activeButtonImage.transform.position = new Vector3(targetXPos, pos.y, pos.z);
        }
        private void OnHomeButtonClick(int stateIndex, AnimatedButton.State state)
        {
            _animationsHomeScreen.gameObject.SetActive(true);
            _lottiePlayerScreen.gameObject.SetActive(false);
            _exploreTelegramStickers.gameObject.SetActive(false);
            UpdateActiveButtonPosition(_homeButton.Transform);
        }
        private void OnPlayerButtonClick(int stateIndex, AnimatedButton.State state)
        {
            _animationsHomeScreen.gameObject.SetActive(false);
            _lottiePlayerScreen.gameObject.SetActive(true);
            _exploreTelegramStickers.gameObject.SetActive(false);
            UpdateActiveButtonPosition(_playerButton.Transform);
        }
        private void OnExploreButtonClick(int stateIndex, AnimatedButton.State state)
        {
            _animationsHomeScreen.gameObject.SetActive(false);
            _lottiePlayerScreen.gameObject.SetActive(false);
            _exploreTelegramStickers.gameObject.SetActive(true);
            UpdateActiveButtonPosition(_exploreButton.Transform);
        }
        private void On3dObjectsButtonClick(int stateIndex, AnimatedButton.State state)
        {
            UpdateActiveButtonPosition(_3dObjectsButton.Transform);
        }
    }
}
