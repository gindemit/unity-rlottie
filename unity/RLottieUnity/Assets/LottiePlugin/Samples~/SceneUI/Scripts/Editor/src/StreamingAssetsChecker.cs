using UnityEditor;
using UnityEngine;
using System.IO;
using System.Linq;

namespace LottiePlugin.Sample.SceneUI.Editor.Menu
{
    internal static class StreamingAssetsChecker
    {
        // Define the file name to search for
        private const string FileNameToSearch = "alright_sticker";
        private const string DontAskToCopyFiles = "LOTTIE_PLUGIN_COPY_ASSETS_DONT_ASK";


        [InitializeOnLoadMethod]
        internal static void CheckIfStreaminAssetsContainLottieFiles()
        {
            string streamingAssetsPath = "Assets/StreamingAssets/LottieSamples/";
            if (Directory.Exists(streamingAssetsPath))
            {
                return;
            }
            if (EditorPrefs.HasKey(DontAskToCopyFiles))
            {
                return;
            }
            int dialogResult = EditorUtility.DisplayDialogComplex(
                "Missing lottie animations",
                "Can not find StreamingAssets folder with lottie animations.\n" +
                "Copy the animations from Sample folder?",
                "Copy",
                "Cancel",
                "Don't ask anymore");
            if (dialogResult == 1 || dialogResult == 2)
            {
                Debug.LogWarning("User cancelled the copy of the sample Lottie animations to StreamingAssets folder");
                if (dialogResult == 2)
                {
                    EditorPrefs.SetBool(DontAskToCopyFiles, true);
                }
                return;
            }
            string[] foundFiles = AssetDatabase.FindAssets(FileNameToSearch + " t:Object");
            if (foundFiles.Length == 0)
            {
                Debug.LogError($"File not found: {FileNameToSearch}");
                return;
            }
            string sourceFileAssetPath = AssetDatabase.GUIDToAssetPath(foundFiles[0]);
            string sourceFolderPath = Path.GetDirectoryName(sourceFileAssetPath);

            if (!Directory.Exists(streamingAssetsPath))
            {
                AssetDatabase.CreateFolder("Assets", "StreamingAssets");
                AssetDatabase.CreateFolder("Assets/StreamingAssets", "LottieSamples");
            }
            CopyFilesRecursively(sourceFolderPath, streamingAssetsPath);
            AssetDatabase.Refresh();
        }

        private static void CopyFilesRecursively(string sourcePath, string destinationPath)
        {
            // Get the subdirectories for the specified directory.
            string[] sourceSubDirs = AssetDatabase.GetSubFolders(sourcePath);

            // Get the files in the source directory and copy them to the destination folder
            string[] sourceFiles = AssetDatabase.FindAssets("t:Object", new[] { sourcePath });
            sourceFiles = sourceFiles.Where(guid =>
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                string assetParentPath = Path.GetDirectoryName(assetPath);
                return assetParentPath == sourcePath;
            }).ToArray();
            foreach (string guid in sourceFiles)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                string fileName = Path.GetFileName(assetPath);
                string destinationFilePath = Path.Combine(destinationPath, fileName);

                AssetDatabase.CopyAsset(assetPath, destinationFilePath);
            }

            // Copy the subdirectories by recursively calling the CopyFilesRecursively method
            foreach (string subdir in sourceSubDirs)
            {
                string subdirName = Path.GetFileName(subdir);
                string destinationSubDirPath = Path.Combine(destinationPath, subdirName);

                CopyFilesRecursively(subdir, destinationSubDirPath);
            }
        }
    }
}
