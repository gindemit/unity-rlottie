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
        internal TextAsset AnimationJson => _animationJson;
        internal uint TextureWidth => _textureWidth;
        internal uint TextureHeight => _textureHeight;
        internal LottieAnimation LottieAnimation => _lottieAnimation;
        internal float AnimationSpeed => _animationSpeed;
        internal bool Loop => _loop;

        [SerializeField] private TextAsset _animationJson;
        [SerializeField] private RawImage _rawImage;
        [SerializeField] private float _animationSpeed = 1f;
        [SerializeField] private uint _textureWidth;
        [SerializeField] private uint _textureHeight;
        [SerializeField] private bool _playOnAwake = true;
        [SerializeField] private bool _loop = true;

        private LottieAnimation _lottieAnimation;
        private Coroutine _renderLottieAnimationCoroutine;
        private WaitForEndOfFrame _waitForEndOfFrame;

        private void Awake()
        {
            Transform = transform;
            _waitForEndOfFrame = new WaitForEndOfFrame();
        }

        private void Start()
        {
            if (_animationJson == null)
            {
                return;
            }
            if (_rawImage == null)
            {
                _rawImage = GetComponent<RawImage>();
            }
            CreateIfNeededAndReturnLottieAnimation();
            if (_playOnAwake)
            {
                Play();
            }
            else
            {
                _lottieAnimation.DrawOneFrame(0);
            }
        }
        private void OnDestroy()
        {
            DisposeLottieAnimation();
        }

        public void Play()
        {
            if (_renderLottieAnimationCoroutine != null)
            {
                StopCoroutine(_renderLottieAnimationCoroutine);
            }
            _lottieAnimation.Play();
            _renderLottieAnimationCoroutine = StartCoroutine(RenderLottieAnimationCoroutine());
        }
        public void Stop()
        {
            if (_renderLottieAnimationCoroutine != null)
            {
                StopCoroutine(_renderLottieAnimationCoroutine);
                _renderLottieAnimationCoroutine = null;
            }
            _lottieAnimation.Stop();
            _lottieAnimation.DrawOneFrame(0);
        }
        internal LottieAnimation CreateIfNeededAndReturnLottieAnimation()
        {
            if (_animationJson == null)
            {
                return null;
            }
            if (_rawImage == null)
            {
                _rawImage = GetComponent<RawImage>();
            }
            if (_rawImage == null)
            {
                return null;
            }
            if (_lottieAnimation == null)
            {
                _lottieAnimation = LottieAnimation.LoadFromJsonData(
                _animationJson.text,
                string.Empty,
                _textureWidth,
                _textureHeight);
                _rawImage.texture = _lottieAnimation.Texture;
            }
            return _lottieAnimation;
        }
        internal void DisposeLottieAnimation()
        {
            if (_lottieAnimation != null)
            {
                _lottieAnimation.Dispose();
                _lottieAnimation = null;
            }
        }

        private IEnumerator RenderLottieAnimationCoroutine()
        {
            while (true)
            {
                yield return _waitForEndOfFrame;
                if (_lottieAnimation != null)
                {
                    _lottieAnimation.Update(_animationSpeed);
                    if (!_loop && _lottieAnimation.CurrentFrame == _lottieAnimation.TotalFramesCount - 1)
                    {
                        Stop();
                    }
                }
            }
        }
    }
}
