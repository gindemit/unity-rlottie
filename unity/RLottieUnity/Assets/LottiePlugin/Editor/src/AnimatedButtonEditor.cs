#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.UI;
using UnityEditorInternal;
using UnityEngine;

namespace LottiePlugin.UI.Editor
{
    [CustomEditor(typeof(AnimatedButton), true)]
    [CanEditMultipleObjects]
    internal sealed class AnimatedButtonEditor : SelectableEditor
    {
        //Own
        private SerializedProperty _animationJsonProperty;
        private SerializedProperty _animationSpeedProperty;
        private SerializedProperty _widthProperty;
        private SerializedProperty _heightProperty;
        private SerializedProperty _rawImageProperty;
        private SerializedProperty _ignoreInputWhileAnimatingProperty;
        private SerializedProperty _onClickProperty;
        private SerializedProperty _statesProperty;
        private SerializedProperty _logLevel;
        //Selectable
        private SerializedProperty m_InteractableProperty;
        private SerializedProperty m_NavigationProperty;

        private AnimatedButton _button;
        private LottieAnimation Animation => _button.Animation;
        private int LastFrame => AnimationInspector.LastFrameIndex(Animation?.TotalFramesCount ?? 0);
        private ReorderableList _statesList;
        private string _animationInfoBoxText;

        protected override void OnEnable()
        {
            base.OnEnable();

            _button = serializedObject.targetObject as AnimatedButton;
            _animationJsonProperty = serializedObject.FindProperty("_animationJson");
            _animationSpeedProperty = serializedObject.FindProperty("_animationSpeed");
            _widthProperty = serializedObject.FindProperty("_textureWidth");
            _heightProperty = serializedObject.FindProperty("_textureHeight");
            _rawImageProperty = serializedObject.FindProperty("_rawImage");
            _ignoreInputWhileAnimatingProperty = serializedObject.FindProperty("_ignoreInputWhileAnimating");
            _onClickProperty = serializedObject.FindProperty("_onClick");
            _statesProperty = serializedObject.FindProperty("_states");
            _logLevel = serializedObject.FindProperty("_logLevel");

            m_InteractableProperty = serializedObject.FindProperty("m_Interactable");
            m_NavigationProperty = serializedObject.FindProperty("m_Navigation");

            CreatePreview(_button);
            UpdateTheAnimationInfoBoxText();

            _statesList = new ReorderableList(serializedObject, _statesProperty, true, true, true, true)
            {
                drawHeaderCallback = DrawHeader,
                onAddCallback = AddCallback,
                drawElementCallback = DrawListItems
            };
        }
        protected override void OnDisable()
        {
            base.OnDisable();
            if (_statesList != null)
            {
                _statesList.drawHeaderCallback = null;
                _statesList.onAddCallback = null;
                _statesList.drawElementCallback = null;
                _statesList = null;
            }
        }
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(m_InteractableProperty);
            EditorGUILayout.PropertyField(_ignoreInputWhileAnimatingProperty);
            EditorGUILayout.PropertyField(m_NavigationProperty);

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(_animationJsonProperty);
            if (EditorGUI.EndChangeCheck())
            {
                RebuildPreviews();

                _statesProperty.arraySize = 0;
                _statesProperty.arraySize = 2;
                SetStateValuesAtIndex(0, "Begin", 0);
                int lastFrame = 0;
                // The new JSON is shared across the selection. Some targets may
                // have no RawImage and therefore no preview of their own.
                foreach (AnimatedButton button in targets)
                {
                    if (button.Animation == null) continue;
                    lastFrame = AnimationInspector.LastFrameIndex(button.Animation.TotalFramesCount);
                    break;
                }
                SetStateValuesAtIndex(1, "End", lastFrame);
            }
            if (Animation == null && !serializedObject.isEditingMultipleObjects &&
                _button.AnimationJson == null)
            {
                EditorGUILayout.HelpBox("You must have a lottie json in order to use the animated button.", MessageType.Error);
            }
            if (!serializedObject.isEditingMultipleObjects && Animation != null && !string.IsNullOrEmpty(_animationInfoBoxText))
            {
                EditorGUILayout.HelpBox(_animationInfoBoxText, MessageType.Info);
            }
            EditorGUILayout.Space();
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(_animationSpeedProperty);
            if (EditorGUI.EndChangeCheck())
            {
                UpdateTheAnimationInfoBoxText();
            }
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(_widthProperty);
            EditorGUILayout.PropertyField(_heightProperty);
            if (EditorGUI.EndChangeCheck())
            {
                RebuildPreviews();
            }
            EditorGUILayout.EndHorizontal();
            if ((!_widthProperty.hasMultipleDifferentValues && _widthProperty.longValue == 0) ||
                (!_heightProperty.hasMultipleDifferentValues && _heightProperty.longValue == 0))
                EditorGUILayout.HelpBox("A texture dimension of zero uses 128 pixels.", MessageType.Info);
            if (_widthProperty.intValue > 2048 || _heightProperty.intValue > 2048)
            {
                EditorGUILayout.HelpBox("Higher texture resolution will consume more processor resources at runtime.", MessageType.Warning);
            }
            EditorGUILayout.Space();
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(_rawImageProperty);
            if (EditorGUI.EndChangeCheck())
            {
                foreach (AnimatedButton button in targets)
                {
                    if (button.RawImage != null && button.Animation != null &&
                        button.RawImage.texture == button.Animation.OutputTexture)
                        button.RawImage.texture = null;
                }
                serializedObject.ApplyModifiedProperties();
                foreach (AnimatedButton button in targets)
                {
                    if (button.RawImage != null)
                    {
                        CreatePreview(button);
                        button.SetTextureToTheTargetRawImage();
                    }
                }
                UpdateTheAnimationInfoBoxText();
            }
            if (!serializedObject.isEditingMultipleObjects && _button.RawImage == null)
            {
                EditorGUILayout.HelpBox("You must have a target graphic set in order to use the animated button.", MessageType.Error);
            }
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(_logLevel, new GUIContent("Log Level", "Controls verbosity of native plugin logging. Warning: only errors and warnings. Info: all messages including debug info."));
            EditorGUILayout.Space();
            _statesList.DoLayoutList();
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(_onClickProperty);
            serializedObject.ApplyModifiedProperties();
        }
        private void RebuildPreviews()
        {
            serializedObject.ApplyModifiedProperties();
            foreach (AnimatedButton button in targets)
            {
                button.DisposeLottieAnimation();
                CreatePreview(button);
            }
            UpdateTheAnimationInfoBoxText();
        }
        private static void CreatePreview(AnimatedButton button)
        {
            if (button.Animation != null) return;
            if (AnimationInspector.HasEmptyAsset(button.AnimationJson))
            {
                Debug.LogError("The selected animation JSON is empty.", button);
                return;
            }
            button.CreateIfNeededAndReturnLottieAnimation()?.DrawOneFrame(0);
        }
        private void UpdateTheAnimationInfoBoxText()
        {
            _animationInfoBoxText = AnimationInspector.FormatInfo(Animation, _animationSpeedProperty.floatValue);
        }

