#if UNITY_EDITOR
using System.Collections.ObjectModel;
using UnityEditor;
using UnityEditor.UI;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UI;

namespace LottiePlugin.UI.Editor
{
    [CustomEditor(typeof(AnimatedButton), true)]
    [CanEditMultipleObjects]
    internal sealed class AnimatedButtonEditor : SelectableEditor
    {
        //Own
        private SerializedProperty _animationJsonProperty;
        private SerializedProperty _widthProperty;
        private SerializedProperty _heightProperty;
        private SerializedProperty _graphicProperty;
        private SerializedProperty _statesProperty;
        //Selectable
        private SerializedProperty m_InteractableProperty;
        private SerializedProperty m_NavigationProperty;

        private ReorderableList _statesList;
        private LottieAnimation _lottieAnimation;

        protected override void OnEnable()
        {
            base.OnEnable();

            _animationJsonProperty = serializedObject.FindProperty("_animationJson");
            _widthProperty = serializedObject.FindProperty("_textureWidth");
            _heightProperty = serializedObject.FindProperty("_textureHeight");
            _graphicProperty = serializedObject.FindProperty("_graphic");
            _statesProperty = serializedObject.FindProperty("_states");

            m_InteractableProperty = serializedObject.FindProperty("m_Interactable");
            m_NavigationProperty = serializedObject.FindProperty("m_Navigation");

            CreateAnimationIfNecessaryAndAttachToGraphic();

            _statesList = new ReorderableList(serializedObject, _statesProperty, true, true, true, true) {
                drawHeaderCallback = DrawHeader,
                onAddCallback = AddCallback,
                drawElementCallback = DrawListItems,
                onSelectCallback = OnSelectCallback
            };
        }
        protected override void OnDisable()
        {
            base.OnDisable();
            _lottieAnimation?.Dispose();
            _lottieAnimation = null;
            _statesList.drawHeaderCallback = null;
            _statesList.onAddCallback = null;
            _statesList.drawElementCallback = null;
            _statesList.onSelectCallback = null;
            _statesList = null;
        }
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(m_InteractableProperty);
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(m_NavigationProperty);
            EditorGUILayout.Space();
            AnimatedButton button = serializedObject.targetObject as AnimatedButton;

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(_animationJsonProperty);
            if (EditorGUI.EndChangeCheck())
            {
                _lottieAnimation?.Dispose();
                _lottieAnimation = null;
                CreateAnimationIfNecessaryAndAttachToGraphic();

                _statesProperty.arraySize = 0;
                _statesProperty.arraySize = 2;
                SetStateValuesAtIndex(0, "Begin", 0, true);
                SetStateValuesAtIndex(1, "End", (int)_lottieAnimation?.TotalFramesCount, true);
            }
            if (button.AnimationJson == null)
            {
                EditorGUILayout.HelpBox("You must have a lottie json in order to use the animated button.", MessageType.Error);
            }
            EditorGUILayout.Space();
            if (_widthProperty.intValue == 0)
            {
                _widthProperty.intValue = 128;
            }
            if (_heightProperty.intValue == 0)
            {
                _heightProperty.intValue = 128;
            }
            EditorGUILayout.PropertyField(_widthProperty);
            EditorGUILayout.PropertyField(_heightProperty);
            EditorGUILayout.PropertyField(_graphicProperty);

