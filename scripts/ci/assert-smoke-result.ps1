function Assert-LottieSmokeResult {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [psobject] $Result,
        [Parameter(Mandatory = $true)]
        [string] $Platform,
        [string] $ExpectedGraphicsApi,
        [string] $ExpectedColorSpace,
        [string] $ExpectedUploadBackend,
        [string] $ExpectedGraphicsVendor,
        [int] $MinimumSchemaVersion,
        [int] $ExpectedLifecycleCycles,
        [string[]] $RequiredCheckNames,
        [switch] $RequireGraphicsDeviceMetadata,
        [switch] $RequireNativeUpload,
        [switch] $RequireNativeVulkanUpload
    )

    if ($Result.passed -isnot [bool] -or $Result.checks -isnot [array] -or $Result.checks.Count -eq 0) {
        throw "$Platform smoke result must contain a boolean verdict and a nonempty list of checks."
    }
    $seen = @{}
    foreach ($check in $Result.checks) {
        if ($check.name -isnot [string] -or [string]::IsNullOrWhiteSpace($check.name) -or
            $check.passed -isnot [bool] -or $seen.ContainsKey($check.name)) {
            throw "$Platform smoke result has an invalid or duplicate check '$($check.name)'."
        }
        $seen[$check.name] = $true
    }
    $failedChecks = @($Result.checks | Where-Object { -not $_.passed })
    if (-not $Result.passed -or $failedChecks.Count -gt 0) {
        $details = @($Result.error | Where-Object { $_ }) + @($failedChecks | ForEach-Object { "$($_.name): $($_.details)" })
        throw "$Platform rendered-player smoke test failed:`n$($details -join "`n")"
    }

    if ($MinimumSchemaVersion -gt 0 -and [int] $Result.schemaVersion -lt $MinimumSchemaVersion) {
        throw "$Platform smoke result uses an outdated schema: required>=$MinimumSchemaVersion, actual=$($Result.schemaVersion)."
    }
    if ($MinimumSchemaVersion -ge 4 -or $ExpectedLifecycleCycles -gt 0) {
        $RequiredCheckNames = @($RequiredCheckNames) + @(
            'animatedButtonFound', 'animatedImageFound', 'animatedButtonLoaded', 'animatedImageLoaded',
            'animatedButtonStartsAtFirstFrame', 'animatedImageStartsAtFirstFrame', 'animatedImagePlayOnAwake',
            'animatedButtonFirstFrameVisible', 'animatedImageFirstFrameVisible', 'animatedImageAdvances',
            'animatedImagePixelsChange', 'animatedButtonAcceptsPress', 'animatedButtonAdvancesAfterPress',
            'animatedButtonPixelsChangeAfterPress', 'exactColorCalibration', 'semanticColorOverrides',
            'animationLifecycleStress', 'asyncFrameMatchesSync', 'disposedOutputTexturesReleased'
        )
    }
    foreach ($requiredCheckName in @($RequiredCheckNames | Where-Object { $_ })) {
        if (-not $seen.ContainsKey($requiredCheckName)) {
            throw "$Platform smoke result is missing required check '$requiredCheckName'."
        }
    }
    if ($ExpectedLifecycleCycles -gt 0 -and (
        ($Result.completedLifecycleCycles -isnot [int] -and $Result.completedLifecycleCycles -isnot [long]) -or
        $Result.completedLifecycleCycles -ne $ExpectedLifecycleCycles)) {
        throw "$Platform smoke result completed $($Result.completedLifecycleCycles) lifecycle cycles, expected $ExpectedLifecycleCycles."
    }

    $animatedImageBackend = [string] $Result.animatedImageUploadBackend
    $animatedButtonBackend = [string] $Result.animatedButtonUploadBackend
    if (-not $animatedImageBackend -or -not $animatedButtonBackend) {
        throw "$Platform smoke result is missing upload-backend metadata: AnimatedImage=$animatedImageBackend, AnimatedButton=$animatedButtonBackend."
    }
    if ($ExpectedGraphicsApi -and $Result.graphicsApi -ne $ExpectedGraphicsApi) {
        throw "$Platform smoke test used an unexpected graphics API: expected=$ExpectedGraphicsApi, actual=$($Result.graphicsApi)."
    }
    if ($ExpectedColorSpace -and $Result.colorSpace -ne $ExpectedColorSpace) {
        throw "$Platform smoke test used an unexpected color space: expected=$ExpectedColorSpace, actual=$($Result.colorSpace)."
    }
    if ($ExpectedGraphicsVendor -and $Result.graphicsDeviceVendor -notlike $ExpectedGraphicsVendor) {
        throw "$Platform smoke test used an unexpected graphics vendor: expected=$ExpectedGraphicsVendor, actual=$($Result.graphicsDeviceVendor)."
    }
    if ($RequireGraphicsDeviceMetadata -and
        (-not $Result.graphicsDevice -or -not $Result.graphicsDeviceVendor -or
        -not $Result.graphicsDeviceVersion -or -not $Result.operatingSystem)) {
        throw "$Platform smoke result is missing graphics-device metadata."
    }
    if ($ExpectedUploadBackend -and
        ($animatedImageBackend -ne $ExpectedUploadBackend -or $animatedButtonBackend -ne $ExpectedUploadBackend)) {
        throw "$Platform smoke test used an unexpected upload backend: expected=$ExpectedUploadBackend, AnimatedImage=$animatedImageBackend, AnimatedButton=$animatedButtonBackend."
    }

    if ($RequireNativeUpload) {
        $nativeBackends = @('NativeVulkan', 'NativeExternalTexture', 'NativeOpenGL', 'NativeWebGL')
        if ($nativeBackends -notcontains $animatedImageBackend -or $nativeBackends -notcontains $animatedButtonBackend) {
            throw "$Platform smoke test requires a native upload backend: AnimatedImage=$animatedImageBackend, AnimatedButton=$animatedButtonBackend."
        }
    }

    if ($RequireNativeVulkanUpload -and
        ($Result.graphicsApi -ne 'Vulkan' -or $animatedImageBackend -ne 'NativeVulkan' -or
        $animatedButtonBackend -ne 'NativeVulkan')) {
        throw "$Platform player did not use native Vulkan upload: graphicsApi=$($Result.graphicsApi), AnimatedImage=$animatedImageBackend, AnimatedButton=$animatedButtonBackend."
    }
}
