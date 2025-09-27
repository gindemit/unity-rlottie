#if UNITY_EDITOR
using LSCore;
using UnityEditor;

[CustomEditor(typeof(LottieImage), true)]
[CanEditMultipleObjects]
public class LottieImageEditor : LSRawImageEditor
{
    protected SerializedProperty manager;
    protected override void OnEnable()
    {
        base.OnEnable();
        manager = serializedObject.FindProperty("manager");
    }

    public override void OnInspectorGUI()
    {
        EditorGUI.BeginChangeCheck();
        base.OnInspectorGUI();
        EditorGUILayout.PropertyField(manager);
        if (EditorGUI.EndChangeCheck())
        {
            serializedObject.ApplyModifiedProperties();
        }
    }
}
#endif