using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace Presentation.UI
{
    internal sealed class AnimationPreview : MonoBehaviour, System.IDisposable
    {
        internal RectTransform RectTransform => _rectTransform;

        [SerializeField] private RectTransform _rectTransform;
        [SerializeField] private RawImage _animationPreview;

        private LottiePlugin.LottieAnimation _lottieAnimation;

        internal void Init(string jsonFilePath, uint width, uint height)
        {
            _lottieAnimation = LottiePlugin.LottieAnimation.LoadFromJsonFile(jsonFilePath, width, height);
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
