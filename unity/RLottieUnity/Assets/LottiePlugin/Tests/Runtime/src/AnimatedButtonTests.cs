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

        private static void SetField<T>(AnimatedButton button, string fieldName, T value)
        {
            FieldInfo field = typeof(AnimatedButton).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field, $"Could not find AnimatedButton.{fieldName}.");
            field.SetValue(button, value);
        }
    }
}
