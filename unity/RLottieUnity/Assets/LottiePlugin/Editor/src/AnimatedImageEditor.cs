#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace LottiePlugin.UI.Editor
{
    [CustomEditor(typeof(AnimatedImage), true)]
    [CanEditMultipleObjects]
    internal sealed class AnimatedImageEditor : UnityEditor.Editor
    {
        //Own
        private SerializedProperty Started;
        private SerializedProperty Paused;
        private SerializedProperty Stopped;
        private SerializedProperty _animationJsonProperty;
        private SerializedProperty _animationSpeedProperty;
        private SerializedProperty _widthProperty;
        private SerializedProperty _heightProperty;
        private SerializedProperty _playOnAwake;
        private SerializedProperty _loop;
        private SerializedProperty _stopOnLastFrame;
        private SerializedProperty _logLevel;

        private AnimatedImage _image;
        private LottieAnimation Animation => _image.Animation;
        private string _animationInfoBoxText;

        private void OnEnable()
        {
            _image = serializedObject.targetObject as AnimatedImage;
            Started = serializedObject.FindProperty("Started");
            Paused = serializedObject.FindProperty("Paused");
            Stopped = serializedObject.FindProperty("Stopped");
            _animationJsonProperty = serializedObject.FindProperty("_animationJson");
            _animationSpeedProperty = serializedObject.FindProperty("_animationSpeed");
            _widthProperty = serializedObject.FindProperty("_textureWidth");
            _heightProperty = serializedObject.FindProperty("_textureHeight");
            _playOnAwake = serializedObject.FindProperty("_playOnAwake");
            _loop = serializedObject.FindProperty("_loop");
            _stopOnLastFrame = serializedObject.FindProperty("_stopOnLastFrame");
            _logLevel = serializedObject.FindProperty("_logLevel");

            CreatePreview(_image);
            UpdateTheAnimationInfoBoxText();
        }
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(_animationJsonProperty);
            if (EditorGUI.EndChangeCheck())
            {
                RebuildPreviews();
            }
            if (Animation == null && !serializedObject.isEditingMultipleObjects &&
                _image.AnimationJson == null)
            {
                EditorGUILayout.HelpBox("You must have a lottie json in order to use the animated image.", MessageType.Error);
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
            EditorGUILayout.PropertyField(_playOnAwake);
            EditorGUILayout.PropertyField(_loop);
            EditorGUILayout.PropertyField(_stopOnLastFrame);
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(_logLevel, new GUIContent("Log Level", "Controls verbosity of native plugin logging. Warning: only errors and warnings. Info: all messages including debug info."));
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(Started);
            EditorGUILayout.PropertyField(Paused);
            EditorGUILayout.PropertyField(Stopped);
            serializedObject.ApplyModifiedProperties();
        }
        private void RebuildPreviews()
        {
            serializedObject.ApplyModifiedProperties();
            foreach (AnimatedImage image in targets)
            {
                image.DisposeLottieAnimation();
                CreatePreview(image);
            }
            UpdateTheAnimationInfoBoxText();
        }
        private static void CreatePreview(AnimatedImage image)
        {
            if (image.Animation != null) return;
            if (AnimationInspector.HasEmptyAsset(image.AnimationJson))
            {
                Debug.LogError("The selected animation JSON is empty.", image);
                return;
            }
            image.CreateIfNeededAndReturnLottieAnimation()?.DrawOneFrame(0);
        }
        private void UpdateTheAnimationInfoBoxText()
        {
            _animationInfoBoxText = AnimationInspector.FormatInfo(Animation, _animationSpeedProperty.floatValue);
        }
    }
}
#endif
