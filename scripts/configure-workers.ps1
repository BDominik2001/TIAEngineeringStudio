<#
.SYNOPSIS
    A telepített workerek .exe.config codeBase bejegyzésének beállítása a célgép registryje alapján.
.DESCRIPTION
    A Siemens által leírt, strong name ellenőrzést megőrző app.config-feloldás: a codeBase a helyi
    TIA Portal telepítés Siemens.Engineering.dll-jére mutat. Hiányzó telepítésnél az adott worker
    konfigurációját nem módosítja és hibát jelez (verziót nem helyettesít).
#>
param([string]$WorkersRoot = (Join-Path $PSScriptRoot '..\dist\TiaEngineeringStudio\workers'))
. "$PSScriptRoot\_common.ps1"

$template = Get-Content -Raw (Join-Path $RepoRoot 'src\tia-adapters\openness-shared\app.config.template')
foreach ($version in $SupportedTiaVersions) {
    $exe = Join-Path $WorkersRoot "$version\TiaStudio.TiaAdapter.$version.exe"
    if (-not (Test-Path $exe)) { Write-Host "    ${version}: worker nincs telepítve ($exe)"; continue }
    $dll = Get-OpennessDllPath $version
    if (-not $dll) { Write-Host "    ${version}: TIA Portal $version Openness nem található – kihagyva" -ForegroundColor Yellow; continue }
    $assemblyVersion = [System.Reflection.AssemblyName]::GetAssemblyName($dll).Version.ToString()
    $config = $template.Replace('@TIA_API_PATH@', (Split-Path $dll -Parent)).Replace('@TIA_ASSEMBLY_VERSION@', $assemblyVersion).Replace('@TIA_VERSION@', $version)
    Set-Content -LiteralPath "$exe.config" -Value $config -Encoding UTF8
    Write-Host "    ${version}: $exe.config -> $dll ($assemblyVersion)" -ForegroundColor Green
}
