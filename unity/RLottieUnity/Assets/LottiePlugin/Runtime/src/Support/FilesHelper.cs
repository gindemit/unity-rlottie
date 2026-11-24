using System;
using System.IO;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace LottiePlugin.Support
{
    public static class FilesHelper
    {
        private const string JSON_EXTENTION = ".json";
        private const int MaxSnapshotBytes = 1024 * 1024;
        private static readonly object sSnapshotLock = new object();
        private static readonly (string Path, byte[] Bytes, string Json)[] sRecentSnapshots = new (string, byte[], string)[4];
        private static int sNextSnapshot;

        internal static string ReadAnimationFile(string path, out string resourcesPath)
        {
            string fullPath = Path.GetFullPath(path);
            // rlottie concatenates this directory with asset paths verbatim.
            resourcesPath = fullPath.Substring(0, fullPath.Length - Path.GetFileName(fullPath).Length);
            byte[] bytes;
            using (var stream = File.OpenRead(fullPath))
            {
                if (!stream.CanSeek)
                    return ReadJson(stream);

                long length = stream.Length;
                if (length > MaxSnapshotBytes)
                    return ReadJson(stream);

                bytes = new byte[(int)length];
                int offset = 0;
                while (offset < bytes.Length)
                {
                    int read = stream.Read(bytes, offset, bytes.Length - offset);
                    if (read == 0) break;
                    offset += read;
                }
                if (offset != bytes.Length || stream.ReadByte() != -1)
                {
                    // A writer may change the length on filesystems that allow
                    // shared writes. Read to EOF from the same open file.
                    stream.Position = 0;
                    return ReadJson(stream);
                }
            }

            // An exact byte match skips decoding and reuses the string identity
            // in the marker parser's weak cache. File timestamps can repeat.
            lock (sSnapshotLock)
            {
                for (int i = 0; i < sRecentSnapshots.Length; i++)
                {
                    var previous = sRecentSnapshots[i];
                    if (!string.Equals(previous.Path, fullPath, StringComparison.Ordinal))
                        continue;
                    if (BytesEqual(previous.Bytes, bytes))
                        return previous.Json;

                    return SaveSnapshot(i, fullPath, bytes);
                }

                string json = SaveSnapshot(sNextSnapshot, fullPath, bytes);
                sNextSnapshot = (sNextSnapshot + 1) % sRecentSnapshots.Length;
                return json;
            }
        }

        private static string SaveSnapshot(int slot, string path, byte[] bytes)
        {
            string json = ReadJson(new MemoryStream(bytes, writable: false));
            sRecentSnapshots[slot] = (path, bytes, json);
            return json;
        }

        private static string ReadJson(Stream stream)
        {
            using (var reader = new StreamReader(stream)) return reader.ReadToEnd();
        }

        private static unsafe bool BytesEqual(byte[] left, byte[] right)
        {
            if (left.Length != right.Length) return false;
            if (left.Length == 0) return true;
            fixed (byte* first = left, second = right)
                return UnsafeUtility.MemCmp(first, second, left.Length) == 0;
        }

        public static void CopyAnimationsJsonsFromStreamingAssetsToPersistentData(string[] animations)
        {
            for (int i = 0; i < animations.Length; ++i)
            {
                string fileName = animations[i] + JSON_EXTENTION;
                string jsonFilePath = Path.Combine(Application.streamingAssetsPath, fileName);
                string targetFilePath = Path.Combine(Application.persistentDataPath, fileName);
                if (!File.Exists(targetFilePath))
                {
                    CopyFileFromStreamingAssetsToPersistentData(jsonFilePath, targetFilePath);
                }
            }
        }
        public static void CopyFileFromStreamingAssetsToPersistentData(string streamingAssetsFilePath, string targetFilePath)
        {
            byte[] file = StreamingAssetsHelper.LoadFileFromStreamingAssets(streamingAssetsFilePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFilePath));
            File.WriteAllBytes(targetFilePath, file);
        }
        public static string[] GetPersistentAnimationsPaths(string[] animations)
        {
            string[] toReturn = new string[animations.Length];
            for (int i = 0; i < animations.Length; ++i)
            {
                toReturn[i] = GetPersistentAnimationPath(animations[i]);
            }
            return toReturn;
        }
        public static string GetPersistentAnimationPath(string animation)
        {
            return Path.Combine(Application.persistentDataPath, animation + JSON_EXTENTION);
        }
    }
}
