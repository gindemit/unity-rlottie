#if UNITY_EDITOR
using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;

[ScriptedImporter(1, exts: new []{"tgs", "tglottie"})]
public sealed class TelegramLottieScriptedImporter : LottieScriptedImporter
{
    public override void OnImportAsset(AssetImportContext ctx)
    {
        var ext = Path.GetExtension(ctx.assetPath);
        Object asset;
        
        if (ext == ".tgs")
        {
            var lottie = ScriptableObject.CreateInstance<TelegramLottieAsset>();
            lottie.data = File.ReadAllBytes(ctx.assetPath);
            asset = lottie;
        }
        else
        {
            var lottie = ScriptableObject.CreateInstance<RawTelegramLottieAsset>();
            lottie.json = File.ReadAllText(ctx.assetPath);
            asset = lottie;
        }
        
        var baseAsset = (BaseLottieAsset)asset;
        baseAsset.rotation = rotation;
        baseAsset.flip = flip;
        
        asset.name = Path.GetFileNameWithoutExtension(ctx.assetPath);
        ctx.AddObjectToAsset("TelegramLottieAsset", asset);
        ctx.SetMainObject(asset);
    }
}
#endif