using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Telegram.Bot;
using UnityEngine;

namespace Presentation.Storage
{
    internal sealed class TelegramStickerStorage : System.IDisposable
    {
        private readonly TelegramBotClient mTelegramClient;
        private readonly string mTgsFilesDirectoryPath;
        private readonly string mJsonFilesDirectoryPath;

        internal TelegramStickerStorage(string botKey)
        {
            mTelegramClient = new Telegram.Bot.TelegramBotClient(botKey);
            mTgsFilesDirectoryPath = Path.Combine(Application.persistentDataPath, "tgs");
            mJsonFilesDirectoryPath = Path.Combine(Application.persistentDataPath, "json");
        }
        public void Dispose()
        {
            mTelegramClient.CloseAsync();
        }

        /// <summary>
        /// Downloads to local storage telegram stickers by pack name
        /// </summary>
        /// <param name="packName">Name of Telegram sticker pack</param>
        /// <returns>Paths to local downloaded tgs files</returns>
        public async Task<string[]> DownloadTelegramStickersPackAsync(string packName)
        {
            Telegram.Bot.Types.StickerSet stickerSet = await mTelegramClient.GetStickerSetAsync(packName);
            string tgsFinalDirectoryPath = Path.Combine(mTgsFilesDirectoryPath, packName);
            Directory.CreateDirectory(tgsFinalDirectoryPath);
            string[] pathsToTgsFiles = new string[stickerSet.Stickers.Length];
            for (int i = 0; i < stickerSet.Stickers.Length; ++i)
            {
                Telegram.Bot.Types.Sticker sticker = stickerSet.Stickers[i];
                string tgsFilePath = Path.Combine(tgsFinalDirectoryPath, sticker.FileUniqueId + ".tgs");
                if (!File.Exists(tgsFilePath))
                {
                    using FileStream downloadedFile = File.OpenWrite(tgsFilePath);
                    await mTelegramClient.GetInfoAndDownloadFileAsync(sticker.FileId, downloadedFile);
                }
                pathsToTgsFiles[i] = tgsFilePath;
            }
            return pathsToTgsFiles;
        }
        /// <summary>
        /// Unpacks already downloaded tgs files to json local files
        /// </summary>
        /// <param name="packName">Name of Telegram sticker pack</param>
        /// <param name="tgsLocalFilesPaths">Paths to local tgs files</param>
        /// <returns></returns>
        public async Task<string[]> UnpackLocalTgsFilesToJsonFiles(string packName, string[] tgsLocalFilesPaths)
        {
            string tgsFinalDirectoryPath = Path.Combine(mTgsFilesDirectoryPath, packName);
            string jsonFinalDirectoryPath = Path.Combine(mJsonFilesDirectoryPath, packName);
            Directory.CreateDirectory(jsonFinalDirectoryPath);
            string[] jsonFiles = new string[tgsLocalFilesPaths.Length];
            for (int i = 0; i < tgsLocalFilesPaths.Length; ++i)
            {
                string tgsFilePath = tgsLocalFilesPaths[i];
                string stickerUniqueId = Path.GetFileNameWithoutExtension(tgsFilePath);
                string jsonFilePath = Path.Combine(jsonFinalDirectoryPath, stickerUniqueId + ".json");
                using FileStream downloadedFile = File.OpenRead(tgsFilePath);
                using FileStream outputFileStream = File.OpenWrite(jsonFilePath);
                using GZipStream decompressor = new GZipStream(downloadedFile, CompressionMode.Decompress);
                decompressor.CopyTo(outputFileStream);
                await decompressor.FlushAsync();
                jsonFiles[i] = jsonFilePath;
            }
            return jsonFiles;
        }
    }
}
