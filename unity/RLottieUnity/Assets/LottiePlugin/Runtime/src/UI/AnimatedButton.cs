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
        internal TextAsset AnimationJson => _animationJson;

        internal RawImage RawImage => _rawImage;

        [SerializeField] private TextAsset _animationJson;
        [SerializeField] private float _animationSpeed = 1f;
        [SerializeField] private uint _textureWidth;
        [SerializeField] private uint _textureHeight;
        [FormerlySerializedAs("_graphic")] 
        [SerializeField] private RawImage _rawImage;
        [SerializeField] private bool _ignoreInputWhileAnimating = true;
        [SerializeField] private State[] _states;
        [SerializeField] private ButtonClickedEvent _onClick = new ButtonClickedEvent();

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
            if (_animationJson == null)
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
            if (_animationJson == null)
            {
                return null;
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
            _rawImage.texture = _lottieAnimation.Texture;
        }
        internal void DisposeLottieAnimation()
        {
            if (_lottieAnimation != null)
            {
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
                _lottieAnimation.Update(_animationSpeed);
                if (_lottieAnimation.CurrentFrame == 0)
                {
                    _updateAnimationCoroutine = null;
                    yield break;
                }
            }
            if (_currentStateIndex == 0)
            {
                _lottieAnimation.Stop();
            }
            _updateAnimationCoroutine = null;
        }
    }
}
