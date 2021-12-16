using UnityEngine;

namespace Presentation.UI
{
    internal sealed class AnimationsHomeScreen : MonoBehaviour, System.IDisposable
    {
        [SerializeField] private LottieAnimationsPreview _lottieAnimationsPreview;
        [SerializeField] private TMPro.TMP_Dropdown _textureSizeDropdown;

        private string[] _animationPaths;

        internal void Init(string[] animationPaths)
        {
            _animationPaths = animationPaths;
            _textureSizeDropdown.onValueChanged.AddListener(OnTextureSizeDropdownValueChanged);
            OnTextureSizeDropdownValueChanged(_textureSizeDropdown.value);
        }
        public void Dispose()
        {
            _textureSizeDropdown.onValueChanged.RemoveListener(OnTextureSizeDropdownValueChanged);
            _lottieAnimationsPreview.Dispose();
        }

        private void OnTextureSizeDropdownValueChanged(int index)
        {
            string value = _textureSizeDropdown.options[index].text;
            if (!uint.TryParse(value, out uint textureSize))
            {
                Debug.LogError($"Can not parse the texture size dropdown value: {value} at index {index.ToString()}");
                return;
            }
            _lottieAnimationsPreview.Dispose();
            _lottieAnimationsPreview.Init(_animationPaths, textureSize);
        }
    }
}
