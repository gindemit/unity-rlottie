using UnityEngine;

namespace LottiePlugin
{
    internal static class LottieScheduler
    {
        public static int MaxAsyncStartsPerFrame = Mathf.Max(SystemInfo.processorCount * 2, 16);
        public static int MaxUploadsPerFrame = Mathf.Max(SystemInfo.processorCount * 4, 32);

        private static int sStarted;

        internal static void ResetFrameBudget()
        {
            sStarted = 0;
        }

        internal static bool TryStartRender()
        {
            if (sStarted >= MaxAsyncStartsPerFrame)
            {
                return false;
            }

            sStarted++;
            return true;
        }
    }

    [DefaultExecutionOrder(int.MaxValue)]
    internal sealed class LottieSchedulerReset : MonoBehaviour
    {
        private void LateUpdate()
        {
            LottieScheduler.ResetFrameBudget();
        }
    }
}
