using UnityEngine;
using UnityEngine.UI;

namespace LottiePlugin.Sample.SceneUI.UI
{
    internal sealed class AnimationPreview : MonoBehaviour, System.IDisposable
    {
        internal RectTransform RectTransform => _rectTransform;

        [SerializeField] private RectTransform _rectTransform;
        [SerializeField] private RawImage _animationPreview;
        [SerializeField] private TextAsset _animationJsonData;

        private LottiePlugin.LottieAnimation _lottieAnimation;
        private bool _animationEnabled = true;

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
            Destroy(gameObject);
        }

        internal void DoUpdate()
        {
            if (!_animationEnabled)
            {
                return;
            }
            _lottieAnimation.Update();
        }
        internal void DoUpdateAsync()
        {
            if (!_animationEnabled)
            {
                return;
            }
            _lottieAnimation.UpdateAsync();
        }
        internal void DoDrawOneFrameAsyncGetResult()
        {
            if (!_animationEnabled)
            {
                return;
            }
            _lottieAnimation.DrawOneFrameAsyncGetResult();
        }

        internal void DisableAnimation()
        {
            _animationEnabled = false;
        }

        internal void EnableAnimation()
        {
            _animationEnabled = true;
        }
    }
}
