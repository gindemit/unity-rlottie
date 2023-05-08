using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace LottiePlugin.UI
{
    [RequireComponent(typeof(RawImage))]
    public sealed class AnimatedImage : MonoBehaviour
    {
        public Transform Transform { get; private set; }
        public RawImage RawImage { get => _rawImage; internal set { _rawImage = value; } }
        public LottieAnimation LottieAnimation { get; private set; }
        internal TextAsset AnimationJson => _animationJson;
        internal uint TextureWidth => _textureWidth;
        internal uint TextureHeight => _textureHeight;


        [SerializeField] private TextAsset _animationJson;
        [SerializeField] private RawImage _rawImage;
        [SerializeField] private float _animationSpeed = 1f;
        [SerializeField] private uint _textureWidth;
        [SerializeField] private uint _textureHeight;
        [SerializeField] private bool _playOnAwake = true;
        [SerializeField] private bool _loop = true;
        private Coroutine _renderLottieAnimationCoroutine;
        private WaitForEndOfFrame _waitForEndOfFrame;

        private void Awake()
        {
            Transform = transform;
        }

        private void Start()
        {
            if (_animationJson == null)
            {
                return;
            }
            _rawImage = GetComponent<RawImage>();
            LottieAnimation = LottieAnimation.LoadFromJsonData(
                _animationJson.text,
                string.Empty,
                _textureWidth,
                _textureHeight);
            _rawImage.texture = LottieAnimation.Texture;
            _waitForEndOfFrame = new WaitForEndOfFrame();
            if (_playOnAwake)
            {
                Play();
            }
            else
            {
                LottieAnimation.DrawOneFrame(0);
            }
        }
        private void OnDestroy()
        {
            LottieAnimation?.Dispose();
            LottieAnimation = null;
        }

        public void Play()
        {
            if (_renderLottieAnimationCoroutine != null)
            {
                StopCoroutine(_renderLottieAnimationCoroutine);
            }
            LottieAnimation.Play();
            _renderLottieAnimationCoroutine = StartCoroutine(RenderLottieAnimationCoroutine());
        }
        public void Stop()
        {
            if (_renderLottieAnimationCoroutine != null)
            {
                StopCoroutine(_renderLottieAnimationCoroutine);
                _renderLottieAnimationCoroutine = null;
            }
            LottieAnimation.Stop();
            LottieAnimation.DrawOneFrame(0);
        }

        private IEnumerator RenderLottieAnimationCoroutine()
        {
            while (true)
            {
                if (LottieAnimation != null)
                {
                    LottieAnimation.Update(_animationSpeed);
                    if (!_loop && LottieAnimation.CurrentFrame == LottieAnimation.TotalFramesCount - 1)
                    {
                        Stop();
                    }
                }
                yield return _waitForEndOfFrame;
            }
        }
    }
}
