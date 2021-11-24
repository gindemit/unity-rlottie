using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Presentation
{
    public class LottiePlayerScreen : MonoBehaviour, System.IDisposable
    {
        [SerializeField] private RawImage _animationImage;
        [SerializeField] private TMP_Dropdown _animationDropdown;
        [SerializeField] private TMP_Text _frameRateText;
        [SerializeField] private TMP_Text _totalFramesCountText;
        [SerializeField] private TMP_Text _durationSecondsText;
        [SerializeField] private Slider _playPositionSlider;
        [SerializeField] private LottiePlugin.UI.AnimatedButton _playPauseButton;
        [SerializeField] private LottiePlugin.UI.AnimatedButton _nextAnimationButton;

        private LottiePlugin.LottieAnimation _lottieAnimation;
        private bool _ignoreSliderCallback;

        internal void Init()
        {
            _animationDropdown.onValueChanged.AddListener(OnAnimationDropdownValueChanged);
            _playPositionSlider.onValueChanged.AddListener(OnPlayPositionSliderValueChanged);
            _playPauseButton.OnClick.AddListener(OnPlayPauseButtonClick);
            _nextAnimationButton.OnClick.AddListener(OnNextAnimationClick);
            OnAnimationDropdownValueChanged(0);
        }
        public void Dispose()
        {
            _animationDropdown.onValueChanged.RemoveListener(OnAnimationDropdownValueChanged);
            _playPositionSlider.onValueChanged.RemoveListener(OnPlayPositionSliderValueChanged);
            _playPauseButton.OnClick.RemoveListener(OnPlayPauseButtonClick);
            _nextAnimationButton.OnClick.RemoveListener(OnNextAnimationClick);
            _animationImage.texture = null;
            if (_lottieAnimation != null)
            {
                _lottieAnimation.Dispose();
                _lottieAnimation = null;
            }
        }

        private void Update()
        {
            if (_lottieAnimation != null)
            {
                _lottieAnimation.Update();
                if (_lottieAnimation.IsPlaying)
                {
                    _playPositionSlider.value = _lottieAnimation.CurrentFrame;
                }
            }
        }

        private void OnAnimationDropdownValueChanged(int newValue)
        {
            string selectedFileName = _animationDropdown.options[newValue].text;
            selectedFileName += ".json";
            string targetFilePath = Path.Combine(Application.persistentDataPath, selectedFileName);
            if (!File.Exists(targetFilePath))
            {
                CopyFileFromStreamingAssetsToPersistentData(selectedFileName, targetFilePath);
            }
            if (_lottieAnimation != null)
            {
                _lottieAnimation.Dispose();
            }
            _lottieAnimation = LottiePlugin.LottieAnimation.LoadFromJsonFile(targetFilePath, 512, 512);
            _animationImage.texture = _lottieAnimation.Texture;
            _frameRateText.text = _lottieAnimation.FrameRate.ToString();
            _totalFramesCountText.text = _lottieAnimation.TotalFramesCount.ToString();
            _durationSecondsText.text = _lottieAnimation.DurationSeconds.ToString("F3");
            _ignoreSliderCallback = true;
            _playPositionSlider.maxValue = _lottieAnimation.TotalFramesCount;
            _ignoreSliderCallback = false;
            _playPauseButton.ResetState();
        }
        private void OnPlayPositionSliderValueChanged(float newValue)
        {
            if (!_ignoreSliderCallback && newValue != _lottieAnimation.CurrentFrame)
            {
                _lottieAnimation.Pause();
                _lottieAnimation.DrawOneFrame(Mathf.RoundToInt(newValue));
            }
        }
        private void OnPlayPauseButtonClick(int currentStateIndex, LottiePlugin.UI.AnimatedButton.State state)
        {
            _lottieAnimation.TogglePlay();
        }
        private void OnNextAnimationClick(int currentStateIndex, LottiePlugin.UI.AnimatedButton.State state)
        {
            int animationToSelectAsNext = _animationDropdown.value;
            if (++animationToSelectAsNext >= _animationDropdown.options.Count)
            {
                animationToSelectAsNext = 0;
            }
            _animationDropdown.value = animationToSelectAsNext;
        }
        private void CopyFileFromStreamingAssetsToPersistentData(string streamingAssetsFilePath, string targetFilePath)
        {
            byte[] file = Support.StreamingAssets.StreamingAssetsHelper.LoadFileFromStreamingAssets(streamingAssetsFilePath);
            File.WriteAllBytes(targetFilePath, file);
        }
    }
}
