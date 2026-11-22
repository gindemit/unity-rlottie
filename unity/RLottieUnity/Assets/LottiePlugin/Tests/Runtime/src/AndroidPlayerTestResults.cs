#if UNITY_ANDROID && !UNITY_EDITOR
using System.IO;
using NUnit.Framework.Interfaces;
using UnityEngine;
using UnityEngine.Scripting;
using UnityEngine.TestRunner;

[assembly: TestRunCallback(typeof(LottiePlugin.Tests.AndroidPlayerTestResults))]

namespace LottiePlugin.Tests
{
    // Keep the completed player result available over USB if Unity's editor connection drops.
    [Preserve]
    public sealed class AndroidPlayerTestResults : ITestRunCallback
    {
        public void RunFinished(ITestResult testResults)
        {
            string path = Path.Combine(Application.persistentDataPath, "lottie-player-tests.xml");
            File.WriteAllText(path + ".tmp", testResults.ToXml(true).OuterXml);
            File.Delete(path);
            File.Move(path + ".tmp", path);
        }

        public void RunStarted(ITest testsToRun) { }
        public void TestStarted(ITest test) { }
        public void TestFinished(ITestResult result) { }
    }
}
#endif
