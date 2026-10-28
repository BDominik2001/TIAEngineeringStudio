# Közös segédfüggvények a TIA Engineering Studio szkriptjeihez (dot-source: . "$PSScriptRoot\_common.ps1").
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$script:SupportedTiaVersions = @('V18', 'V19', 'V20')
$script:OpennessRegistryRoot = 'HKLM:\SOFTWARE\Siemens\Automation\Openness'

function Write-Step([string]$Message) {
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Invoke-Checked([string]$FilePath, [string[]]$Arguments) {
    Write-Host "    $FilePath $($Arguments -join ' ')" -ForegroundColor DarkGray
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath exited with code $LASTEXITCODE"
    }
}

function Get-TiaMajor([string]$Version) {
    if ($script:SupportedTiaVersions -notcontains $Version) {
        throw "Unsupported TIA version '$Version' (V18, V19, V20). The tooling never substitutes another version."
    }
    return [int]$Version.Substring(1)
}

# A Siemens.Engineering.dll helye a registry alapján (spec 3.4). $null, ha nincs natív bejegyzés.
function Get-OpennessDllPath([string]$Version) {
    $major = Get-TiaMajor $Version
    $versionKey = Get-ChildItem $script:OpennessRegistryRoot -ErrorAction SilentlyContinue |
        Where-Object { $_.PSChildName -like "$major.*" } |
        Sort-Object PSChildName -Descending | Select-Object -First 1
    if (-not $versionKey) { return $null }
    $apiKey = Get-ChildItem (Join-Path $versionKey.PSPath 'PublicAPI') -ErrorAction SilentlyContinue |
        Where-Object { $_.PSChildName -like "$major.*" } |
        Sort-Object PSChildName -Descending | Select-Object -First 1
    if (-not $apiKey) { return $null }
    $props = Get-ItemProperty $apiKey.PSPath
    $dll = $null
    if ($props.PSObject.Properties.Name -contains 'Siemens.Engineering') {
        $dll = $props.'Siemens.Engineering'
    }
    if (-not $dll) {
        $dll = $props.PSObject.Properties |
            Where-Object { $_.Value -is [string] -and $_.Value -like '*\Siemens.Engineering.dll' } |
            Select-Object -First 1 -ExpandProperty Value
    }
    if ($dll -and (Test-Path -LiteralPath $dll)) { return $dll }
    return $null
}

function Test-OpennessGroupMembership {
    $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    foreach ($group in $identity.Groups) {
        try {
            $name = $group.Translate([System.Security.Principal.NTAccount]).Value
            if ($name -like '*\Siemens TIA Openness' -or $name -eq 'Siemens TIA Openness') { return $true }
        } catch { }
    }
    return $false
}
