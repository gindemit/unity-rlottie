using LottiePlugin.UI;
using UnityEngine;

namespace LottiePlugin.Sample.SceneUI.UI
{
    internal sealed class MainMenu : MonoBehaviour, System.IDisposable
    {
        private const int BUTTONS_COUNT = 4;

        [SerializeField] private RectTransform _rectTransform;
        [SerializeField] private AnimationsHomeScreen _animationsHomeScreen;
        [SerializeField] private LottiePlayerScreen _lottiePlayerScreen;
        [SerializeField] private UnityEngine.UI.RawImage _activeButtonImage;
        [SerializeField] private AnimatedButton _homeButton;
        [SerializeField] private AnimatedButton _playerButton;

        internal void Init(TextAsset[] animations)
        {
            _homeButton.OnClick.AddListener(OnHomeButtonClick);
            _playerButton.OnClick.AddListener(OnPlayerButtonClick);
            _animationsHomeScreen.Init(animations);
            _lottiePlayerScreen.Init(animations);
            _lottiePlayerScreen.gameObject.SetActive(false);
            UpdateTheButtonsPositions();
            UpdateActiveButtonPosition(_homeButton.Transform);
        }
        public void Dispose()
        {
            _homeButton.OnClick.RemoveListener(OnHomeButtonClick);
            _playerButton.OnClick.RemoveListener(OnPlayerButtonClick);
            _animationsHomeScreen.Dispose();
            _lottiePlayerScreen.Dispose();
        }

        private void UpdateTheButtonsPositions()
        {
            float width = _rectTransform.rect.size.x;
            float step = width / BUTTONS_COUNT;
            float fixedYPos = _homeButton.Transform.localPosition.y;
            _homeButton.Transform.localPosition = new Vector3(-step * 1.5f, fixedYPos, 0);
            _playerButton.Transform.localPosition = new Vector3(-step * 0.5f, fixedYPos, 0);
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
            UpdateActiveButtonPosition(_homeButton.Transform);
        }
        private void OnPlayerButtonClick(int stateIndex, AnimatedButton.State state)
        {
            _animationsHomeScreen.gameObject.SetActive(false);
            _lottiePlayerScreen.gameObject.SetActive(true);
            UpdateActiveButtonPosition(_playerButton.Transform);
        }
    }
}
