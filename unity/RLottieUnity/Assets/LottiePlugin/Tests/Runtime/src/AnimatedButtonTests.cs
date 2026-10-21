using System.Collections;
using System.Reflection;
using LottiePlugin.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace LottiePlugin.Tests.Runtime
{
    public class AnimatedButtonTests
    {
        private GameObject _gameObject;

        [TearDown]
        public void TearDown()
        {
            if (_gameObject != null)
            {
                Object.DestroyImmediate(_gameObject);
            }
        }

        [UnityTest]
        public IEnumerator AnimatedButtonBindsItsOutputTexture()
        {
            TextAsset animationJson = Resources.Load<TextAsset>("body_movin");
            Assert.NotNull(animationJson);

            _gameObject = new GameObject("AnimatedButtonTest");
            RawImage rawImage = _gameObject.AddComponent<RawImage>();
            AnimatedButton button = _gameObject.AddComponent<AnimatedButton>();
            SetField(button, "_animationJson", animationJson);
            SetField(button, "_rawImage", rawImage);
            SetField(button, "_textureWidth", 32u);
            SetField(button, "_textureHeight", 32u);
            SetField(button, "_states", new[]
            {
                new AnimatedButton.State { Name = "Default", FrameNumber = 0 }
            });

            yield return null;

            Assert.NotNull(button.Animation);
            Assert.AreSame(button.Animation.OutputTexture, rawImage.texture);
        }

        [UnityTest]
        public IEnumerator AnimatedButtonCompletesFinalAsyncFrame()
        {
            TextAsset animationJson = Resources.Load<TextAsset>("body_movin");
            Assert.NotNull(animationJson);

            _gameObject = new GameObject("AnimatedButtonAsyncTest");
            RawImage rawImage = _gameObject.AddComponent<RawImage>();
            AnimatedButton button = _gameObject.AddComponent<AnimatedButton>();
            SetField(button, "_animationJson", animationJson);
            SetField(button, "_rawImage", rawImage);
            SetField(button, "_textureWidth", 64u);
            SetField(button, "_textureHeight", 64u);
            SetField(button, "_pauseIfCulled", false);
            SetField(button, "_states", new[]
            {
                new AnimatedButton.State { Name = "Default", FrameNumber = 0 },
                new AnimatedButton.State { Name = "Pressed", FrameNumber = 5 }
            });

            yield return null;
            SetField(button, "_currentStateIndex", 1);
            IEnumerator animation = InvokeCoroutine(button, "AnimateToNextState");

            float deadline = Time.realtimeSinceStartup + 5f;
            bool completed = false;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (!button.Animation.AsyncDrawPending)
                {
                    SetAnimationField(button.Animation, "_timeSinceLastRenderCall", 0.04f);
                }
                if (!animation.MoveNext())
                {
                    completed = true;
                    break;
                }
                yield return null;
            }

            Assert.IsTrue(completed, "AnimatedButton did not finish its async state transition.");
            Assert.GreaterOrEqual(button.Animation.CurrentFrame, 5);
            Assert.IsFalse(button.Animation.AsyncDrawPending);
            Assert.AreSame(button.Animation.OutputTexture, rawImage.texture);
        }

        private static void SetField<T>(AnimatedButton button, string fieldName, T value)
        {
            FieldInfo field = typeof(AnimatedButton).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field, $"Could not find AnimatedButton.{fieldName}.");
            field.SetValue(button, value);
        }

        private static void SetAnimationField<T>(LottieAnimation animation, string fieldName, T value)
        {
            FieldInfo field = typeof(LottieAnimation).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field, $"Could not find LottieAnimation.{fieldName}.");
            field.SetValue(animation, value);
        }

        private static IEnumerator InvokeCoroutine(AnimatedButton button, string methodName)
        {
            MethodInfo method = typeof(AnimatedButton).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method, $"Could not find AnimatedButton.{methodName}.");
            return (IEnumerator)method.Invoke(button, null);
        }
    }
}
