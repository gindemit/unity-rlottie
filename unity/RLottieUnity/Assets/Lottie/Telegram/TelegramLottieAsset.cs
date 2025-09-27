using UnityEngine;

public abstract class BaseTelegramLottieAsset : BaseLottieAsset
{
    protected override string CompressedExtension => ".tgs";
    protected override string DecompressedExtension => ".tglottie";
    
    protected static string SanitizeJson(string src)
    {
        const string tag = "\"tgs\":1,";
        int i = src.IndexOf(tag, System.StringComparison.Ordinal);
        src = src.Remove(i, tag.Length);
        return src;
    }
}

public class TelegramLottieAsset : BaseTelegramLottieAsset
{
    [SerializeField] internal byte[] data;
    private string json;
    public override string Json => string.IsNullOrEmpty(json) ? json = SanitizeJson(LottieCompressor.Decompress(data)) : json;
    protected override bool IsCompressed => true;
    
    public static TelegramLottieAsset Create(byte[] rawData)
    {
        var asset = CreateInstance<TelegramLottieAsset>();
        asset.data = rawData;
        return asset;
    }
}