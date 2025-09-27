using System.IO;
using System.IO.Compression;
using System.Text;

public static class LottieCompressor
{
    public static void ToNewFile(string inputPath, string outputPath)
    {
        using (var inputStream = new FileStream(inputPath, FileMode.Open, FileAccess.Read))
        using (var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress))
        using (var outputStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
        {
            gzipStream.CopyTo(outputStream);
        }
    }
    
    public static string Decompress(string filePath)
    {
        using (var inputStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
        using (var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress))
        using (var reader = new StreamReader(gzipStream))
        {
            return reader.ReadToEnd();
        }
    }
    
    public static string Decompress(byte[] rawData)
    {
        using (var inputStream = new MemoryStream(rawData))
        using (var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress))
        using (var reader = new StreamReader(gzipStream))
        {
            return reader.ReadToEnd();
        }
    }
    
    public static byte[] Compress(string json)
    {
        var inputBytes = Encoding.UTF8.GetBytes(json);
        using var outputStream = new MemoryStream();
        using (var gzipStream = new GZipStream(outputStream, CompressionMode.Compress, leaveOpen: true))
        {
            gzipStream.Write(inputBytes, 0, inputBytes.Length);
        }
        return outputStream.ToArray();
    }
}