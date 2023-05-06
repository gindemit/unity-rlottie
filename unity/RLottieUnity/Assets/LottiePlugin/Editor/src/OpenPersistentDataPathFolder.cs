using UnityEditor;
using UnityEngine;

namespace MyEditor.Menu
{
    internal static class OpenPersistentDataPathFolder
    {
        [MenuItem("Tools/Open Persistent Data Folder")]
        internal static void OpenPersistentDataFolder()
        {
            EditorUtility.RevealInFinder(Application.persistentDataPath);
        }
    }
}