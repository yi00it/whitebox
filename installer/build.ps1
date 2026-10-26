# Builds a self-contained Whitebox.exe and wraps it in an Inno Setup installer at dist\.
# Usage: .\installer\build.ps1 [-Version 1.2.0]   (defaults to <Version> in Whitebox.csproj)
param([string]$Version)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

if (-not $Version) {
    $Version = (Select-Xml -Path "$root\Whitebox.csproj" -XPath '//Version').Node.InnerText
}

$iscc = (Get-Command iscc -ErrorAction SilentlyContinue).Source
if (-not $iscc) {
    $iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
              "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
              "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") |
            Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $iscc) {
    throw 'Inno Setup 6 not found. Install it with: winget install JRSoftware.InnoSetup'
}

$out = "$root\out\installer"
dotnet publish "$root\Whitebox.csproj" -c Release -r win-x64 --self-contained true "-p:Version=$Version" `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o $out
if ($LASTEXITCODE) { throw 'dotnet publish failed' }

& $iscc "/DAppVersion=$Version" "/DSourceExe=$out\Whitebox.exe" "$PSScriptRoot\Whitebox.iss"
if ($LASTEXITCODE) { throw 'ISCC failed' }
