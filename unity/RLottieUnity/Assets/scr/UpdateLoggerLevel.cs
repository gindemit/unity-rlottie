using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UpdateLoggerLevel : MonoBehaviour
{
    private void Awake()
    {
        LottiePlugin.LottieAnimation.SetGlobalLogLevel(LottiePlugin.LottieLogLevel.Info);
    }
}
