using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using LottiePlugin.UI;
using System.Collections;
using System.Reflection;

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
        public IEnumerator PlaybackCompletesAsyncFrames()
        {
            SetField(_animatedImage, "_pauseIfCulled", false);
            _animatedImage.LoadFromAnimationJson(_lottieAnimation.text, 64, 64);
            _animatedImage.LottieAnimation.Play();
            int initialFrame = _animatedImage.LottieAnimation.CurrentFrame;
            IEnumerator playback = InvokeCoroutine(_animatedImage, "RenderLottieAnimationCoroutine");
            Assert.IsTrue(playback.MoveNext());
            float deadline = Time.realtimeSinceStartup + 5f;

            while ((_animatedImage.LottieAnimation.CurrentFrame == initialFrame ||
                    _animatedImage.LottieAnimation.AsyncDrawPending) &&
                   Time.realtimeSinceStartup < deadline)
            {
                if (!_animatedImage.LottieAnimation.AsyncDrawPending)
                {
                    SetAnimationField(_animatedImage.LottieAnimation, "_timeSinceLastRenderCall", 0.04f);
                }
                Assert.IsTrue(playback.MoveNext());
                yield return null;
            }

            Assert.AreNotEqual(initialFrame, _animatedImage.LottieAnimation.CurrentFrame);
            Assert.IsFalse(_animatedImage.LottieAnimation.AsyncDrawPending);
            Assert.IsTrue(HasVisibleOutputPixel(_animatedImage.LottieAnimation.OutputTexture));
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

        [UnityTest]
        public IEnumerator ManagedTextureUploadRendersAVisibleFrame()
        {
            LottieAnimation animation = LottieAnimation.LoadFromJsonData(
                _lottieAnimation.text,
                string.Empty,
                64,
                64,
                new LottieAnimationOptions
                {
                    UseManagedTextureUpload = true
                });

            try
            {
                Assert.AreEqual(LottieTextureUploadBackend.ManagedTextureUpload, animation.TextureUploadBackend);
                Assert.AreSame(animation.Texture, animation.OutputTexture);

                animation.DrawOneFrame(1);
                yield return null;

                Color32[] pixels = animation.Texture.GetPixels32();
                bool hasVisiblePixel = false;
                for (int i = 0; i < pixels.Length; i++)
                {
                    if (pixels[i].a > 8)
                    {
                        hasVisiblePixel = true;
                        break;
                    }
                }
                Assert.IsTrue(hasVisiblePixel, "Managed texture upload produced a fully transparent frame.");
            }
            finally
            {
                animation.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator RepeatedManagedAsyncPrepareAndResultAdvancesFramesAndPresentsPixels()
        {
            LottieAnimation animation = LottieAnimation.LoadFromJsonData(
                _lottieAnimation.text,
                string.Empty,
                128,
                128,
                new LottieAnimationOptions
                {
                    UseManagedTextureUpload = true
                });

            try
            {
                int[] frames = { 1, 5, 10 };
                for (int i = 0; i < frames.Length; i++)
                {
                    animation.DrawOneFrameAsyncPrepare(frames[i]);
                    animation.DrawOneFrameAsyncGetResult();

                    Assert.AreEqual(frames[i], animation.CurrentFrame);
                    Assert.IsTrue(
                        HasVisibleOutputPixel(animation.OutputTexture),
                        $"Managed async frame {frames[i]} was not presented to the output texture.");
                    yield return null;
                }
            }
            finally
            {
                animation.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator SynchronousDrawCompletesPendingAsyncFrameFirst()
        {
            LottieAnimation animation = LottieAnimation.LoadFromJsonData(
                _lottieAnimation.text,
                string.Empty,
                64,
                64,
                new LottieAnimationOptions
                {
                    UseManagedTextureUpload = true
                });

            try
            {
                animation.DrawOneFrameAsyncPrepare(2);
                Assert.IsTrue(animation.AsyncDrawPending);

                animation.DrawOneFrame(3);

                Assert.IsFalse(animation.AsyncDrawPending);
                Assert.AreEqual(3, animation.CurrentFrame);
                Assert.IsTrue(HasVisibleOutputPixel(animation.OutputTexture));
                yield return null;
            }
            finally
            {
                animation.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator AnimatedImageBindsItsOutputTexture()
        {
            _animatedImage.LoadFromAnimationJson(_lottieAnimation.text, 32, 32);
            yield return null;

            Assert.AreSame(
                _animatedImage.LottieAnimation.OutputTexture,
                _animatedImage.RawImage.texture);
        }

        private static bool HasVisibleOutputPixel(Texture texture)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture renderTexture = RenderTexture.GetTemporary(
                texture.width,
                texture.height,
                0,
                RenderTextureFormat.ARGB32);
            Texture2D readback = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
            try
            {
                Graphics.Blit(texture, renderTexture);
                RenderTexture.active = renderTexture;
                readback.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                readback.Apply();

                Color32[] pixels = readback.GetPixels32();
                for (int i = 0; i < pixels.Length; i++)
                {
                    if (pixels[i].a > 8)
                    {
                        return true;
                    }
                }

                return false;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(renderTexture);
                Object.DestroyImmediate(readback);
            }
        }

        private static void SetField<T>(AnimatedImage image, string fieldName, T value)
        {
            FieldInfo field = typeof(AnimatedImage).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field, $"Could not find AnimatedImage.{fieldName}.");
            field.SetValue(image, value);
        }

        private static void SetAnimationField<T>(LottieAnimation animation, string fieldName, T value)
        {
            FieldInfo field = typeof(LottieAnimation).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field, $"Could not find LottieAnimation.{fieldName}.");
            field.SetValue(animation, value);
        }

        private static IEnumerator InvokeCoroutine(AnimatedImage image, string methodName)
        {
            MethodInfo method = typeof(AnimatedImage).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method, $"Could not find AnimatedImage.{methodName}.");
            return (IEnumerator)method.Invoke(image, null);
        }
    }
}
