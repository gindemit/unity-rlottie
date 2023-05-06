using System.IO;

namespace LottiePlugin.Support
{
    public static class StreamingAssetsHelper
    {
        public static byte[] LoadFileFromStreamingAssets(string streamingAssetsFilePath)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            var loadingRequest = UnityEngine.Networking.UnityWebRequest.Get(streamingAssetsFilePath);
            loadingRequest.SendWebRequest();
            while (!loadingRequest.isDone)
            {
                if (loadingRequest.isNetworkError)
                {
                    break;
                }
            }
            if (!loadingRequest.isDone)
            {
                throw new System.InvalidOperationException(
                    $"Failed to load file at path \"{streamingAssetsFilePath}\", responseCode: \"{loadingRequest.responseCode}\"");
            }
            return loadingRequest.downloadHandler.data;
#else
            return File.ReadAllBytes(streamingAssetsFilePath);
#endif
        }
    }
}
