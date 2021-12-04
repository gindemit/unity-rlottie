using System.IO;
using UnityEngine;

namespace Presentation.Utility
{
    internal static class FilesHelper
    {
        private const string JSON_EXTENTION = ".json";

        internal static void CopyAnimationsJsonsFromStreamingAssetsToPersistentData(string[] animations)
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
        internal static void CopyFileFromStreamingAssetsToPersistentData(string streamingAssetsFilePath, string targetFilePath)
        {
            byte[] file = Support.StreamingAssets.StreamingAssetsHelper.LoadFileFromStreamingAssets(streamingAssetsFilePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFilePath));
            File.WriteAllBytes(targetFilePath, file);
        }
        internal static string[] GetPersistentAnimationsPaths(string[] animations)
        {
            string[] toReturn = new string[animations.Length];
            for (int i = 0; i < animations.Length; ++i)
            {
                toReturn[i] = GetPersistentAnimationPath(animations[i]);
            }
            return toReturn;
        }
        internal static string GetPersistentAnimationPath(string animation)
        {
            return Path.Combine(Application.persistentDataPath, animation + JSON_EXTENTION);
        }
    }
}
