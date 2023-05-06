using System;
using System.IO;
using LottiePlugin.Sample.SceneUI.Data;
using UnityEditor;
using UnityEngine;

namespace LottiePlugin.Sample.SceneUI.Editor.Menu
{
    internal static class PopulateLottieAnimations
    {
        [MenuItem("Tools/Populate Lottie Animations")]
        internal static void Add()
        {
            UnityEngine.Object[] selectedObjects = Selection.objects;
            if (selectedObjects.Length != 1)
            {
                Debug.LogWarning("Please select one folder to populate the animations array");
                return;
            }
            UnityEngine.Object obj = selectedObjects[0];
            string path = AssetDatabase.GetAssetPath(obj.GetInstanceID());
            if (!Directory.Exists(path))
            {
                Debug.LogWarning("Please select a folder to populate the animations array");
                return;
            }
            if (!path.StartsWith("Assets/StreamingAssets"))
            {
                Debug.LogWarning("Please select a folder inside of Streaming Assets folder");
                return;
            }
            LottieAnimations lottieAnimations =
                AssetDatabase.LoadAssetAtPath<LottieAnimations>("Assets/_ScriptableObjects/LottieAnimationsArray.asset");
            string[] allFiles = Directory.GetFiles(path, "*.json", SearchOption.AllDirectories);
            string[] finalFilePaths = new string[allFiles.Length];
            Array.Sort(allFiles);
            for (int i = 0; i < allFiles.Length; ++i)
            {
                ReadOnlySpan<char> filePath = allFiles[i].AsSpan();
                const int ASSETS_STREAMINGASSETS_FOLDER_PREFIX_LENGTH = 23;
                const int DOT_JSON_EXTENTION_LENGTH = 5;
                ReadOnlySpan<char> final = filePath.Slice(
                    ASSETS_STREAMINGASSETS_FOLDER_PREFIX_LENGTH,
                    filePath.Length - ASSETS_STREAMINGASSETS_FOLDER_PREFIX_LENGTH - DOT_JSON_EXTENTION_LENGTH);
                finalFilePaths[i] = final.ToString().Replace("\\", "/");//TODO: find a better solution than "Replace"
            }
            lottieAnimations.SetAnimationsEditorCall(finalFilePaths);
            EditorUtility.SetDirty(lottieAnimations);
        }
    }
}
