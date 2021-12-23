using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
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

        internal TextAsset AnimationJson => _animationJson;
        internal uint TextureWidth => _textureWidth;
        internal uint TextureHeight => _textureHeight;

        internal Graphic Graphic => _graphic;
        internal State[] States => _states;

        [SerializeField] private TextAsset _animationJson;
        [SerializeField] private float _animationSpeed = 1f;
        [SerializeField] private uint _textureWidth;
        [SerializeField] private uint _textureHeight;
        [SerializeField] private Graphic _graphic;
        [SerializeField] private bool _ignoreInputWhileAnimating = true;
        [SerializeField] private State[] _states;
        [SerializeField] private ButtonClickedEvent _onClick = new ButtonClickedEvent();

        private int _currentStateIndex;
        private LottieAnimation _lottieAnimation;
        private Coroutine _updateAnimationCoroutine;

        protected override void Start()
        {
            base.Start();
            _lottieAnimation = LottieAnimation.LoadFromJsonData(
                _animationJson.text,
                string.Empty,
                _textureWidth,
                _textureHeight);
            _lottieAnimation.DrawOneFrame(_states[0].FrameNumber);
            ((RawImage)_graphic).texture = _lottieAnimation.Texture;
        }
        protected override void OnDestroy()
        {
            base.OnDestroy();
            _lottieAnimation?.Dispose();
            _lottieAnimation = null;
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
                _lottieAnimation.Update(_animationSpeed);
                if (_lottieAnimation.CurrentFrame == 0)
                {
                    _updateAnimationCoroutine = null;
                    yield break;
                }
                yield return null;
            }
            if (_currentStateIndex == 0)
            {
                _lottieAnimation.Stop();
            }
            _updateAnimationCoroutine = null;
        }
    }
}
