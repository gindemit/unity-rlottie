using LSCore;
using LSCore.Extensions;
using UnityEngine;

public abstract class BaseLottieAsset : ScriptableObject
{
    public abstract string Json { get; }
    public LSImage.RotationMode rotation;
    public Vector2Int flip;
    [SerializeField] private float aspect = float.NegativeInfinity;

    public float Aspect
    {
        get
        {
            if (float.IsNegativeInfinity(aspect))
            {
                var size = Lottie.GetSize(Json);
                aspect = (float)size.x /  size.y;
            }
            
            return aspect;
        }
    }
    
    protected internal virtual bool IsCompressed => false;
    protected internal abstract string CompressedExtension { get; }
    protected internal abstract string DecompressedExtension { get; }
    
    public void SetupImage(LottieImage image)
    {
        image.PreserveAspectRatio = true;
        image.manager.asset = this;
        image.Rotation = rotation;
        image.Flip = (flip.x.ToBool(),  flip.y.ToBool());
    }
    
    public void SetupRenderer(LottieRenderer renderer)
    {
        renderer.manager.asset = this;
        renderer.Rotation = rotation;
        renderer.Flip = (flip.x.ToBool(),  flip.y.ToBool());
    }
}

public abstract class BaseRegularLottieAsset : BaseLottieAsset
{
    protected internal override string CompressedExtension => ".ziplottie";
    protected internal override string DecompressedExtension => ".lottie";
}

public class LottieAsset : BaseRegularLottieAsset
{
    [SerializeField] internal byte[] data;
    private string json;
    public override string Json => string.IsNullOrEmpty(json) ? json = LottieCompressor.Decompress(data) : json;
    protected internal override bool IsCompressed => true;

    public static LottieAsset Create(byte[] rawData)
    {
        var asset = CreateInstance<LottieAsset>();
        asset.data = rawData;
        return asset;
    }
}