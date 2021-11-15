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

        private LottiePlugin.LottieAnimation _lottieAnimation;

        internal void Init()
        {
            _animationDropdown.onValueChanged.AddListener(OnAnimationDropdownValueChanged);
            OnAnimationDropdownValueChanged(0);
        }
        public void Dispose()
        {
            _animationDropdown.onValueChanged.RemoveListener(OnAnimationDropdownValueChanged);
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
            _lottieAnimation = new LottiePlugin.LottieAnimation(targetFilePath, 512, 512);
            _animationImage.texture = _lottieAnimation.Texture;
            _frameRateText.text = _lottieAnimation.FrameRate.ToString();
            _totalFramesCountText.text = _lottieAnimation.TotalFramesCount.ToString();
            _durationSecondsText.text = _lottieAnimation.DurationSeconds.ToString("F3");
        }
        private void CopyFileFromStreamingAssetsToPersistentData(string streamingAssetsFilePath, string targetFilePath)
        {
            byte[] file = Support.StreamingAssets.StreamingAssetsHelper.LoadFileFromStreamingAssets(streamingAssetsFilePath);
            File.WriteAllBytes(targetFilePath, file);
        }
    }
}