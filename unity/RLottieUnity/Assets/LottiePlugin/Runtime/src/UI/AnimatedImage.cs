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
        public LottieAnimation Animation => _lottieAnimation;
        internal TextAsset AnimationJson => _animationJson;
        internal uint TextureWidth => _textureWidth;
        internal uint TextureHeight => _textureHeight;
        internal LottieAnimation LottieAnimation => _lottieAnimation;
        internal float AnimationSpeed => _animationSpeed;
        internal bool Loop => _loop;
        internal bool StopOnLastFrame => _stopOnLastFrame;

        [SerializeField] private TextAsset _animationJson = null;
        [SerializeField] private string _resourcesPath = string.Empty;
        [SerializeField] private string _jsonFilePath = string.Empty;
        [SerializeField] private RawImage _rawImage = null;
        [SerializeField] private float _animationSpeed = 1f;
        [SerializeField] private uint _textureWidth = 0;
        [SerializeField] private uint _textureHeight = 0;
        [SerializeField] private bool _playOnAwake = true;
        [SerializeField] private bool _loop = true;
        [SerializeField] private bool _stopOnLastFrame = true;
        [SerializeField] private int _targetFps = 30;
        [SerializeField] private int _resolutionDivider = 1;
        [SerializeField] private bool _pauseIfCulled = true;
        [SerializeField] private LottieLogLevel _logLevel = LottieLogLevel.Warning;

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
#if UNITY_EDITOR
            if (UnityEditor.BuildPipeline.isBuildingPlayer)
            {
                return;
            }
#endif
            if (_animationJson == null && string.IsNullOrEmpty(_jsonFilePath))
            {
                return;
            }
            if (_rawImage == null)
            {
                _rawImage = GetComponent<RawImage>();
            }
            CreateIfNeededAndReturnLottieAnimation();
            if (_playOnAwake && Application.isPlaying)
            {
                Play();
            }
            // Play() advances immediately. Render frame zero last so the first
            // presented texture is deterministic while playback remains active.
            _lottieAnimation.DrawOneFrame(0);
        }
        private void OnDestroy()
        {
            DisposeLottieAnimation();
        }

        private void OnDisable()
        {
            if (_renderLottieAnimationCoroutine != null)
            {
                StopCoroutine(_renderLottieAnimationCoroutine);
                _renderLottieAnimationCoroutine = null;
            }
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
        public void LoadFromAnimationJson(string jsonData, uint width, uint height, string resourcesPath = "")
        {
            if (string.IsNullOrWhiteSpace(jsonData))
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
                jsonData,
                resourcesPath,
                width,
                height,
                CreateOptions());
            _rawImage.texture = _lottieAnimation.OutputTexture;
            _lottieAnimation.Started += OnAnimationStarted;
            _lottieAnimation.Paused += OnAnimationPaused;
            _lottieAnimation.Stopped += OnAnimationStopped;
        }

        public void LoadFromAnimationJsonFile(string filePath, uint width, uint height)
        {
            if (string.IsNullOrWhiteSpace(filePath))
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
            _lottieAnimation = LottieAnimation.LoadFromJsonFile(
                filePath,
                width,
                height,
                CreateOptions());
            _rawImage.texture = _lottieAnimation.OutputTexture;
            _lottieAnimation.Started += OnAnimationStarted;
            _lottieAnimation.Paused += OnAnimationPaused;
            _lottieAnimation.Stopped += OnAnimationStopped;
        }

        internal LottieAnimation CreateIfNeededAndReturnLottieAnimation()
        {
            if (_animationJson == null && string.IsNullOrEmpty(_jsonFilePath))
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
                if (_animationJson != null)
                {
                    _lottieAnimation = LottieAnimation.LoadFromJsonData(
                        _animationJson.text,
                        _resourcesPath ?? string.Empty,
                        _textureWidth,
                        _textureHeight,
                        CreateOptions());
                }
                else if (!string.IsNullOrEmpty(_jsonFilePath))
                {
                    _lottieAnimation = LottieAnimation.LoadFromJsonFile(
                        _jsonFilePath,
                        _textureWidth,
                        _textureHeight,
                        CreateOptions());
                }
                _rawImage.texture = _lottieAnimation.OutputTexture;
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
                if (_rawImage != null)
                {
                    _rawImage.texture = null;
                }
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

        private LottieAnimationOptions CreateOptions()
        {
            return new LottieAnimationOptions
            {
                TargetFps = Mathf.Max(1, _targetFps),
                ResolutionDivider = Mathf.Max(1, _resolutionDivider),
                PauseIfCulled = _pauseIfCulled,
                LogLevel = _logLevel,
                VisibilityEvaluator = () =>
                {
                    if (_rawImage == null)
                    {
                        return false;
                    }

                    return _rawImage.isActiveAndEnabled && !_rawImage.canvasRenderer.cull;
                }
            };
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
