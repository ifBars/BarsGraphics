param(
    [string[]]$Profiles = @("Balanced"),
    [string]$Location = "townhall",
    [int]$SampleSeconds = 15,
    [int]$SettleSeconds = 3,
    [int]$TimeoutMs = 20000,
    [string]$OutputName = "live-candidate-samples"
)

$ErrorActionPreference = "Stop"

$tool = Join-Path $PSScriptRoot "live-control.ps1"
$rows = New-Object System.Collections.Generic.List[object]
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"

function Invoke-Live([hashtable]$payload, [int]$timeout = $TimeoutMs) {
    $json = $payload | ConvertTo-Json -Compress
    $result = & $tool $json -TimeoutMs $timeout
    if ([string]::IsNullOrWhiteSpace($result)) {
        throw "Live control returned an empty response for $json"
    }

    $parsed = $result | ConvertFrom-Json
    if ($parsed.ok -eq $false) {
        throw "Live control failed for $json :: $result"
    }

    return $parsed
}

function Apply-Profile([string]$profile) {
    if ($profile -eq "Off") {
        Invoke-Live @{ method = "restore" } | Out-Null
    }
    else {
        Invoke-Live @{ method = "apply"; profile = $profile } | Out-Null
    }
}

function Invoke-Candidate([string]$candidate) {
    switch ($candidate) {
        "baseline" {
            return
        }
        "renderScale080" {
            Invoke-Live @{ method = "setUrpAsset"; renderScale = 0.8 } | Out-Null
            return
        }
        "fsr080" {
            Invoke-Live @{ method = "setUrpAsset"; renderScale = 0.8; upscalingFilter = 3; fsrOverrideSharpness = $true; fsrSharpness = 0.8 } | Out-Null
            return
        }
        "shaderLod400" {
            Invoke-Live @{ method = "setShaderMaximumLod"; shaderMaximumLod = 400 } | Out-Null
            return
        }
        "anisotropicEnable" {
            Invoke-Live @{ method = "setAnisotropicFiltering"; mode = "Enable" } | Out-Null
            return
        }
        "terrainNoFoliage" {
            Invoke-Live @{ method = "setTerrainFoliage"; drawTreesAndFoliage = $false; detailObjectDistance = 0 } | Out-Null
            return
        }
        default {
            throw "Unknown candidate '$candidate'"
        }
    }
}

function Add-Row([string]$profile, [string]$candidate, [object]$before, [object]$sample, [object]$after, [object]$screenshot) {
    $rows.Add([pscustomobject]@{
        timestamp = (Get-Date).ToString("o")
        location = $Location
        profile = $profile
        candidate = $candidate
        avgFps = $sample.avg
        minFps = $sample.min
        maxFps = $sample.max
        beforeRuntimeRenderScale = $before.runtimeRenderScale
        afterRuntimeRenderScale = $after.runtimeRenderScale
        afterRuntimeShaderMaximumLod = $after.runtimeShaderMaximumLod
        afterRuntimeAnisotropicFiltering = $after.runtimeAnisotropicFiltering
        afterRuntimeMasterTextureLimit = $after.runtimeMasterTextureLimit
        screenshot = $screenshot.path
    })
}

$candidates = @(
    "baseline",
    "renderScale080",
    "fsr080",
    "shaderLod400",
    "anisotropicEnable",
    "terrainNoFoliage"
)

Invoke-Live @{ method = "showfps" } | Out-Null
Invoke-Live @{ method = "console"; command = "setweather clear" } | Out-Null
Invoke-Live @{ method = "console"; command = "settime 0400" } | Out-Null
Invoke-Live @{ method = "console"; command = "setpoliceignoreplayers true" } | Out-Null
Invoke-Live @{ method = "console"; command = "clearwanted" } | Out-Null
Invoke-Live @{ method = "console"; command = "teleport $Location" } | Out-Null
Start-Sleep -Seconds 2
Invoke-Live @{ method = "closeOffenceNotice" } | Out-Null
Invoke-Live @{ method = "restoreHud" } | Out-Null

foreach ($profile in $Profiles) {
    foreach ($candidate in $candidates) {
        Apply-Profile $profile
        Start-Sleep -Seconds $SettleSeconds

        $before = Invoke-Live @{ method = "state" }
        Invoke-Candidate $candidate
        Start-Sleep -Seconds $SettleSeconds
        Invoke-Live @{ method = "closeOffenceNotice" } | Out-Null
        Invoke-Live @{ method = "restoreHud" } | Out-Null

        $safeProfile = ($profile -replace '[^A-Za-z0-9_-]', '_')
        $safeCandidate = ($candidate -replace '[^A-Za-z0-9_-]', '_')
        $shotName = "$OutputName-$stamp-$Location-$safeProfile-$safeCandidate"
        $screenshot = Invoke-Live @{ method = "screenshot"; name = $shotName }
        Start-Sleep -Seconds 1

        $sample = Invoke-Live @{ method = "sampleFps"; seconds = $SampleSeconds } (($SampleSeconds + 20) * 1000)
        $after = Invoke-Live @{ method = "state" }
        Add-Row $profile $candidate $before $sample $after $screenshot
    }
}

Invoke-Live @{ method = "restore" } | Out-Null

$reportDir = Join-Path (Resolve-Path (Join-Path $PSScriptRoot "..")) "artifacts"
New-Item -ItemType Directory -Path $reportDir -Force | Out-Null
$csvPath = Join-Path $reportDir "$OutputName-$stamp.csv"
$jsonPath = Join-Path $reportDir "$OutputName-$stamp.json"
$rows | Export-Csv -NoTypeInformation -Path $csvPath
$rows | ConvertTo-Json -Depth 4 | Set-Content -Path $jsonPath -Encoding UTF8

[pscustomobject]@{
    csv = $csvPath
    json = $jsonPath
    rows = $rows.Count
}


