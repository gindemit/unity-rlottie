using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace LottiePlugin.UI
{
    public sealed class AnimatedButton : Selectable, IPointerClickHandler, ISubmitHandler
    {
        [System.Serializable]
        public struct State
        {
            public string Name;
            public int FrameNumber;
        }
        [System.Serializable]
        public class ButtonClickedEvent : UnityEngine.Events.UnityEvent<int, State> { }

        public ButtonClickedEvent OnClick => _onClick;

        public Transform Transform { get; private set; }
        public LottieAnimation Animation => _lottieAnimation;
        internal TextAsset AnimationJson => _animationJson;

        internal RawImage RawImage => _rawImage;

        [SerializeField] private TextAsset _animationJson = null;
        [SerializeField] private string _resourcesPath = string.Empty;
        [SerializeField] private string _jsonFilePath = string.Empty;
        [SerializeField] private float _animationSpeed = 1f;
        [SerializeField] private uint _textureWidth = 0;
        [SerializeField] private uint _textureHeight = 0;
        [FormerlySerializedAs("_graphic")] 
        [SerializeField] private RawImage _rawImage = null;
        [SerializeField] private bool _ignoreInputWhileAnimating = true;
        [SerializeField] private State[] _states = null;
        [SerializeField] private ButtonClickedEvent _onClick = new ButtonClickedEvent();
        [SerializeField] private int _targetFps = 30;
        [SerializeField] private int _resolutionDivider = 1;
        [SerializeField] private bool _pauseIfCulled = true;
        [SerializeField] private LottieLogLevel _logLevel = LottieLogLevel.Warning;

        private int _currentStateIndex;
        private LottieAnimation _lottieAnimation;
        private Coroutine _updateAnimationCoroutine;
        private WaitForEndOfFrame _waitForEndOfFrame;

        protected override void Awake()
        {
            Transform = transform;
            _waitForEndOfFrame = new WaitForEndOfFrame();
        }

        protected override void Start()
        {
            base.Start();
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
            CreateIfNeededAndReturnLottieAnimation();
            _lottieAnimation.DrawOneFrame(_states[0].FrameNumber);
        }
        protected override void OnDestroy()
        {
            base.OnDestroy();
            DisposeLottieAnimation();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (_updateAnimationCoroutine != null)
            {
                StopCoroutine(_updateAnimationCoroutine);
                _updateAnimationCoroutine = null;
            }
        }
        public void ResetState()
        {
            _currentStateIndex = 0;
            _lottieAnimation?.DrawOneFrame(_states[0].FrameNumber);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
                return;
            Press();
        }
        public void OnSubmit(BaseEventData eventData)
        {
            Press();
        }
        internal LottieAnimation CreateIfNeededAndReturnLottieAnimation()
        {
            if (_animationJson == null && string.IsNullOrEmpty(_jsonFilePath))
            {
                return null;
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
                SetTextureToTheTargetRawImage();
            }
            return _lottieAnimation;
        }
        internal void SetTextureToTheTargetRawImage()
        {
            if (_lottieAnimation == null)
            {
                return;
            }
            _rawImage.texture = _lottieAnimation.OutputTexture;
        }
        internal void DisposeLottieAnimation()
        {
            if (_lottieAnimation != null)
            {
                if (_rawImage != null)
                {
                    _rawImage.texture = null;
                }
                _lottieAnimation.Dispose();
                _lottieAnimation = null;
            }
        }

        private void Press()
        {
            if (!IsActive() ||
                !IsInteractable() ||
                (_updateAnimationCoroutine != null && _ignoreInputWhileAnimating))
            {
                return;
            }
            _onClick.Invoke(_currentStateIndex, _states[_currentStateIndex]);
            if (_updateAnimationCoroutine != null)
            {
                StopCoroutine(_updateAnimationCoroutine);
            }
            _currentStateIndex++;
            if (_currentStateIndex >= _states.Length)
            {
                _currentStateIndex = 0;
            }
            _updateAnimationCoroutine = StartCoroutine(AnimateToNextState());
        }
        private IEnumerator AnimateToNextState()
        {
            State nextState = _states[_currentStateIndex];
            _lottieAnimation.Play();
            while (
                (_currentStateIndex == 0 &&
                _lottieAnimation.CurrentFrame <= _lottieAnimation.TotalFramesCount) ||
                _lottieAnimation.CurrentFrame < nextState.FrameNumber)
            {
                yield return _waitForEndOfFrame;
                _lottieAnimation.UpdateAsync(_animationSpeed);
                if (_lottieAnimation.CurrentFrame == 0)
                {
                    break;
                }
            }
            while (_lottieAnimation.AsyncDrawPending)
            {
                yield return _waitForEndOfFrame;
                _lottieAnimation.DrawOneFrameAsyncGetResult();
            }
            if (_currentStateIndex == 0)
            {
                _lottieAnimation.Stop();
            }
            _updateAnimationCoroutine = null;
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
    }
}
