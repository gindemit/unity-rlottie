#if UNITY_EDITOR
using LSCore.Extensions;
using UnityEditor;
using UnityEditor.UI;
using UnityEngine;

namespace LSCore
{
    [CustomEditor(typeof(LSRawImage), true)]
    [CanEditMultipleObjects]
    public class LSRawImageEditor : RawImageEditor
    {
        LSRawImage image;
        SerializedProperty m_Texture;
        SerializedProperty preserveAspectRatio;
        SerializedProperty rotateId;
        SerializedProperty flip;

        protected override void OnEnable()
        {
            base.OnEnable();
            image = (LSRawImage)target;
            m_Texture = serializedObject.FindProperty("m_Texture");
            preserveAspectRatio = serializedObject.FindProperty("preserveAspectRatio");
            rotateId = serializedObject.FindProperty("rotateId");
            flip = serializedObject.FindProperty("flip");
        }

        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            serializedObject.Update();

            EditorGUILayout.PropertyField(m_Texture);
            var tex = image.texture;
            if (tex)
            {
                GUI.enabled = false;
                EditorGUILayout.Vector2IntField("Size", new Vector2Int(tex.width, tex.height));
                GUI.enabled = true;
            }
            AppearanceControlsGUI();
            RaycastControlsGUI();
            MaskableControlsGUI();
            SetShowNativeSize(m_Texture.objectReferenceValue != null, false);
            NativeSizeButtonGUI();
            
            EditorGUILayout.PropertyField(preserveAspectRatio);
            LSImageEditor.DrawFlipProperty("Flip", flip);
            DrawRotateButton();

            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
            }
        }

        protected virtual void DrawRotateButton()
        {
            GUILayout.Space(10);
            GUILayout.BeginHorizontal();

            var lbl = GUIContent.none;
            Rect totalRect = EditorGUILayout.GetControlRect(GUILayout.Height(30));
            EditorGUI.BeginProperty(totalRect, lbl, rotateId);
            
            for (int i = 0; i < 4; i++)
            {
                var targetAngle = i * 90;
                var text = rotateId.intValue == i ? $"{targetAngle}° ❤️" : $"{targetAngle}°";
                if (GUI.Button(totalRect.Split(i, 4), text) && rotateId.intValue != i)
                {
                    rotateId.intValue = i;
                    image.SetVerticesDirty();
                }
            }
            EditorGUI.EndProperty();
            GUILayout.EndHorizontal();
        }
    }
}
#endif