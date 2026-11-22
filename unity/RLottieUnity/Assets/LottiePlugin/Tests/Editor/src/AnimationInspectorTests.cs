using LottiePlugin.UI;
using LottiePlugin.UI.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LottiePlugin.Tests.Editor
{
    public sealed class AnimationInspectorTests
    {
        [TestCase(0L, 0)]
        [TestCase(1L, 0)]
        [TestCase(60L, 59)]
        [TestCase(long.MaxValue, int.MaxValue)]
        public void InspectorBoundsUseTheLastRepresentableFrame(long frameCount, int expected)
        {
            Assert.AreEqual(expected, AnimationInspector.LastFrameIndex(frameCount));
        }

        [Test]
        public void SelectingImagesTogetherPreservesTheirSerializedDimensions()
        {
            var first = new GameObject("First");
            var second = new GameObject("Second");
            UnityEditor.Editor inspector = null;
            try
            {
                var firstImage = first.AddComponent<AnimatedImage>();
                var secondImage = second.AddComponent<AnimatedImage>();
                using (var firstProperties = new SerializedObject(firstImage))
                using (var secondProperties = new SerializedObject(secondImage))
                {
                    firstProperties.FindProperty("_textureWidth").intValue = 0;
                    firstProperties.FindProperty("_textureHeight").intValue = 32;
                    secondProperties.FindProperty("_textureWidth").intValue = 64;
                    secondProperties.FindProperty("_textureHeight").intValue = 0;
                    firstProperties.ApplyModifiedProperties();
                    secondProperties.ApplyModifiedProperties();

                    inspector = UnityEditor.Editor.CreateEditor(new Object[] { firstImage, secondImage });
                    firstProperties.Update();
                    secondProperties.Update();
                    Assert.AreEqual(0, firstProperties.FindProperty("_textureWidth").intValue);
                    Assert.AreEqual(32, firstProperties.FindProperty("_textureHeight").intValue);
                    Assert.AreEqual(64, secondProperties.FindProperty("_textureWidth").intValue);
                    Assert.AreEqual(0, secondProperties.FindProperty("_textureHeight").intValue);
                }
            }
            finally
            {
                if (inspector != null) Object.DestroyImmediate(inspector);
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }
    }
}
