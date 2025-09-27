using UnityEngine;

public class RawLottieAsset : BaseRegularLottieAsset
{
    [TextArea(10, 30)]
    [SerializeField] [HideInInspector] internal string json;
    public override string Json => json;
}