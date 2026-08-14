function Assert-LottieSmokeResult {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [psobject] $Result,
        [Parameter(Mandatory = $true)]
        [string] $Platform,
        [string] $ExpectedGraphicsApi,
        [string] $ExpectedUploadBackend,
        [switch] $RequireNativeUpload,
        [switch] $RequireNativeVulkanUpload
    )

    $failedChecks = @($Result.checks | Where-Object { -not $_.passed })
    if (-not $Result.passed -or $failedChecks.Count -gt 0) {
        $details = $failedChecks | ForEach-Object { "$($_.name): $($_.details)" }
        throw "$Platform rendered-player smoke test failed:`n$($details -join "`n")"
    }

    $animatedImageBackend = [string] $Result.animatedImageUploadBackend
    $animatedButtonBackend = [string] $Result.animatedButtonUploadBackend
    if (-not $animatedImageBackend -or -not $animatedButtonBackend) {
        throw "$Platform smoke result is missing upload-backend metadata: AnimatedImage=$animatedImageBackend, AnimatedButton=$animatedButtonBackend."
    }
    if ($ExpectedGraphicsApi -and $Result.graphicsApi -ne $ExpectedGraphicsApi) {
        throw "$Platform smoke test used an unexpected graphics API: expected=$ExpectedGraphicsApi, actual=$($Result.graphicsApi)."
    }
    if ($ExpectedUploadBackend -and
        ($animatedImageBackend -ne $ExpectedUploadBackend -or $animatedButtonBackend -ne $ExpectedUploadBackend)) {
        throw "$Platform smoke test used an unexpected upload backend: expected=$ExpectedUploadBackend, AnimatedImage=$animatedImageBackend, AnimatedButton=$animatedButtonBackend."
    }

    if ($RequireNativeUpload) {
        $managedBackends = @('ManagedTextureUpload', 'WebGLManagedTextureUpload', 'WebGLShaderConversion')
        if ($managedBackends -contains $animatedImageBackend -or
            $managedBackends -contains $animatedButtonBackend) {
            throw "$Platform smoke test used a managed upload backend: AnimatedImage=$animatedImageBackend, AnimatedButton=$animatedButtonBackend."
        }
    }

    if ($RequireNativeVulkanUpload -and
        ($Result.graphicsApi -ne 'Vulkan' -or $animatedImageBackend -ne 'NativeVulkan' -or
        $animatedButtonBackend -ne 'NativeVulkan')) {
        throw "$Platform player did not use native Vulkan upload: graphicsApi=$($Result.graphicsApi), AnimatedImage=$animatedImageBackend, AnimatedButton=$animatedButtonBackend."
    }
}
