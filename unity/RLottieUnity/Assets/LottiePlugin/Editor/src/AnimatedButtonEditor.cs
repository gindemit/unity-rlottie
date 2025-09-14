#if UNITY_EDITOR
using System.Text;
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
        //Selectable
        private SerializedProperty m_InteractableProperty;
        private SerializedProperty m_NavigationProperty;

        private AnimatedButton _button;
        private LottieAnimation _lottieAnimation;
        private ReorderableList _statesList;
        private string _animationInfoBoxText;

        protected override void OnEnable()
        {
            base.OnEnable();

            _button = serializedObject.targetObject as AnimatedButton;
            _lottieAnimation = _button.CreateIfNeededAndReturnLottieAnimation();
            _animationJsonProperty = serializedObject.FindProperty("_animationJson");
            _animationSpeedProperty = serializedObject.FindProperty("_animationSpeed");
            _widthProperty = serializedObject.FindProperty("_textureWidth");
            _heightProperty = serializedObject.FindProperty("_textureHeight");
            _rawImageProperty = serializedObject.FindProperty("_rawImage");
            _ignoreInputWhileAnimatingProperty = serializedObject.FindProperty("_ignoreInputWhileAnimating");
            _onClickProperty = serializedObject.FindProperty("_onClick");
            _statesProperty = serializedObject.FindProperty("_states");

            m_InteractableProperty = serializedObject.FindProperty("m_Interactable");
            m_NavigationProperty = serializedObject.FindProperty("m_Navigation");

            CreateAnimationIfNecessaryAndAttachToGraphic();
            UpdateTheAnimationInfoBoxText();

            _statesList = new ReorderableList(serializedObject, _statesProperty, true, true, true, true)
            {
                drawHeaderCallback = DrawHeader,
                onAddCallback = AddCallback,
                drawElementCallback = DrawListItems,
                onSelectCallback = OnSelectCallback
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
                _statesList.onSelectCallback = null;
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
                _button.DisposeLottieAnimation();
                _lottieAnimation = null;
                CreateAnimationIfNecessaryAndAttachToGraphic();
                UpdateTheAnimationInfoBoxText();

                _statesProperty.arraySize = 0;
                _statesProperty.arraySize = 2;
                SetStateValuesAtIndex(0, "Begin", 0, true);
                SetStateValuesAtIndex(1, "End", _lottieAnimation != null ? (int)_lottieAnimation.TotalFramesCount : 0, true);
            }
            if (_button.AnimationJson == null ||
                string.IsNullOrEmpty(_button.AnimationJson.text))
            {
                EditorGUILayout.HelpBox("You must have a lottie json in order to use the animated button.", MessageType.Error);
            }
            if (_lottieAnimation != null)
            {
                EditorGUILayout.HelpBox(_animationInfoBoxText, MessageType.Info);
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
                _button.DisposeLottieAnimation();
                _lottieAnimation = null;
                CreateAnimationIfNecessaryAndAttachToGraphic();
            }
            EditorGUILayout.EndHorizontal();
            if (_widthProperty.intValue > 2048 || _heightProperty.intValue > 2048)
            {
                EditorGUILayout.HelpBox("Higher texture resolution will consume more processor resources at runtime.", MessageType.Warning);
            }
            EditorGUILayout.Space();
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(_rawImageProperty);
            if (EditorGUI.EndChangeCheck())
            {
                _button.SetTextureToTheTargetRawImage();
            }
            if (_button.RawImage == null)
            {
                EditorGUILayout.HelpBox("You must have a target graphic set in order to use the animated button.", MessageType.Error);
            }
            EditorGUILayout.Space();
            _statesList.DoLayoutList();
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(_onClickProperty);
            serializedObject.ApplyModifiedProperties();
        }
        private void CreateAnimationIfNecessaryAndAttachToGraphic()
        {
            if (_lottieAnimation != null)
            {
                return;
            }
            serializedObject.ApplyModifiedProperties();
            if (_button.AnimationJson == null)
            {
                return;
            }
            string jsonData = _button.AnimationJson.text;
            if (string.IsNullOrEmpty(jsonData))
            {
                Debug.LogError("Selected file is not a lottie json");
                return;
            }
            _lottieAnimation = _button.CreateIfNeededAndReturnLottieAnimation();
            if (_lottieAnimation == null)
            {
                return;
            }
            _lottieAnimation.DrawOneFrame(0);
        }
        private void UpdateTheAnimationInfoBoxText()
        {
            if (_lottieAnimation == null)
            {
                return;
            }

            StringBuilder sb = new StringBuilder("Animation info: Frame Rate \"");
            sb.Append(_lottieAnimation.FrameRate.ToString("F2"))
              .Append("\", Total Frames \"")
              .Append(_lottieAnimation.TotalFramesCount)
              .Append("\", Original Duration \"")
              .Append(_lottieAnimation.DurationSeconds.ToString("F2"))
              .Append("\" sec. Play Duration \"")
              .Append((_lottieAnimation.DurationSeconds / _animationSpeedProperty.floatValue).ToString("F2"))
              .Append("\" sec. Current Frame \"")
              .Append(_lottieAnimation.CurrentFrame)
              .Append("\"");

            _animationInfoBoxText = sb.ToString();
        }

        private void DrawListItems(Rect rect, int index, bool isActive, bool isFocused)
        {
            SerializedProperty element = _statesList.serializedProperty.GetArrayElementAtIndex(index); //The element in the list

            rect.y += 3;
            EditorGUI.PropertyField(
                new Rect(rect.x + 10, rect.y, 120, EditorGUIUtility.singleLineHeight),
                GetStateNameProperty(element),
                GUIContent.none
            );
            EditorGUI.IntField(
                new Rect(rect.x + 140, rect.y, 20, EditorGUIUtility.singleLineHeight),
                0);
            SerializedProperty prop = GetFrameNumberProperty(element);
            int currentFrame = EditorGUI.IntSlider(
                new Rect(rect.x + 170, rect.y, rect.width - 180, EditorGUIUtility.singleLineHeight),
                prop.intValue,
                0,
                _lottieAnimation != null ? (int)_lottieAnimation.TotalFramesCount : 0);
            if (currentFrame != prop.intValue)
            {
                _lottieAnimation?.DrawOneFrame(currentFrame);
                prop.intValue = currentFrame;
            }

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
            const string NAME = "State Name";
            EditorGUI.LabelField(new Rect(rect.x + 24, rect.y, 70, EditorGUIUtility.singleLineHeight), NAME);
            const string FRAME_NUMBER = "Frame Number";
            EditorGUI.LabelField(new Rect((rect.width / 2f) + 20, rect.y, 100, EditorGUIUtility.singleLineHeight), FRAME_NUMBER);
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
        }
        private void OnSelectCallback(ReorderableList list)
        {
            //ReadOnlyCollection<int> selectedIndices = list.selectedIndices;
            //if (selectedIndices.Count != 1)
            //{
            //    return;
            //}
            //int selectedElementIndex = selectedIndices[0];
            //SerializedProperty selectedElement = _statesProperty.GetArrayElementAtIndex(selectedElementIndex);
            //SerializedProperty frameNumberProperty = GetFrameNumberProperty(selectedElement);
            //_lottieAnimation?.DrawOneFrame(frameNumberProperty.intValue);
        }
        private static SerializedProperty GetStateNameProperty(SerializedProperty element) =>
            element.FindPropertyRelative("Name");
        private static SerializedProperty GetFrameNumberProperty(SerializedProperty element) =>
            element.FindPropertyRelative("FrameNumber");
    }
}
#endif
