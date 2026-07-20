param(
    [string[]]$Locations = @("townhall", "motel", "docks", "suburbia", "uptown"),
    [string[]]$Profiles = @("Off", "Balanced", "Aggressive"),
    [int]$SampleSeconds = 10,
    [int]$TimeoutMs = 20000,
    [string]$OutputName = "visual-confirmation"
)

$ErrorActionPreference = "Stop"

$tool = Join-Path $PSScriptRoot "live-control.ps1"
$rows = New-Object System.Collections.Generic.List[object]
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"

function Invoke-Live([string]$json, [int]$timeout = $TimeoutMs) {
    $result = & $tool $json -TimeoutMs $timeout
    if ([string]::IsNullOrWhiteSpace($result)) {
        throw "Live control returned an empty response for $json"
    }

    return $result | ConvertFrom-Json
}

function Add-Row([string]$location, [string]$profile, [object]$state, [object]$sample, [object]$screenshot) {
    $rows.Add([pscustomobject]@{
        timestamp = (Get-Date).ToString("o")
        location = $location
        profile = $profile
        avgFps = if ($sample) { $sample.avg } else { $null }
        minFps = if ($sample) { $sample.min } else { $null }
        maxFps = if ($sample) { $sample.max } else { $null }
        currentTime = $state.currentTime
        runtimeLodBias = $state.runtimeLodBias
        runtimeShadowDistance = $state.runtimeShadowDistance
        runtimeRenderScale = $state.runtimeRenderScale
        screenshot = if ($screenshot) { $screenshot.path } else { $null }
    })
}

Invoke-Live '{"method":"showfps"}' | Out-Null
Invoke-Live '{"method":"console","command":"setweather clear"}' | Out-Null
Invoke-Live '{"method":"console","command":"settime 0400"}' | Out-Null
Invoke-Live '{"method":"console","command":"setpoliceignoreplayers true"}' | Out-Null
Invoke-Live '{"method":"console","command":"clearwanted"}' | Out-Null

foreach ($location in $Locations) {
    Invoke-Live (@{ method = "console"; command = "teleport $location" } | ConvertTo-Json -Compress) | Out-Null
    Start-Sleep -Seconds 2
    Invoke-Live '{"method":"closeOffenceNotice"}' | Out-Null
    Invoke-Live '{"method":"restoreHud"}' | Out-Null

    foreach ($profile in $Profiles) {
        if ($profile -eq "Off") {
            Invoke-Live '{"method":"restore"}' | Out-Null
        }
        else {
            Invoke-Live (@{ method = "apply"; profile = $profile } | ConvertTo-Json -Compress) | Out-Null
        }

        Start-Sleep -Seconds 3
        Invoke-Live '{"method":"closeOffenceNotice"}' | Out-Null
        Invoke-Live '{"method":"restoreHud"}' | Out-Null

        $safeLocation = ($location -replace '[^A-Za-z0-9_-]', '_')
        $safeProfile = ($profile -replace '[^A-Za-z0-9_-]', '_')
        $shotName = "$OutputName-$stamp-$safeLocation-$safeProfile"
        $screenshot = Invoke-Live (@{ method = "screenshot"; name = $shotName } | ConvertTo-Json -Compress)
        Start-Sleep -Seconds 1

        $sample = $null
        if ($SampleSeconds -gt 0) {
            $sample = Invoke-Live (@{ method = "sampleFps"; seconds = $SampleSeconds } | ConvertTo-Json -Compress) (($SampleSeconds + 15) * 1000)
        }

        $state = Invoke-Live '{"method":"state"}'
        Add-Row $location $profile $state $sample $screenshot
    }
}

Invoke-Live '{"method":"restore"}' | Out-Null

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


