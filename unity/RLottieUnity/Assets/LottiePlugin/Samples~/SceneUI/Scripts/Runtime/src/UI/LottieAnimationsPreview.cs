using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LottiePlugin.Sample.SceneUI.UI
{
    internal sealed class LottieAnimationsPreview : MonoBehaviour, System.IDisposable
    {
        [SerializeField] private AnimationPreview _animationPreviewPrefab;
        [SerializeField] private ScrollRect _scrollRect;
        [SerializeField] private RectTransform _scrollRectViewPort;
        [SerializeField] private RectTransform _scrollRectContent;
        [SerializeField] private TMPro.TextMeshProUGUI _noItemsText;
        [SerializeField] private int _columns;
        [SerializeField] private int _gabBetweenItems;

        private List<AnimationPreview> _animationPreviews;
        private float _oneItemSize;
        private float _viewPortSize;
        private WaitForEndOfFrame _waitForEndOfFrame;
        private Coroutine _coroutine;

        internal void Init(string[] animationPaths, uint textureSize)
        {
            int animationsCount = animationPaths.Length;
            _waitForEndOfFrame = new WaitForEndOfFrame();
            _noItemsText.gameObject.SetActive(animationsCount == 0);
            if (animationsCount == 0)
            {
                return;
            }
            _animationPreviews = new List<AnimationPreview>(animationsCount);
            Vector2 viewPortSize = _scrollRectViewPort.rect.size;
            _viewPortSize = viewPortSize.y;
            _oneItemSize = (viewPortSize.x / _columns) - (_gabBetweenItems * _columns);
            for (int i = 0; i < animationsCount; ++i)
            {
                string animation = animationPaths[i];
                AnimationPreview animationPreview = Instantiate(_animationPreviewPrefab, _scrollRectContent);
                animationPreview.InitFromFile(animation, textureSize, textureSize);
                animationPreview.RectTransform.anchoredPosition = new Vector3(
                    i % _columns * _oneItemSize + _gabBetweenItems,
                    -i / _columns * _oneItemSize - _gabBetweenItems);
                animationPreview.RectTransform.sizeDelta = new Vector2(_oneItemSize, _oneItemSize);
                _animationPreviews.Add(animationPreview);
            }
            int rows = Mathf.CeilToInt((float)animationsCount / _columns);
            _scrollRectContent.sizeDelta = new Vector2(
                _scrollRectContent.sizeDelta.x,
                (rows * _oneItemSize) +
                (rows * _gabBetweenItems));
            _scrollRect.onValueChanged.AddListener(OnScrollRectValueChanged);
            RestartCoroutine();
        }
        public void Dispose()
        {
            StopAllCoroutines();
            _coroutine = null;
            _scrollRect.onValueChanged.RemoveListener(OnScrollRectValueChanged);
            if (_animationPreviews == null)
            {
                return;
            }
            for (int i = 0; i < _animationPreviews.Count; ++i)
            {
                _animationPreviews[i].Dispose();
            }
            _animationPreviews.Clear();
        }
        private void RestartCoroutine()
        {
            if (_coroutine != null)
            {
                StopCoroutine(_coroutine);
            }
            _coroutine = StartCoroutine(RenderLottieAnimationCoroutine());
        }
        private void OnEnable()
        {
            RestartCoroutine();
        }

        private void OnScrollRectValueChanged(Vector2 scrollPosition)
        {
            float bottom = _scrollRectContent.anchoredPosition.y + _viewPortSize;
            float top = bottom - _viewPortSize;

            for (int i = 0; i < _animationPreviews.Count; ++i)
            {
                AnimationPreview animationPreview = _animationPreviews[i];
                float previewPosition = -animationPreview.RectTransform.anchoredPosition.y;

                if (previewPosition > bottom || previewPosition + _oneItemSize < top)
                {
                    animationPreview.DisableAnimation();
                }
                else
                {
                    animationPreview.EnableAnimation();
                }
            }
        }
        private IEnumerator RenderLottieAnimationCoroutine()
        {
            while (true)
            {
                yield return _waitForEndOfFrame;
                if (_animationPreviews != null)
                {
                    for (int i = 0; i < _animationPreviews.Count; ++i)
                    {
                        _animationPreviews[i].DoUpdate();
                    }
                }
            }
        }
    }
}
