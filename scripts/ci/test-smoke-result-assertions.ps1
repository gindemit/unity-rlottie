[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'assert-smoke-result.ps1')

function New-SmokeResult {
    param(
        [string] $GraphicsApi = 'Direct3D11',
        [string] $AnimatedImageBackend = 'NativeExternalTexture',
        [string] $AnimatedButtonBackend = 'NativeExternalTexture',
        [string] $ColorSpace = 'Linear',
        [bool] $Passed = $true,
        [object[]] $Checks = @([pscustomobject]@{ name = 'render'; passed = $true; details = 'ok' })
    )

    [pscustomobject]@{
        schemaVersion = 2
        passed = $Passed
        checks = $Checks
        graphicsApi = $GraphicsApi
        colorSpace = $ColorSpace
        graphicsDevice = 'Test GPU'
        graphicsDeviceVendor = 'Test Vendor'
        graphicsDeviceVersion = 'Vulkan 1.3'
        operatingSystem = 'Test OS'
        animatedImageUploadBackend = $AnimatedImageBackend
        animatedButtonUploadBackend = $AnimatedButtonBackend
    }
}

function Assert-Throws {
    param(
        [Parameter(Mandatory = $true)]
        [scriptblock] $Action,
        [Parameter(Mandatory = $true)]
        [string] $ExpectedMessage
    )

    try {
        & $Action
    }
    catch {
        if ($_.Exception.Message -notlike "*$ExpectedMessage*") {
            throw "Expected error containing '$ExpectedMessage', got '$($_.Exception.Message)'."
        }
        return
    }
    throw "Expected an error containing '$ExpectedMessage', but no error was thrown."
}

Assert-LottieSmokeResult -Result (New-SmokeResult) -Platform Windows `
    -ExpectedGraphicsApi Direct3D11 -ExpectedColorSpace Linear `
    -ExpectedUploadBackend NativeExternalTexture -ExpectedGraphicsVendor 'Test*' `
    -MinimumSchemaVersion 2 -RequiredCheckNames render `
    -RequireGraphicsDeviceMetadata -RequireNativeUpload
Assert-LottieSmokeResult -Result (New-SmokeResult -GraphicsApi Vulkan `
    -AnimatedImageBackend NativeVulkan -AnimatedButtonBackend NativeVulkan) -Platform Android `
    -ExpectedGraphicsApi Vulkan -ExpectedUploadBackend NativeVulkan -RequireNativeVulkanUpload
# Wrapper scripts forward omitted optional values as empty strings. The shared
# assertion must treat that value as "no expectation" instead of rejecting it
# during parameter binding.
Assert-LottieSmokeResult -Result (New-SmokeResult) -Platform Windows `
    -ExpectedColorSpace '' -MinimumSchemaVersion 0

Assert-Throws { Assert-LottieSmokeResult -Result (New-SmokeResult -Checks @(
            [pscustomobject]@{ name = 'render'; passed = $false; details = 'failed' })) -Platform Windows } `
    'render: failed'
Assert-Throws { Assert-LottieSmokeResult -Result (New-SmokeResult -AnimatedButtonBackend '') -Platform Windows } `
    'missing upload-backend metadata'
Assert-Throws { Assert-LottieSmokeResult -Result (New-SmokeResult) -Platform Windows `
        -ExpectedGraphicsApi Direct3D12 } 'unexpected graphics API'
Assert-Throws { Assert-LottieSmokeResult -Result (New-SmokeResult) -Platform Windows `
        -ExpectedColorSpace Gamma } 'unexpected color space'
Assert-Throws { Assert-LottieSmokeResult -Result (New-SmokeResult) -Platform Windows `
        -ExpectedGraphicsVendor 'Other*' } 'unexpected graphics vendor'
