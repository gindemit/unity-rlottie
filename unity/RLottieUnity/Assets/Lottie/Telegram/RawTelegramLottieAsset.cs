using UnityEngine;

public class RawTelegramLottieAsset : BaseTelegramLottieAsset
{
    [SerializeField] internal string json;
    public override string Json => json;
}