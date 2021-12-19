using UnityEngine;
using UnityEngine.UI;

namespace Presentation.UI
{
    internal sealed class ExploreTelegramStickers : MonoBehaviour, System.IDisposable
    {
        private const string TELEGRAM_BOT_TOKEN_PLAYER_PREFS_KEY = "telegram_bot_token_player_prefs_key";

        [SerializeField] private LottieAnimationsPreview _lottieAnimationsPreview;
        [SerializeField] private TMPro.TMP_InputField _tokenInputField;
        [SerializeField] private TMPro.TMP_InputField _stickerPackNameInputField;
        [SerializeField] private Button _findButton;

        internal void Init()
        {
            string botToken = PlayerPrefs.GetString(TELEGRAM_BOT_TOKEN_PLAYER_PREFS_KEY, string.Empty);
            _tokenInputField.text = botToken;

            _tokenInputField.onValueChanged.AddListener(OnTokenInputFieldValueChanged);
            _stickerPackNameInputField.onSubmit.AddListener(OnStickerPackNameInputFieldSubmit);
            _findButton.onClick.AddListener(OnFindButtonClick);
        }
        public void Dispose()
        {
            _tokenInputField.onValueChanged.RemoveListener(OnTokenInputFieldValueChanged);
            _stickerPackNameInputField.onSubmit.RemoveListener(OnStickerPackNameInputFieldSubmit);
            _findButton.onClick.RemoveListener(OnFindButtonClick);
        }

        private void OnTokenInputFieldValueChanged(string value)
        {
            PlayerPrefs.SetString(TELEGRAM_BOT_TOKEN_PLAYER_PREFS_KEY, value);
        }
        private void OnStickerPackNameInputFieldSubmit(string value)
        {
            Debug.Log(value);
        }
        private void OnFindButtonClick()
        {
            Debug.Log("On Find Button Click");
        }
    }
}
