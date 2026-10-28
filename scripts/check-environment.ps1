<#
.SYNOPSIS
    Környezetvizsgálat (spec 13.1): eszközök, telepített TIA Openness verziók, csoporttagság.
    Csak olvas; semmit nem módosít.
#>
. "$PSScriptRoot\_common.ps1"
$ErrorActionPreference = 'Continue'

Write-Step 'Eszközök'
foreach ($tool in @('python', 'cargo', 'rustc', 'dotnet')) {
    $cmd = Get-Command $tool -ErrorAction SilentlyContinue
    if ($cmd) {
        $version = & $tool --version 2>&1 | Select-Object -First 1
        Write-Host ("    {0,-8} {1}" -f $tool, $version)
    } else {
        Write-Host ("    {0,-8} NINCS a PATH-on" -f $tool) -ForegroundColor Yellow
    }
}

Write-Step "Openness registry-ágak ($OpennessRegistryRoot)"
Get-ChildItem $OpennessRegistryRoot -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "    $($_.PSChildName)" }

Write-Step 'TIA Portal V18 / V19 / V20 (registry alapú felderítés)'
foreach ($version in $SupportedTiaVersions) {
    $dll = Get-OpennessDllPath $version
    if ($dll) {
        $name = [System.Reflection.AssemblyName]::GetAssemblyName($dll)
        $token = ($name.GetPublicKeyToken() | ForEach-Object { $_.ToString('x2') }) -join ''
        Write-Host ("    {0}: {1} (assembly {2}, token {3})" -f $version, $dll, $name.Version, $token) -ForegroundColor Green
    } else {
        Write-Host ("    {0}: Not found" -f $version) -ForegroundColor Yellow
    }
}

Write-Step 'Tipikus DLL-útvonalak (csak gyors diagnosztika; a program a registryt használja)'
foreach ($version in $SupportedTiaVersions) {
    $path = "C:\Program Files\Siemens\Automation\Portal $version\PublicAPI\$version\Siemens.Engineering.dll"
    Write-Host ("    {0}: {1}" -f (Test-Path $path), $path)
}

Write-Step "Windows-csoport 'Siemens TIA Openness'"
if (Test-OpennessGroupMembership) {
    Write-Host '    A felhasználó tagja.' -ForegroundColor Green
} else {
    Write-Host '    A felhasználó NEM tagja (tagság után ki- és bejelentkezés szükséges).' -ForegroundColor Yellow
}
