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
Assert-LottieSmokeResult -Result (New-SmokeResult) -Platform Windows -ExpectedColorSpace ''

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
        -Platform Windows -RequireNativeUpload } 'managed upload backend'
Assert-Throws { Assert-LottieSmokeResult -Result (New-SmokeResult -GraphicsApi Vulkan `
        -AnimatedImageBackend NativeVulkan -AnimatedButtonBackend ManagedTextureUpload) -Platform Android `
        -RequireNativeVulkanUpload } 'did not use native Vulkan upload'

Write-Output 'Smoke-result assertion tests passed.'
