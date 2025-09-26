#if UNITY_EDITOR
using LSCore;
using LSCore.Extensions;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(LottieRenderer), false)]
[CanEditMultipleObjects]
public class LottieRendererEditor : Editor
{
    protected SerializedProperty manager;
    protected SerializedProperty rotateId;
    protected SerializedProperty pixelsPerUnit;
    protected SerializedProperty flip;
    protected SerializedProperty color;
    protected SerializedProperty material;
    protected LottieRenderer renderer;
    
    private void OnEnable()
    {
        renderer = target as LottieRenderer;
        manager = serializedObject.FindProperty("manager");
        rotateId = serializedObject.FindProperty("rotateId");
        pixelsPerUnit = serializedObject.FindProperty("pixelsPerUnit");
        flip = serializedObject.FindProperty("flip");
        color = serializedObject.FindProperty("color");
        material = serializedObject.FindProperty("material");
    }

    public override void OnInspectorGUI()
    {
        EditorGUI.BeginChangeCheck();
        serializedObject.Update();
        EditorGUILayout.PropertyField(manager);
        
        Rect totalRect = EditorGUILayout.GetControlRect();
        var label = new GUIContent("Pixels Per Unit");
        EditorGUI.BeginProperty(totalRect, label, pixelsPerUnit);
        renderer.PixelsPerUnit = EditorGUI.IntField(totalRect, label, renderer.PixelsPerUnit);
        EditorGUI.EndProperty();
        
        totalRect = EditorGUILayout.GetControlRect(); 
        label = new GUIContent("Color");
        EditorGUI.BeginProperty(totalRect, label, color);
        renderer.Color = EditorGUI.ColorField(totalRect, label, renderer.Color);
        EditorGUI.EndProperty();
        
        totalRect = EditorGUILayout.GetControlRect(); 
        label = new GUIContent("Material");
        EditorGUI.BeginProperty(totalRect, label, material);
        renderer.Material = (Material)EditorGUI.ObjectField(totalRect, label, renderer.Material, typeof(Material), false);
        EditorGUI.EndProperty();
        
        EditorGUI.BeginChangeCheck();
        LSImageEditor.DrawFlipProperty("Flip", flip);
        if (EditorGUI.EndChangeCheck()) renderer.MarkMeshAsDirty();
        
        DrawRotateButton();
        if (EditorGUI.EndChangeCheck())
        {
            serializedObject.ApplyModifiedProperties();
        }
    }
    
    protected virtual void DrawRotateButton()
    {
        GUILayout.Space(10);
        
        var lbl = GUIContent.none;
        var r = EditorGUILayout.GetControlRect();

        EditorGUI.BeginProperty(r, lbl, rotateId);
        EditorGUI.LabelField(r, new GUIContent("Rotation"));

        GUILayout.BeginHorizontal();
        
        Rect totalRect = EditorGUILayout.GetControlRect(GUILayout.Height(30));

        for (int i = 0; i < 4; i++)
        {
            var targetAngle = i * 90;
            var text = rotateId.intValue == i ? $"{targetAngle}° ❤️" : $"{targetAngle}°";
            if (GUI.Button(totalRect.Split(i, 4), text) && rotateId.intValue != i)
            {
                rotateId.intValue = i;
            }
        }

        GUILayout.EndHorizontal();
        EditorGUI.EndProperty();
    }
}
#endif