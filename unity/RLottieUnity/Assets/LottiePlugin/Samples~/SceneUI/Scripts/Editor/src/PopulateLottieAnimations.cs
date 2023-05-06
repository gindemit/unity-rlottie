using LottiePlugin.Sample.SceneUI.Data;
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace LottiePlugin.Sample.SceneUI.Editor
{
    public class PopulateLottieAnimationsWindow : EditorWindow
    {
        private LottieAnimations lottieAnimations;
        private SerializedObject serializedObject;
        private SerializedProperty lottieAnimationsArray;
        private string folderPath;

        [MenuItem("Tools/Populate Lottie Animations")]
        public static void ShowWindow()
        {
            GetWindow<PopulateLottieAnimationsWindow>("Populate Lottie Animations");
        }

        private void OnEnable()
        {
            if (lottieAnimations != null)
            {
                serializedObject = new SerializedObject(lottieAnimations);
                lottieAnimationsArray = serializedObject.FindProperty("_animations");
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Select Lottie Animations Array", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            lottieAnimations = EditorGUILayout.ObjectField("Lottie Animations Array", lottieAnimations, typeof(LottieAnimations), false) as LottieAnimations;
            if (EditorGUI.EndChangeCheck())
            {
                serializedObject = new SerializedObject(lottieAnimations);
                lottieAnimationsArray = serializedObject.FindProperty("_animations");
            }

            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Select the folder inside StreamingAssets folder", EditorStyles.boldLabel);
            folderPath = EditorGUILayout.TextField("Folder Path", folderPath);
            if (GUILayout.Button("Select Folder"))
            {
                string currentProjectStreamingAssetsFolder = Application.dataPath + "/StreamingAssets";
                string path = EditorUtility.OpenFolderPanel(
                    "Select Lottie Animations Folder",
                    currentProjectStreamingAssetsFolder,
                    "StreamingAssets");
                if (!string.IsNullOrEmpty(path) && path.StartsWith(currentProjectStreamingAssetsFolder))
                {
                    folderPath = "Assets" + path.Substring(Application.dataPath.Length);
                }
                else
                {
                    EditorUtility.DisplayDialog("Error", "Please select a folder inside of Streaming Assets current projects folder folder", "OK");
                }
            }

            if (GUILayout.Button("Populate Lottie Animations"))
            {
                PopulateLottieAnimations();
            }
        }

        private void PopulateLottieAnimations()
        {
            if (lottieAnimations == null)
            {
                EditorUtility.DisplayDialog("Error", "Please select a Lottie Animations Array asset", "OK");
                return;
            }

            if (string.IsNullOrEmpty(folderPath))
            {
                EditorUtility.DisplayDialog("Error", "Please select a folder to populate the animations array", "OK");
                return;
            }
            if (!Directory.Exists(folderPath))
            {
                EditorUtility.DisplayDialog("Error", "Folder does not exist. Please select a valid folder.", "OK");
                return;
            }

            string[] allFiles = Directory.GetFiles(folderPath, "*.json", SearchOption.AllDirectories);
            string[] finalFilePaths = new string[allFiles.Length];
            Array.Sort(allFiles);
            for (int i = 0; i < allFiles.Length; ++i)
            {
                string filePath = allFiles[i];
                const int ASSETS_STREAMINGASSETS_FOLDER_PREFIX_LENGTH = 23;
                const int DOT_JSON_EXTENTION_LENGTH = 5;
                string final = filePath.Substring(
                    ASSETS_STREAMINGASSETS_FOLDER_PREFIX_LENGTH,
                    filePath.Length - ASSETS_STREAMINGASSETS_FOLDER_PREFIX_LENGTH - DOT_JSON_EXTENTION_LENGTH);
                finalFilePaths[i] = final.Replace("\\", "/"); //TODO: find a better solution than "Replace"
            }

            lottieAnimationsArray.ClearArray();
            for (int i = 0; i < finalFilePaths.Length; ++i)
            {
                lottieAnimationsArray.InsertArrayElementAtIndex(i);
                lottieAnimationsArray.GetArrayElementAtIndex(i).stringValue = finalFilePaths[i];
            }

            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(lottieAnimations);
            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("Success!", "Lottie animations asset updated", "OK");
            Selection.activeObject = lottieAnimations;
        }
    }
}
