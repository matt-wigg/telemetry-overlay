param([switch]$SkipInstaller)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
$output = Join-Path $root 'artifacts'
$portable = Join-Path $output 'portable'
if (Test-Path -LiteralPath $portable) {
    $resolved = (Resolve-Path -LiteralPath $portable).Path
    $expected = [System.IO.Path]::GetFullPath((Join-Path $root 'artifacts\portable'))
    if ($resolved -ne $expected -or ((Get-Item -LiteralPath $portable).Attributes -band [System.IO.FileAttributes]::ReparsePoint)) {
        throw 'Publish directory must be the ordinary artifacts/portable directory inside this checkout.'
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
[xml]$properties = Get-Content (Join-Path $root 'Directory.Build.props')
$version = $properties.Project.PropertyGroup.Version
New-Item -ItemType Directory -Force -Path $portable | Out-Null
Push-Location $root
try {
    & $dotnet restore src/Telemetry.App/Telemetry.App.csproj -r win-x64 --locked-mode -p:PublishReadyToRun=true
    if ($LASTEXITCODE -ne 0) { throw 'Locked restore failed.' }
    & $dotnet publish src/Telemetry.App/Telemetry.App.csproj -c Release -r win-x64 --self-contained true --no-restore -p:PublishReadyToRun=true -p:DebugType=None -p:DebugSymbols=false -p:ContinuousIntegrationBuild=true -o $portable
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    Copy-Item -LiteralPath LICENSE,README.md,THIRD-PARTY-NOTICES.md -Destination $portable
    Copy-Item -LiteralPath docs -Destination $portable -Recurse -Force
    $licenses = Join-Path $portable 'licenses'
    New-Item -ItemType Directory -Force -Path $licenses | Out-Null
    $nuget = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
    $runtime = Get-Content -Raw -LiteralPath (Join-Path $portable 'Telemetry.runtimeconfig.json') | ConvertFrom-Json
    foreach ($pack in @('microsoft.netcore.app.runtime.win-x64','microsoft.windowsdesktop.app.runtime.win-x64')) {
        $frameworkName = $pack.Replace('.runtime.win-x64', '')
        $frameworkVersion = ($runtime.runtimeOptions.includedFrameworks | Where-Object { $_.name -eq $frameworkName }).version
        $package = Get-Item -LiteralPath (Join-Path (Join-Path $nuget $pack) $frameworkVersion)
        $notices = Get-ChildItem -LiteralPath $package.FullName -File | Where-Object { $_.Name -match 'LICENSE|THIRD.PARTY.NOTICES' }
        if (!$notices) { throw "Missing framework licenses: $pack" }
        foreach ($notice in $notices) { Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $licenses ($pack + '-' + $notice.Name)) }
    }
    $zip = Join-Path $output "Telemetry-$version-win-x64.zip"
    Compress-Archive -Path "$portable\*" -DestinationPath $zip -Force
    if (!$SkipInstaller) {
        $compiler = @((Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'), (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe')) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
        if (!$compiler) { throw 'Install Inno Setup 6, or use -SkipInstaller for a portable build.' }
        & $compiler "/DAppVersion=$version" /Q (Join-Path $PSScriptRoot 'Telemetry.iss')
        if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
    }
    Get-ChildItem -LiteralPath $output -File | Where-Object { $_.Name -in "Telemetry-$version-win-x64.zip","Telemetry-Setup-$version.exe" } | ForEach-Object {
        $hash = Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256
        '{0}  {1}' -f $hash.Hash.ToLowerInvariant(), $_.Name
    } | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding ascii
} finally { Pop-Location }