        private void DrawListItems(Rect rect, int index, bool isActive, bool isFocused)
        {
            SerializedProperty element = _statesProperty.GetArrayElementAtIndex(index);
            float nameWidth = rect.width * 0.4f;
            var nameRect = new Rect(rect.x, rect.y + 2, nameWidth, EditorGUIUtility.singleLineHeight);
            var frameRect = new Rect(nameRect.xMax + 4, nameRect.y, rect.width - nameWidth - 4, nameRect.height);
            EditorGUI.PropertyField(nameRect, GetStateNameProperty(element), GUIContent.none);

            SerializedProperty frame = GetFrameNumberProperty(element);
            if (Animation == null || serializedObject.isEditingMultipleObjects)
            {
                EditorGUI.PropertyField(frameRect, frame, GUIContent.none);
                return;
            }

            EditorGUI.BeginProperty(frameRect, GUIContent.none, frame);
            EditorGUI.BeginChangeCheck();
            int selected = EditorGUI.IntSlider(frameRect, frame.intValue, 0, LastFrame);
            if (EditorGUI.EndChangeCheck())
            {
                frame.intValue = selected;
                Animation.DrawOneFrame(selected);
                UpdateTheAnimationInfoBoxText();
            }
            EditorGUI.EndProperty();
        }
        private void DrawHeader(Rect rect)
        {
            float nameWidth = rect.width * 0.4f;
            EditorGUI.LabelField(new Rect(rect.x, rect.y, nameWidth, rect.height), "State Name");
            EditorGUI.LabelField(new Rect(rect.x + nameWidth, rect.y, rect.width - nameWidth, rect.height), "Frame Number");
        }
        private void AddCallback(ReorderableList list)
        {
            _statesProperty.arraySize++;
            int newIndex = _statesProperty.arraySize - 1;
            string stateName = "State Number " + (newIndex + 1).ToString();
            SetStateValuesAtIndex(newIndex, stateName, 0);
        }
        private void SetStateValuesAtIndex(int index, string stateName, int frameNumber)
        {
            SerializedProperty element = _statesProperty.GetArrayElementAtIndex(index);
            SerializedProperty stateNameProp = GetStateNameProperty(element);
            stateNameProp.stringValue = stateName;
            SerializedProperty frameNumberProp = GetFrameNumberProperty(element);
            frameNumberProp.intValue = frameNumber;
        }
        private static SerializedProperty GetStateNameProperty(SerializedProperty element) =>
            element.FindPropertyRelative("Name");
        private static SerializedProperty GetFrameNumberProperty(SerializedProperty element) =>
            element.FindPropertyRelative("FrameNumber");
    }
}
#endif