Assert-Throws {
    $oldSchema = New-SmokeResult
    $oldSchema.schemaVersion = 1
    Assert-LottieSmokeResult -Result $oldSchema -Platform Windows -MinimumSchemaVersion 2
} 'outdated schema'
Assert-Throws { Assert-LottieSmokeResult -Result (New-SmokeResult) -Platform Windows `
        -RequiredCheckNames exactColorCalibration } "missing required check 'exactColorCalibration'"
Assert-Throws {
    $missingMetadata = New-SmokeResult
    $missingMetadata.graphicsDeviceVersion = ''
    Assert-LottieSmokeResult -Result $missingMetadata -Platform Windows -RequireGraphicsDeviceMetadata
} 'missing graphics-device metadata'
Assert-Throws { Assert-LottieSmokeResult -Result (New-SmokeResult -AnimatedImageBackend ManagedTextureUpload) `
        -Platform Windows -ExpectedUploadBackend NativeExternalTexture } 'unexpected upload backend'
Assert-Throws { Assert-LottieSmokeResult -Result (New-SmokeResult -AnimatedButtonBackend ManagedTextureUpload) `
        -Platform Windows -RequireNativeUpload } 'requires a native upload backend'
Assert-Throws { Assert-LottieSmokeResult -Result (New-SmokeResult -AnimatedButtonBackend Unknown) `
        -Platform Windows -RequireNativeUpload } 'requires a native upload backend'
Assert-Throws { Assert-LottieSmokeResult -Result (New-SmokeResult -GraphicsApi Vulkan `
        -AnimatedImageBackend NativeVulkan -AnimatedButtonBackend ManagedTextureUpload) -Platform Android `
        -RequireNativeVulkanUpload } 'did not use native Vulkan upload'

Assert-Throws { Assert-LottieSmokeResult -Result (New-SmokeResult -Checks @()) -Platform Android } 'nonempty list'
Assert-Throws {
    $malformed = New-SmokeResult
    $malformed.checks[0].passed = 'false'
    Assert-LottieSmokeResult -Result $malformed -Platform Android
} 'invalid or duplicate'
Assert-Throws {
    $duplicate = New-SmokeResult
    $duplicate.checks += $duplicate.checks[0]
    Assert-LottieSmokeResult -Result $duplicate -Platform Android
} 'invalid or duplicate'
$current = New-SmokeResult -Checks @(
    foreach ($name in @(
        'animatedButtonFound', 'animatedImageFound', 'animatedButtonLoaded', 'animatedImageLoaded',
        'animatedButtonStartsAtFirstFrame', 'animatedImageStartsAtFirstFrame', 'animatedImagePlayOnAwake',
        'animatedButtonFirstFrameVisible', 'animatedImageFirstFrameVisible', 'animatedImageAdvances',
        'animatedImagePixelsChange', 'animatedButtonAcceptsPress', 'animatedButtonAdvancesAfterPress',
        'animatedButtonPixelsChangeAfterPress', 'exactColorCalibration', 'semanticColorOverrides',
        'animationLifecycleStress', 'asyncFrameMatchesSync', 'disposedOutputTexturesReleased'
    )) {
        [pscustomobject]@{ name = $name; passed = $true; details = 'ok' }
    }
)
$current.schemaVersion = 4
$current | Add-Member -NotePropertyName completedLifecycleCycles -NotePropertyValue 12
Assert-LottieSmokeResult -Result $current -Platform Android -MinimumSchemaVersion 4 -ExpectedLifecycleCycles 12
Assert-Throws { Assert-LottieSmokeResult -Result $current -Platform Android -ExpectedLifecycleCycles 24 } 'expected 24'
$current.completedLifecycleCycles = '12'
Assert-Throws { Assert-LottieSmokeResult -Result $current -Platform Android -ExpectedLifecycleCycles 12 } 'expected 12'
$current.checks = @($current.checks | Where-Object name -ne asyncFrameMatchesSync)
Assert-Throws { Assert-LottieSmokeResult -Result $current -Platform Android -MinimumSchemaVersion 4 } "missing required check 'asyncFrameMatchesSync'"
$current.checks = @($current.checks | Where-Object name -ne animatedButtonFirstFrameVisible)
Assert-Throws { Assert-LottieSmokeResult -Result $current -Platform Android -MinimumSchemaVersion 4 } "missing required check 'animatedButtonFirstFrameVisible'"

Write-Output 'Smoke-result assertion tests passed.'
