using System.IO;
namespace Support.StreamingAssets
{
    public static class StreamingAssetsHelper
    {
        public static byte[] LoadFileFromStreamingAssets(string streamingAssetsFilePath)
        {
            string finalPath = Path.Combine(UnityEngine.Application.streamingAssetsPath, streamingAssetsFilePath);
#if UNITY_ANDROID && !UNITY_EDITOR
            var loadingRequest = UnityEngine.Networking.UnityWebRequest.Get(finalPath);
            loadingRequest.SendWebRequest();
            while (!loadingRequest.isDone)
            {
                if (loadingRequest.result == UnityEngine.Networking.UnityWebRequest.Result.ConnectionError ||
                    loadingRequest.result == UnityEngine.Networking.UnityWebRequest.Result.DataProcessingError ||
                    loadingRequest.result == UnityEngine.Networking.UnityWebRequest.Result.ProtocolError)
                {
                    break;
                }
            }
            if (loadingRequest.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                throw new System.InvalidOperationException(
                    $"Failed to load file at path \"{finalPath}\", result: \"{loadingRequest.result}\"");
            }
            return loadingRequest.downloadHandler.data;
#else
            return File.ReadAllBytes(finalPath);
#endif
        }
    }
}