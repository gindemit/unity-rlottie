#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UI;
using UnityEngine.UI;

namespace LottiePlugin.UI.Editor
{
    [CustomEditor(typeof(AnimatedToggle), true)]
    [CanEditMultipleObjects]
    internal sealed class AnimatedImageToggleEditor :  SelectableEditor
    {
        //Own
        private SerializedProperty _animationJsonProperty;
        //Selectable
        private SerializedProperty m_InteractableProperty;
        private SerializedProperty m_NavigationProperty;
        //Toggle
        private SerializedProperty m_OnValueChangedProperty;
        private SerializedProperty m_GraphicProperty;
        private SerializedProperty m_GroupProperty;
        private SerializedProperty m_IsOnProperty;

        protected override void OnEnable()
        {
            base.OnEnable();

            _animationJsonProperty = serializedObject.FindProperty("_animationJson");

            m_InteractableProperty = serializedObject.FindProperty("m_Interactable");
            m_NavigationProperty = serializedObject.FindProperty("m_Navigation");

            m_GraphicProperty = serializedObject.FindProperty("graphic");
            m_GroupProperty = serializedObject.FindProperty("m_Group");
            m_IsOnProperty = serializedObject.FindProperty("m_IsOn");
            m_OnValueChangedProperty = serializedObject.FindProperty("onValueChanged");
        }
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(m_InteractableProperty);
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(m_NavigationProperty);
            EditorGUILayout.Space();
            AnimatedToggle toggle = serializedObject.targetObject as AnimatedToggle;
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(_animationJsonProperty);
            if (toggle.AnimationJson == null)
            {
                EditorGUILayout.HelpBox("You must have a lottie json in order to use the animated toggle.", MessageType.Error);
            }
            EditorGUILayout.PropertyField(m_IsOnProperty);
            if (EditorGUI.EndChangeCheck())
            {
                EditorSceneManager.MarkSceneDirty(toggle.gameObject.scene);
                ToggleGroup group = m_GroupProperty.objectReferenceValue as ToggleGroup;

                toggle.isOn = m_IsOnProperty.boolValue;

                if (group != null && group.isActiveAndEnabled && toggle.IsActive())
                {
                    if (toggle.isOn || (!group.AnyTogglesOn() && !group.allowSwitchOff))
                    {
                        toggle.isOn = true;
                        group.NotifyToggleOn(toggle);
                    }
                }
            }
            EditorGUILayout.PropertyField(m_GraphicProperty);
            if (toggle.graphic == null)
            {
                EditorGUILayout.HelpBox("You must have a graphic in order to use the animated toggle.", MessageType.Error);
            }
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(m_GroupProperty);
            if (EditorGUI.EndChangeCheck())
            {
                EditorSceneManager.MarkSceneDirty(toggle.gameObject.scene);
                ToggleGroup group = m_GroupProperty.objectReferenceValue as ToggleGroup;
                toggle.group = group;
            }

            EditorGUILayout.Space();

            // Draw the event notification options
            EditorGUILayout.PropertyField(m_OnValueChangedProperty);

            serializedObject.ApplyModifiedProperties();

        }
    }
}
#endif