            if (button.Graphic == null)
            {
                EditorGUILayout.HelpBox("You must have a target graphic set in order to use the animated button.", MessageType.Error);
            }
            _statesList.DoLayoutList();
            serializedObject.ApplyModifiedProperties();
        }
        private void CreateAnimationIfNecessaryAndAttachToGraphic()
        {
            if (_lottieAnimation != null)
            {
                return;
            }
            serializedObject.ApplyModifiedProperties();
            AnimatedButton button = serializedObject.targetObject as AnimatedButton;
            if (button.AnimationJson == null)
            {
                return;
            }
            string jsonData = button.AnimationJson.text;
            _lottieAnimation = LottieAnimation.LoadFromJsonData(
                jsonData,
                string.Empty,
                button.TextureWidth,
                button.TextureHeight);
            _lottieAnimation.DrawOneFrame();
            ((RawImage)button.Graphic).texture = _lottieAnimation.Texture;
        }
        private void DrawListItems(Rect rect, int index, bool isActive, bool isFocused)
        {
            SerializedProperty element = _statesList.serializedProperty.GetArrayElementAtIndex(index); //The element in the list

            EditorGUI.PropertyField(
                new Rect(rect.x, rect.y, 120, EditorGUIUtility.singleLineHeight),
                GetStateNameProperty(element),
                GUIContent.none
            );
            //EditorGUI.PropertyField(
            //    new Rect(rect.x + 140, rect.y, 50, EditorGUIUtility.singleLineHeight),
            //    GetFrameNumberProperty(element),
            //    GUIContent.none
            //);
            SerializedProperty prop = GetFrameNumberProperty(element);
            int currentFrame = EditorGUI.IntSlider(
                new Rect(rect.x + 200, rect.y, rect.width - 260, EditorGUIUtility.singleLineHeight),
                prop.intValue,
                0,
                (int)_lottieAnimation.TotalFramesCount);
            if (currentFrame != prop.intValue)
            {
                _lottieAnimation.DrawOneFrame(currentFrame);
                prop.intValue = currentFrame;
            }
            EditorGUI.PropertyField(
                new Rect(rect.width, rect.y, 0, EditorGUIUtility.singleLineHeight),
                GetStayHereProperty(element),
                GUIContent.none
            );

            //// The 'level' property
            //// The label field for level (width 100, height of a single line)
            //EditorGUI.LabelField(new Rect(rect.x + 120, rect.y, 100, EditorGUIUtility.singleLineHeight), "Level");

            ////The property field for level. Since we do not need so much space in an int, width is set to 20, height of a single line.
            //EditorGUI.PropertyField(
            //    new Rect(rect.x + 160, rect.y, 20, EditorGUIUtility.singleLineHeight),
            //    element.FindPropertyRelative("level"),
            //    GUIContent.none
            //);


            //// The 'quantity' property
            //// The label field for quantity (width 100, height of a single line)
            //EditorGUI.LabelField(new Rect(rect.x + 200, rect.y, 100, EditorGUIUtility.singleLineHeight), "Quantity");

            ////The property field for quantity (width 20, height of a single line)
            //EditorGUI.PropertyField(
            //    new Rect(rect.x + 250, rect.y, 20, EditorGUIUtility.singleLineHeight),
            //    element.FindPropertyRelative("quantity"),
            //    GUIContent.none
            //);

        }
        private void DrawHeader(Rect rect)
        {
            string name = "Button states";
            EditorGUI.LabelField(rect, name);
        }
        private void AddCallback(ReorderableList list)
        {
            _statesProperty.arraySize++;
            int newIndex = _statesProperty.arraySize - 1;
            string stateName = "State Number " + (newIndex + 1).ToString();
            SetStateValuesAtIndex(newIndex, stateName, 0, true);
        }
        private void SetStateValuesAtIndex(int index, string stateName, int frameNumber, bool stayInThisState)
        {
            SerializedProperty element = _statesProperty.GetArrayElementAtIndex(index);
            SerializedProperty stateNameProp = GetStateNameProperty(element);
            stateNameProp.stringValue = stateName;
            SerializedProperty frameNumberProp = GetFrameNumberProperty(element);
            frameNumberProp.intValue = frameNumber;
            SerializedProperty stayInThisStateProp = GetStayHereProperty(element);
            stayInThisStateProp.boolValue = stayInThisState;
        }
        private void OnSelectCallback(ReorderableList list)
        {
            ReadOnlyCollection<int> selectedIndices = list.selectedIndices;
            if (selectedIndices.Count != 1)
            {
                return;
            }
            int selectedElementIndex = selectedIndices[0];
            SerializedProperty selectedElement = _statesProperty.GetArrayElementAtIndex(selectedElementIndex);
            SerializedProperty frameNumberProperty = GetFrameNumberProperty(selectedElement);
            _lottieAnimation?.DrawOneFrame(frameNumberProperty.intValue);
        }
        private static SerializedProperty GetStateNameProperty(SerializedProperty element) =>
            element.FindPropertyRelative("Name");
        private static SerializedProperty GetFrameNumberProperty(SerializedProperty element) =>
            element.FindPropertyRelative("FrameNumber");
        private static SerializedProperty GetStayHereProperty(SerializedProperty element) =>
            element.FindPropertyRelative("StayHere");

    }
}
#endif
