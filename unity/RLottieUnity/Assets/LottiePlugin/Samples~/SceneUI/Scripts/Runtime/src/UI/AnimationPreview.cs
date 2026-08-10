using UnityEngine;
using UnityEngine.UI;

namespace LottiePlugin.Sample.SceneUI.UI
{
    internal sealed class AnimationPreview : MonoBehaviour, System.IDisposable
    {
        internal RectTransform RectTransform => _rectTransform;

        [SerializeField] private RectTransform _rectTransform;
        [SerializeField] private RawImage _animationPreview;

        private LottiePlugin.LottieAnimation _lottieAnimation;
        private bool _animationEnabled = true;

        internal void InitFromData(string jsonData, uint width, uint height)
        {
            if (string.IsNullOrWhiteSpace(jsonData))
            {
                Debug.LogError($"Can not initialize {nameof(AnimationPreview)} from null or empty json data");
                return;
            }
            _lottieAnimation = LottiePlugin.LottieAnimation.LoadFromJsonData(jsonData, string.Empty, width, height);
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
