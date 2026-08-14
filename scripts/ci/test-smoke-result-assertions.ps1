[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'assert-smoke-result.ps1')

function New-SmokeResult {
    param(
        [string] $GraphicsApi = 'Direct3D11',
        [string] $AnimatedImageBackend = 'NativeExternalTexture',
        [string] $AnimatedButtonBackend = 'NativeExternalTexture',
        [bool] $Passed = $true,
        [object[]] $Checks = @([pscustomobject]@{ name = 'render'; passed = $true; details = 'ok' })
    )

    [pscustomobject]@{
        passed = $Passed
        checks = $Checks
        graphicsApi = $GraphicsApi
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
    -ExpectedGraphicsApi Direct3D11 -ExpectedUploadBackend NativeExternalTexture -RequireNativeUpload
Assert-LottieSmokeResult -Result (New-SmokeResult -GraphicsApi Vulkan `
    -AnimatedImageBackend NativeVulkan -AnimatedButtonBackend NativeVulkan) -Platform Android `
    -ExpectedGraphicsApi Vulkan -ExpectedUploadBackend NativeVulkan -RequireNativeVulkanUpload

Assert-Throws { Assert-LottieSmokeResult -Result (New-SmokeResult -Checks @(
            [pscustomobject]@{ name = 'render'; passed = $false; details = 'failed' })) -Platform Windows } `
    'render: failed'
Assert-Throws { Assert-LottieSmokeResult -Result (New-SmokeResult -AnimatedButtonBackend '') -Platform Windows } `
    'missing upload-backend metadata'
Assert-Throws { Assert-LottieSmokeResult -Result (New-SmokeResult) -Platform Windows `
        -ExpectedGraphicsApi Direct3D12 } 'unexpected graphics API'
Assert-Throws { Assert-LottieSmokeResult -Result (New-SmokeResult -AnimatedImageBackend ManagedTextureUpload) `
        -Platform Windows -ExpectedUploadBackend NativeExternalTexture } 'unexpected upload backend'
Assert-Throws { Assert-LottieSmokeResult -Result (New-SmokeResult -AnimatedButtonBackend ManagedTextureUpload) `
        -Platform Windows -RequireNativeUpload } 'managed upload backend'
Assert-Throws { Assert-LottieSmokeResult -Result (New-SmokeResult -GraphicsApi Vulkan `
        -AnimatedImageBackend NativeVulkan -AnimatedButtonBackend ManagedTextureUpload) -Platform Android `
        -RequireNativeVulkanUpload } 'did not use native Vulkan upload'

Write-Output 'Smoke-result assertion tests passed.'
