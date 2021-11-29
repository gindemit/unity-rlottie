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

        internal void Init(string animation, uint width, uint height)
        {
            animation += ".json";
            string targetFilePath = Path.Combine(Application.persistentDataPath, animation);
            if (!File.Exists(targetFilePath))
            {
                LottiePlayerScreen.CopyFileFromStreamingAssetsToPersistentData(animation, targetFilePath);
            }
            _lottieAnimation = LottiePlugin.LottieAnimation.LoadFromJsonFile(targetFilePath, width, height);
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
    }
}
