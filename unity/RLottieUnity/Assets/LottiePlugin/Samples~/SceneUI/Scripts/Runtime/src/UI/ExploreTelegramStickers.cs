using UnityEngine;
using UnityEngine.UI;

namespace LottiePlugin.Sample.SceneUI.UI
{
    internal sealed class ExploreTelegramStickers : MonoBehaviour, System.IDisposable
    {
        private const string TELEGRAM_BOT_TOKEN_PLAYER_PREFS_KEY = "telegram_bot_token_player_prefs_key";

        [SerializeField] private LottieAnimationsPreview _lottieAnimationsPreview;
        [SerializeField] private TMPro.TMP_InputField _tokenInputField;
        [SerializeField] private TMPro.TMP_InputField _stickerPackNameInputField;
        [SerializeField] private Button _findButton;
        [SerializeField] private AnimationPreview _loadingAnimation;
        [SerializeField] private AnimationPreview _noDataFoundAnimation;

        private Storage.TelegramStickerStorage _storage;
        private string _lastLoadedStickerPackName;

        internal void Init()
        {
            string botToken = PlayerPrefs.GetString(TELEGRAM_BOT_TOKEN_PLAYER_PREFS_KEY, string.Empty);
            _tokenInputField.text = botToken;
            CreateStorageIfTokenIsNotEmpty();
            _loadingAnimation.InitFromData(256, 256);
            _loadingAnimation.gameObject.SetActive(false);
            _noDataFoundAnimation.InitFromData(256, 256);
            _noDataFoundAnimation.gameObject.SetActive(false);

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

        private void Update()
        {
            if (_loadingAnimation.gameObject.activeInHierarchy)
            {
                _loadingAnimation.DoUpdate();
            }
            if (_noDataFoundAnimation.gameObject.activeInHierarchy)
            {
                _noDataFoundAnimation.DoUpdate();
            }
        }

        private void OnTokenInputFieldValueChanged(string value)
        {
            PlayerPrefs.SetString(TELEGRAM_BOT_TOKEN_PLAYER_PREFS_KEY, value);
            CreateStorageIfTokenIsNotEmpty();
        }
        private void OnStickerPackNameInputFieldSubmit(string value)
        {
            LoadTelegramStickersIfNecessary();
        }
        private void OnFindButtonClick()
        {
            LoadTelegramStickersIfNecessary();
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
        private async void LoadTelegramStickersIfNecessary()
        {
            string stickerPackToLoad = _stickerPackNameInputField.text;
            _noDataFoundAnimation.gameObject.SetActive(false);
            if (_lastLoadedStickerPackName == stickerPackToLoad)
            {
                Debug.LogWarning("Trying to load already loaded Sticker pack " + stickerPackToLoad);
                return;
            }
            if (_loadingAnimation.isActiveAndEnabled)
            {
                Debug.LogWarning("There is already a find operation in progress, ignoring this request");
                return;
            }
            try
            {
                _noDataFoundAnimation.gameObject.SetActive(false);
                _loadingAnimation.gameObject.SetActive(true);
                string[] stickerTgsPaths =
                    await _storage.DownloadTelegramStickersPackIfNecessaryAsync(stickerPackToLoad);
                string[] stickerJsonPaths =
                    await _storage.UnpackLocalTgsFilesToJsonFilesIfNecessaryAsync(stickerPackToLoad, stickerTgsPaths);
                _lottieAnimationsPreview.Dispose();
                _lottieAnimationsPreview.Init(stickerJsonPaths, 128);
                _loadingAnimation.gameObject.SetActive(false);
                _lastLoadedStickerPackName = stickerPackToLoad;
            }
            catch (Storage.CanNotFindStickerPackException)
            {
                _noDataFoundAnimation.gameObject.SetActive(true);
                _loadingAnimation.gameObject.SetActive(false);
            }
        }
    }
}
