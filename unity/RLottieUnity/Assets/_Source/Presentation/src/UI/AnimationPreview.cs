using UnityEngine;
using UnityEngine.UI;

namespace Presentation.UI
{
    internal sealed class AnimationPreview : MonoBehaviour, System.IDisposable
    {
        internal RectTransform RectTransform => _rectTransform;

        [SerializeField] private RectTransform _rectTransform;
        [SerializeField] private RawImage _animationPreview;
        [SerializeField] private TextAsset _animationJsonData;

        private LottiePlugin.LottieAnimation _lottieAnimation;

        internal void InitFromFile(string jsonFilePath, uint width, uint height)
        {
            _lottieAnimation = LottiePlugin.LottieAnimation.LoadFromJsonFile(jsonFilePath, width, height);
            _animationPreview.texture = _lottieAnimation.Texture;
            DoUpdate();
        }
        internal void InitFromData(uint width, uint height)
        {
            if (_animationJsonData == null || string.IsNullOrWhiteSpace(_animationJsonData.text))
            {
                Debug.LogError($"Can not initialize {nameof(AnimationPreview)} from null or empty jsong file");
                return;
            }
            _lottieAnimation = LottiePlugin.LottieAnimation.LoadFromJsonData(_animationJsonData.text, string.Empty, width, height);
            _animationPreview.texture = _lottieAnimation.Texture;
            DoUpdate();
        }
        public void Dispose()
        {
            _lottieAnimation.Dispose();
        }

        internal void DoUpdate()
        {
            _lottieAnimation.Update();
        }
        internal void DoUpdateAsync()
        {
            _lottieAnimation.UpdateAsync();
        }
        internal void DoDrawOneFrameAsyncGetResult()
        {
            _lottieAnimation.DrawOneFrameAsyncGetResult();
        }
    }
}
