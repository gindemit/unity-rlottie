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

        private Storage.TelegramStickerStorage _storage;
        private string _lastLoadedStickerPackName;

        internal void Init()
        {
            string botToken = PlayerPrefs.GetString(TELEGRAM_BOT_TOKEN_PLAYER_PREFS_KEY, string.Empty);
            _tokenInputField.text = botToken;
            CreateStorageIfTokenIsNotEmpty();

            _tokenInputField.onValueChanged.AddListener(OnTokenInputFieldValueChanged);
            _stickerPackNameInputField.onSubmit.AddListener(OnStickerPackNameInputFieldSubmit);
            _findButton.onClick.AddListener(OnFindButtonClick);
        }
        public void Dispose()
        {
            DisposeStorageIfNecessary();
            _lastLoadedStickerPackName = null;
            _tokenInputField.onValueChanged.RemoveListener(OnTokenInputFieldValueChanged);
            _stickerPackNameInputField.onSubmit.RemoveListener(OnStickerPackNameInputFieldSubmit);
            _findButton.onClick.RemoveListener(OnFindButtonClick);
        }

        private void OnTokenInputFieldValueChanged(string value)
        {
            PlayerPrefs.SetString(TELEGRAM_BOT_TOKEN_PLAYER_PREFS_KEY, value);
            CreateStorageIfTokenIsNotEmpty();
        }
        private void OnStickerPackNameInputFieldSubmit(string value)
        {
            Debug.Log(value);
        }
        private void OnFindButtonClick()
        {
            Debug.Log("On Find Button Click");
        }
        private void CreateStorageIfTokenIsNotEmpty()
        {
            string token = _tokenInputField.text;
            if (string.IsNullOrWhiteSpace(token))
            {
                return;
            }
            DisposeStorageIfNecessary();
            _storage = new Storage.TelegramStickerStorage(token);
        }
        private void DisposeStorageIfNecessary()
        {
            if (_storage != null)
            {
                _storage.Dispose();
                _storage = null;
            }
        }
        private void LoadTelegramStickersIfNecessary()
        {
            string stickerPackToLoad = _stickerPackNameInputField.text;
            if (_lastLoadedStickerPackName == stickerPackToLoad)
            {
                Debug.LogWarning("Trying to load already loaded Sticker pack " + stickerPackToLoad);
                return;
            }
            

        }
    }
}
