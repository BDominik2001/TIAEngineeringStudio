<#
.SYNOPSIS
    A V18/V19/V20 Openness workerek építése, mindegyik a SAJÁT telepítése Siemens.Engineering.dll-jével.
.DESCRIPTION
    A DLL helyét a registryből olvassa (spec 3.4); ha nem található, a worker "unlinked" módban
    épül (minden TIA-műveletet WORKER_BUILT_WITHOUT_OPENNESS hibával elutasít), kivéve ha -RequireOpenness.
    A Siemens DLL-t nem másolja a kimenetbe (Private=False).
.EXAMPLE
    .\build-tia-workers.ps1 -Versions V18 -RequireOpenness
#>
param(
    [string[]]$Versions = @('V18', 'V19', 'V20'),
    [string]$Configuration = 'Release',
    [string]$OutputRoot = (Join-Path $PSScriptRoot '..\dist\TiaEngineeringStudio\workers'),
    [switch]$RequireOpenness
)
. "$PSScriptRoot\_common.ps1"

foreach ($version in $Versions) {
    $null = Get-TiaMajor $version
    $dll = Get-OpennessDllPath $version
    $project = Join-Path $RepoRoot "src\tia-adapters\$($version.ToLowerInvariant())\TiaStudio.TiaAdapter.$version.csproj"
    $output = Join-Path $OutputRoot $version
    $arguments = @('build', $project, '-c', $Configuration, '-o', $output)
    if ($dll) {
        Write-Step "$version worker – Openness: $dll"
        $arguments += "-p:TiaApiPath=$(Split-Path $dll -Parent)"
    } elseif ($RequireOpenness) {
        throw "TIA Portal $version Openness nincs telepítve; -RequireOpenness miatt leállok."
    } else {
        Write-Step "$version worker – Openness NEM található: unlinked build (minden TIA-művelet elutasítva)"
        $arguments += '-p:TesForceUnlinked=true'
    }
    Invoke-Checked 'dotnet' $arguments
}
Write-Host "`nWorkerek: $OutputRoot" -ForegroundColor Green
