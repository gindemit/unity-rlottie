using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Telegram.Bot;
using UnityEngine;

namespace LottiePlugin.Sample.SceneUI.Storage
{
    internal sealed class TelegramStickerStorage : System.IDisposable
    {
        private const string JSON = "json";
        private const string TGS = "tgs";
        private const string JSON_FILE_EXTENTION = "." + JSON;
        private const string TGS_FILE_EXTENTION = "." + TGS;
        private const string ANY_TGS_FILE = "*." + TGS;
        private const string ANY_JSON_FILE = "*." + JSON;

        private readonly TelegramBotClient mTelegramClient;
        private readonly string mTgsFilesDirectoryPath;
        private readonly string mJsonFilesDirectoryPath;

        internal TelegramStickerStorage(string botKey)
        {
            mTelegramClient = new TelegramBotClient(botKey);
            mTgsFilesDirectoryPath = Path.Combine(Application.persistentDataPath, TGS);
            mJsonFilesDirectoryPath = Path.Combine(Application.persistentDataPath, JSON);
        }
        public void Dispose()
        {
            mTelegramClient.CloseAsync();
        }

        /// <summary>
        /// Downloads to local storage telegram stickers by pack name
        /// </summary>
        /// <param name="packName">Name of Telegram sticker pack</param>
        /// <returns>Paths to local tgs files</returns>
        public async Task<string[]> DownloadTelegramStickersPackAsync(string packName)
        {
            try
            {
                Telegram.Bot.Types.StickerSet stickerSet = await mTelegramClient.GetStickerSetAsync(packName);
                string tgsFinalDirectoryPath = Path.Combine(mTgsFilesDirectoryPath, packName);
                Directory.CreateDirectory(tgsFinalDirectoryPath);
                List<string> pathsToTgsFiles = new List<string>(stickerSet.Stickers.Length);
                for (int i = 0; i < stickerSet.Stickers.Length; ++i)
                {
                    Telegram.Bot.Types.Sticker sticker = stickerSet.Stickers[i];
                    if (!sticker.IsAnimated)
                    {
                        continue;
                    }
                    string tgsFilePath = Path.Combine(tgsFinalDirectoryPath, sticker.FileUniqueId + TGS_FILE_EXTENTION);
                    if (!File.Exists(tgsFilePath))
                    {
                        using (FileStream downloadedFile = File.OpenWrite(tgsFilePath))
                        {
                            await mTelegramClient.GetInfoAndDownloadFileAsync(sticker.FileId, downloadedFile);
                        }
                    }
                    pathsToTgsFiles.Add(tgsFilePath);
                }
                return pathsToTgsFiles.ToArray();
            }
            catch (Telegram.Bot.Exceptions.ApiRequestException)
            {
                throw new CanNotFindStickerPackException();
            }
        }
        /// <summary>
        /// Downloads to local storage telegram stickers by pack name if the pack is not yet there
        /// </summary>
        /// <param name="packName">Name of Telegram sticker pack</param>
        /// <returns>Paths to local tgs files</returns>
        public Task<string[]> DownloadTelegramStickersPackIfNecessaryAsync(string packName)
        {
            string tgsFinalDirectoryPath = Path.Combine(mTgsFilesDirectoryPath, packName);
            if (Directory.Exists(tgsFinalDirectoryPath))
            {
                string[] pathsToTgsFiles = Directory.GetFiles(
                    tgsFinalDirectoryPath,
                    ANY_TGS_FILE,
                    SearchOption.TopDirectoryOnly);
                if (pathsToTgsFiles.Length > 0)
                {
                    return Task.FromResult(pathsToTgsFiles);
                }
            }
            return DownloadTelegramStickersPackAsync(packName);
        }
        /// <summary>
        /// Unpacks already downloaded tgs files to json local files
        /// </summary>
        /// <param name="packName">Name of Telegram sticker pack</param>
        /// <param name="tgsLocalFilesPaths">Paths to local tgs files</param>
        /// <returns>Paths to local json files</returns>
        public async Task<string[]> UnpackLocalTgsFilesToJsonFilesAsync(string packName, string[] tgsLocalFilesPaths)
        {
            string jsonFinalDirectoryPath = Path.Combine(mJsonFilesDirectoryPath, packName);
            Directory.CreateDirectory(jsonFinalDirectoryPath);
            string[] jsonFiles = new string[tgsLocalFilesPaths.Length];
            for (int i = 0; i < tgsLocalFilesPaths.Length; ++i)
            {
                string tgsFilePath = tgsLocalFilesPaths[i];
                string stickerUniqueId = Path.GetFileNameWithoutExtension(tgsFilePath);
                string jsonFilePath = Path.Combine(jsonFinalDirectoryPath, stickerUniqueId + JSON_FILE_EXTENTION);
                using (FileStream downloadedFile = File.OpenRead(tgsFilePath))
                {
                    using (FileStream outputFileStream = File.OpenWrite(jsonFilePath))
                    {
                        using (GZipStream decompressor = new GZipStream(downloadedFile, CompressionMode.Decompress))
                        {
                            decompressor.CopyTo(outputFileStream);
                            await decompressor.FlushAsync();
                            jsonFiles[i] = jsonFilePath;
                        }
                    }
                }
            }
            return jsonFiles;
        }
        /// <summary>
        /// Unpacks already downloaded tgs files to json local files if the json files does not exist
        /// </summary>
        /// <param name="packName">Name of Telegram sticker pack</param>
        /// <param name="tgsLocalFilesPaths">Paths to local tgs files</param>
        /// <returns>Paths to local json files</returns>
        public Task<string[]> UnpackLocalTgsFilesToJsonFilesIfNecessaryAsync(string packName, string[] tgsLocalFilesPaths)
        {
            string jsonFinalDirectoryPath = Path.Combine(mJsonFilesDirectoryPath, packName);
            if (Directory.Exists(jsonFinalDirectoryPath))
            {
                string[] pathsToJsonFiles = Directory.GetFiles(
                    jsonFinalDirectoryPath,
                    ANY_TGS_FILE,
                    SearchOption.TopDirectoryOnly);
                if (pathsToJsonFiles.Length > 0)
                {
                    return Task.FromResult(pathsToJsonFiles);
                }
            }
            return UnpackLocalTgsFilesToJsonFilesAsync(packName, tgsLocalFilesPaths);
        }
    }
}
