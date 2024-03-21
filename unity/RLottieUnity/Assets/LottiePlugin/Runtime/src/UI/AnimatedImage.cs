using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace LottiePlugin.UI
{
    [RequireComponent(typeof(RawImage))]
    [ExecuteAlways]
    public sealed class AnimatedImage : MonoBehaviour
    {
        [System.Serializable]
        public class AnimationEvent : UnityEvent<AnimatedImage> {}

        public AnimationEvent Started = new AnimationEvent();
        public AnimationEvent Paused = new AnimationEvent();
        public AnimationEvent Stopped = new AnimationEvent();

        public Transform Transform { get; private set; }
        public RawImage RawImage { get => _rawImage; internal set { _rawImage = value; } }
        internal TextAsset AnimationJson => _animationJson;
        internal uint TextureWidth => _textureWidth;
        internal uint TextureHeight => _textureHeight;
        internal LottieAnimation LottieAnimation => _lottieAnimation;
        internal float AnimationSpeed => _animationSpeed;
        internal bool Loop => _loop;
        internal bool StopOnLastFrame => _stopOnLastFrame;

        [SerializeField] private TextAsset _animationJson;
        [SerializeField] private RawImage _rawImage;
        [SerializeField] private float _animationSpeed = 1f;
        [SerializeField] private uint _textureWidth;
        [SerializeField] private uint _textureHeight;
        [SerializeField] private bool _playOnAwake = true;
        [SerializeField] private bool _loop = true;
        [SerializeField] private bool _stopOnLastFrame = true;

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
            _lottieAnimation.DrawOneFrame(0);
            if (_playOnAwake && Application.isPlaying)
            {
                Play();
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
        public void Pause()
        {
            _lottieAnimation.Pause();
        }
        public void Stop()
        {
            if (_renderLottieAnimationCoroutine != null)
            {
                StopCoroutine(_renderLottieAnimationCoroutine);
                _renderLottieAnimationCoroutine = null;
            }
            _lottieAnimation.Stop();
            int lastFrame = (int)_lottieAnimation.TotalFramesCount - 1;
            _lottieAnimation.DrawOneFrame(_stopOnLastFrame ? lastFrame : 0);
        }
        public void LoadFromAnimationJson(string json, uint width, uint height, string resourcesPath = "")
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new System.ArgumentException("The json parameter should be not null or empty");
            }
            if (_rawImage == null)
            {
                _rawImage = GetComponent<RawImage>();
            }
            if (_rawImage == null)
            {
                throw new System.InvalidOperationException(
                    "Can not find the RawImage component on the current game object: " + gameObject.name);
            }
            DisposeLottieAnimation();
            _lottieAnimation = LottieAnimation.LoadFromJsonData(
                json,
                resourcesPath,
                width,
                height);
            _rawImage.texture = _lottieAnimation.Texture;
            _lottieAnimation.Started += OnAnimationStarted;
            _lottieAnimation.Paused += OnAnimationPaused;
            _lottieAnimation.Stopped += OnAnimationStopped;
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
                _lottieAnimation.Started += OnAnimationStarted;
                _lottieAnimation.Paused += OnAnimationPaused;
                _lottieAnimation.Stopped += OnAnimationStopped;
            }
            return _lottieAnimation;
        }
        internal void DisposeLottieAnimation()
        {
            if (_lottieAnimation != null)
            {
                _lottieAnimation.Started -= OnAnimationStarted;
                _lottieAnimation.Paused -= OnAnimationPaused;
                _lottieAnimation.Stopped -= OnAnimationStopped;
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

        private void OnAnimationStarted(LottieAnimation animation)
        {
            Started.Invoke(this);
        }
        private void OnAnimationPaused(LottieAnimation animation)
        {
            Paused.Invoke(this);
        }
        private void OnAnimationStopped(LottieAnimation animation)
        {
            Stopped.Invoke(this);
        }
    }
}
