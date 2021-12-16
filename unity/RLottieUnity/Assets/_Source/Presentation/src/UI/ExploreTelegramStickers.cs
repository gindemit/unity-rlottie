using UnityEngine;

namespace Presentation.UI
{
    internal sealed class ExploreTelegramStickers : MonoBehaviour, System.IDisposable
    {
        private const string TELEGRAM_BOT_TOKEN_PLAYER_PREFS_KEY = "telegram_bot_token_player_prefs_key";

        [SerializeField] private LottieAnimationsPreview _lottieAnimationsPreview;
        [SerializeField] private TMPro.TMP_InputField _tokenInputField;
        [SerializeField] private TMPro.TMP_InputField _stickerPackNameInputField;

        internal void Init()
        {
            string botToken = PlayerPrefs.GetString(TELEGRAM_BOT_TOKEN_PLAYER_PREFS_KEY, string.Empty);
            _tokenInputField.text = botToken;

            _tokenInputField.onValueChanged.AddListener(OnTokenInputFieldValueChanged);
        }
        public void Dispose()
        {
            _tokenInputField.onValueChanged.RemoveListener(OnTokenInputFieldValueChanged);

        }

        private void OnTokenInputFieldValueChanged(string value)
        {
            PlayerPrefs.SetString(TELEGRAM_BOT_TOKEN_PLAYER_PREFS_KEY, value);
        }
    }
}
