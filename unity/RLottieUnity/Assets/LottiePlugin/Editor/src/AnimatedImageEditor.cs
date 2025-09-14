#if UNITY_EDITOR
using System.Text;
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

        private AnimatedImage _image;
        private LottieAnimation _lottieAnimation;
        private string _animationInfoBoxText;

        private void OnEnable()
        {
            _image = serializedObject.targetObject as AnimatedImage;
            _lottieAnimation = _image.CreateIfNeededAndReturnLottieAnimation();
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

            CreateAnimationIfNecessaryAndAttachToGraphic();
            UpdateTheAnimationInfoBoxText();
        }
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(_animationJsonProperty);
            if (EditorGUI.EndChangeCheck())
            {
                _image.DisposeLottieAnimation();
                _lottieAnimation = null;
                CreateAnimationIfNecessaryAndAttachToGraphic();
                UpdateTheAnimationInfoBoxText();
            }
            if (_image.AnimationJson == null ||
                string.IsNullOrEmpty(_image.AnimationJson.text))
            {
                EditorGUILayout.HelpBox("You must have a lottie json in order to use the animated image.", MessageType.Error);
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
                _image.DisposeLottieAnimation();
                _lottieAnimation = null;
                CreateAnimationIfNecessaryAndAttachToGraphic();
            }
            EditorGUILayout.EndHorizontal();
            if (_widthProperty.intValue > 2048 || _heightProperty.intValue > 2048)
            {
                EditorGUILayout.HelpBox("Higher texture resolution will consume more processor resources at runtime.", MessageType.Warning);
            }
            EditorGUILayout.PropertyField(_playOnAwake);
            EditorGUILayout.PropertyField(_loop);
            EditorGUILayout.PropertyField(_stopOnLastFrame);
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(Started);
            EditorGUILayout.PropertyField(Paused);
            EditorGUILayout.PropertyField(Stopped);
            serializedObject.ApplyModifiedProperties();
        }
        private void CreateAnimationIfNecessaryAndAttachToGraphic()
        {
            if (_lottieAnimation != null)
            {
                return;
            }
            serializedObject.ApplyModifiedProperties();
            if (_image.AnimationJson == null)
            {
                return;
            }
            string jsonData = _image.AnimationJson.text;
            if (string.IsNullOrEmpty(jsonData))
            {
                Debug.LogError("Selected file is not a lottie json");
                return;
            }
            _lottieAnimation = _image.CreateIfNeededAndReturnLottieAnimation();
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

            var sb = new StringBuilder();
            sb.Append("Animation info: Frame Rate \"");
            sb.Append(_lottieAnimation.FrameRate.ToString("F2"));
            sb.Append("\", Total Frames \"");
            sb.Append(_lottieAnimation.TotalFramesCount.ToString());
            sb.Append("\", Original Duration \"");
            sb.Append(_lottieAnimation.DurationSeconds.ToString("F2"));
            sb.Append("\" sec. Play Duration \"");
            sb.Append((_lottieAnimation.DurationSeconds / _animationSpeedProperty.floatValue).ToString("F2"));
            sb.Append("\" sec. Current Frame \"");
            sb.Append(_lottieAnimation.CurrentFrame.ToString());
            sb.Append("\"");

            _animationInfoBoxText = sb.ToString();
        }
    }
}
#endif
