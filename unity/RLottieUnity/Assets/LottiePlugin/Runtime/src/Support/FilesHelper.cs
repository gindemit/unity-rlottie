using System.IO;
using UnityEngine;

namespace LottiePlugin.Support
{
    public static class FilesHelper
    {
        private const string JSON_EXTENTION = ".json";

        public static void CopyAnimationsJsonsFromStreamingAssetsToPersistentData(string[] animations)
        {
            for (int i = 0; i < animations.Length; ++i)
            {
                string fileName = animations[i] + JSON_EXTENTION;
                string jsonFilePath = Path.Combine(Application.streamingAssetsPath, fileName);
                string targetFilePath = Path.Combine(Application.persistentDataPath, fileName);
                if (!File.Exists(targetFilePath))
                {
                    CopyFileFromStreamingAssetsToPersistentData(jsonFilePath, targetFilePath);
                }
            }
        }
        public static void CopyFileFromStreamingAssetsToPersistentData(string streamingAssetsFilePath, string targetFilePath)
        {
            byte[] file = StreamingAssetsHelper.LoadFileFromStreamingAssets(streamingAssetsFilePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFilePath));
            File.WriteAllBytes(targetFilePath, file);
        }
        public static string[] GetPersistentAnimationsPaths(string[] animations)
        {
            string[] toReturn = new string[animations.Length];
            for (int i = 0; i < animations.Length; ++i)
            {
                toReturn[i] = GetPersistentAnimationPath(animations[i]);
            }
            return toReturn;
        }
        public static string GetPersistentAnimationPath(string animation)
        {
            return Path.Combine(Application.persistentDataPath, animation + JSON_EXTENTION);
        }
    }
}
