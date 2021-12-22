using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Presentation.UI
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

        internal void Init(string[] animationPaths, uint textureSize)
        {
            int animationsCount = animationPaths.Length;
            _noItemsText.gameObject.SetActive(animationsCount == 0);
            if (animationsCount == 0)
            {
                return;
            }
            _animationPreviews = new List<AnimationPreview>(animationsCount);
            Vector2 viewPortSize = _scrollRectViewPort.rect.size;
            float oneItemSize = (viewPortSize.x / _columns) - (_gabBetweenItems * _columns);
            for (int i = 0; i < animationsCount; ++i)
            {
                string animation = animationPaths[i];
                AnimationPreview animationPreview = Instantiate(_animationPreviewPrefab, _scrollRectContent);
                animationPreview.Init(animation, textureSize, textureSize);
                animationPreview.RectTransform.anchoredPosition = new Vector3(
                    i % _columns * oneItemSize + _gabBetweenItems,
                    -i / _columns * oneItemSize - _gabBetweenItems);
                animationPreview.RectTransform.sizeDelta = new Vector2(oneItemSize, oneItemSize);
                _animationPreviews.Add(animationPreview);
            }
            int rows = Mathf.CeilToInt((float)animationsCount / _columns);
            _scrollRectContent.sizeDelta = new Vector2(
                _scrollRectContent.sizeDelta.x,
                (rows * oneItemSize) +
                (rows * _gabBetweenItems));
        }
        public void Dispose()
        {
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
        private void Update()
        {
            if (_animationPreviews == null)
            {
                return;
            }
            for (int i = 0; i < _animationPreviews.Count; ++i)
            {
                _animationPreviews[i].DoUpdateAsync();
            }
        }
        private void LateUpdate()
        {
            if (_animationPreviews == null)
            {
                return;
            }
            for (int i = 0; i < _animationPreviews.Count; ++i)
            {
                _animationPreviews[i].DoDrawOneFrameAsyncGetResult();
            }
        }
    }
}
