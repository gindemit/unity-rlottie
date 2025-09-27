#if UNITY_EDITOR
using System.IO;
using LSCore;
using UnityEditor.AssetImporters;
using UnityEngine;

[ScriptedImporter(1, new []{"lottie", "ziplottie"})]
public class LottieScriptedImporter : ScriptedImporter
{
    [SerializeField] public LSImage.RotationMode rotation;
    [SerializeField] public Vector2Int flip;
    
    public override void OnImportAsset(AssetImportContext ctx)
    {
        var ext = Path.GetExtension(ctx.assetPath);
        Object asset;
        
        if (ext == ".ziplottie")
        {
            var lottie = ScriptableObject.CreateInstance<LottieAsset>();
            lottie.data = File.ReadAllBytes(ctx.assetPath);
            asset = lottie;
        }
        else
        {
            var lottie = ScriptableObject.CreateInstance<RawLottieAsset>();
            lottie.json = File.ReadAllText(ctx.assetPath);
            asset = lottie;
        }
        
        var baseAsset = (BaseLottieAsset)asset;
        baseAsset.rotation = rotation;
        baseAsset.flip = flip;
        
        asset.name = Path.GetFileNameWithoutExtension(ctx.assetPath);
        ctx.AddObjectToAsset("LottieAsset", asset);
        ctx.SetMainObject(asset);
    }
}
#endif