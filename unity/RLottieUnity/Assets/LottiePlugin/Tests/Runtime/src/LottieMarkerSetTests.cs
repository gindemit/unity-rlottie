using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace LottiePlugin.Tests.Runtime
{
    public sealed class LottieMarkerSetTests
    {
        [Test]
        public void ParsesFractionalMarkersAndMapsNonZeroInPoint()
        {
            TextAsset fixture = Resources.Load<TextAsset>("marker_nonzero_ip");
            Assert.NotNull(fixture);
            LottieMarkerSet markers = LottieMarkerSet.Parse(fixture.text, 11);

            Assert.AreEqual(30, markers.FrameRate);
            Assert.AreEqual(10.5, markers.TimelineInPoint);
            Assert.AreEqual(3, markers.Markers.Count);
            LottieMarker idle = markers.Get("idle");
            Assert.AreEqual(0, idle.FirstRenderFrame);
            Assert.AreEqual(1, idle.LastRenderFrameInclusive);
            Assert.AreEqual(0, markers.Frame("idle", 0));
            Assert.AreEqual(1, markers.Frame("idle", 1));
            Assert.AreEqual(2, markers.Get("escaped \" 花").FirstRenderFrame);
            Assert.AreEqual(10, markers.Get("point").FirstRenderFrame, "Point marker clamps to rlottie's final frame.");
            LottieMarker ignored;
            Assert.IsFalse(markers.TryGet("missing", out ignored));
            Assert.Throws<KeyNotFoundException>(() => markers.Get("missing"));
        }

        [TestCase("{\"ip\":0,\"op\":2,\"markers\":[]}")]
        [TestCase("{\"fr\":0,\"ip\":0,\"op\":2,\"markers\":[]}")]
        [TestCase("{\"fr\":30,\"ip\":0,\"op\":2,\"markers\":[{\"cm\":\"x\",\"tm\":0}]}")]
        [TestCase("{\"fr\":30,\"ip\":0,\"op\":2,\"markers\":[{\"cm\":\"x\",\"tm\":0,\"dr\":1},{\"cm\":\"x\",\"tm\":1,\"dr\":1}]}")]
        [TestCase("{\"fr\":30,\"ip\":0,\"op\":2,\"markers\":[{\"cm\":\"x\",\"tm\":0.1,\"dr\":0.1}]}")]
        public void RejectsMalformedMetadata(string json)
        {
            Assert.Throws<FormatException>(() => LottieMarkerSet.Parse(json, 3));
        }
    }
}
