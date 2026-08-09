using System.IO;

namespace LottiePlugin.Support
{
    public static class StreamingAssetsHelper
    {
        public static byte[] LoadFileFromStreamingAssets(string streamingAssetsFilePath)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var loadingRequest = UnityEngine.Networking.UnityWebRequest.Get(streamingAssetsFilePath))
            {
                loadingRequest.SendWebRequest();
                while (!loadingRequest.isDone)
                {
                }

#if UNITY_2020_2_OR_NEWER
                bool failed = loadingRequest.result != UnityEngine.Networking.UnityWebRequest.Result.Success;
#else
                bool failed = loadingRequest.isNetworkError || loadingRequest.isHttpError;
#endif
                if (failed)
                {
                    throw new System.InvalidOperationException(
                        $"Failed to load file at path \"{streamingAssetsFilePath}\", responseCode: \"{loadingRequest.responseCode}\", error: \"{loadingRequest.error}\"");
                }
                return loadingRequest.downloadHandler.data;
            }
#else
            return File.ReadAllBytes(streamingAssetsFilePath);
#endif
        }
    }
}
