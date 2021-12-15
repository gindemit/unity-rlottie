using UnityEngine;

namespace Presentation.UI
{
    internal sealed class ExploreTelegramStickers : MonoBehaviour, System.IDisposable
    {
        [SerializeField] private LottieAnimationsPreview _lottieAnimationsPreview;
        [SerializeField] private TMPro.TMP_InputField _tokenInputField;
        [SerializeField] private TMPro.TMP_InputField _stickerPackNameInputField;

        internal void Init()
        {

        }
        public void Dispose()
        {

        }
    }
}
