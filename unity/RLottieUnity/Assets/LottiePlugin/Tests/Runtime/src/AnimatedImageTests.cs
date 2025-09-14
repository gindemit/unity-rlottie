using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using LottiePlugin.UI;
using System.Collections;

namespace LottiePlugin.Tests.Runtime
{
    public class AnimatedImageTests
    {
        private TextAsset _lottieAnimation;
        private AnimatedImage _animatedImage;

        [SetUp]
        public void SetUp()
        {
            GameObject go = new GameObject();
            _animatedImage = go.AddComponent<AnimatedImage>();

            _lottieAnimation = Resources.Load("body_movin") as TextAsset;
            Assert.NotNull(_lottieAnimation);
        }

        [TearDown]
        public void TearDown()
        {
            GameObject.Destroy(_animatedImage.gameObject);
        }

        [UnityTest]
        public IEnumerator CheckAwakeFunctionality()
        {
            yield return null;
            Assert.IsNotNull(_animatedImage.Transform);
        }

        [UnityTest]
        public IEnumerator CheckStartFunctionalityWithNoJsonAnimation()
        {
            yield return null;
            Assert.IsNull(_animatedImage.AnimationJson);
            Assert.IsNull(_animatedImage.RawImage);
        }

        [UnityTest]
        public IEnumerator CheckStartFunctionalityWithJsonAnimation()
        {
            _animatedImage.LoadFromAnimationJson(_lottieAnimation.text, 32, 32);
            yield return null;
            Assert.IsNotNull(_animatedImage.RawImage);
            Assert.IsNotNull(_animatedImage.RawImage.texture);
            Assert.IsNull(_animatedImage.AnimationJson); // Because loaded not from serialized field.
        }

        [UnityTest]
        public IEnumerator CheckPlayFunctionality()
        {
            _animatedImage.LoadFromAnimationJson(_lottieAnimation.text, 32, 32);
            _animatedImage.Play();
            yield return null;
            Assert.IsTrue(_animatedImage.LottieAnimation.IsPlaying);
        }

        [UnityTest]
        public IEnumerator CheckStopFunctionality()
        {
            _animatedImage.LoadFromAnimationJson(_lottieAnimation.text, 32, 32);
            _animatedImage.Play();
            yield return null;
            _animatedImage.Stop();
            yield return null;
            Assert.IsFalse(_animatedImage.LottieAnimation.IsPlaying);
        }

        [UnityTest]
        public IEnumerator CheckTextureDimensions()
        {
            _animatedImage.LoadFromAnimationJson(_lottieAnimation.text, 32, 32);
            yield return null;
            Assert.AreEqual(32, _animatedImage.LottieAnimation.Texture.width);
            Assert.AreEqual(32, _animatedImage.LottieAnimation.Texture.height);
        }

        [UnityTest]
        public IEnumerator CheckDisposeFunctionality()
        {
            _animatedImage.LoadFromAnimationJson(_lottieAnimation.text, 32, 32);
            yield return null;
            _animatedImage.DisposeLottieAnimation();
            Assert.IsNull(_animatedImage.LottieAnimation);
        }
    }
}
