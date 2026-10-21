using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UpdateLoggerLevel : MonoBehaviour
{
    [SerializeField] private LottiePlugin.LottieLogLevel _logLevel = LottiePlugin.LottieLogLevel.Warning;

    private void Awake()
    {
        LottiePlugin.LottieAnimation.SetGlobalLogLevel(_logLevel);
    }
}
