[CmdletBinding()]
param(
    [switch]$Bootstrap,
    [switch]$Test,
    [switch]$Run
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Push-Location $PSScriptRoot
try {
    $localSdk = Join-Path $PSScriptRoot '.scratch/dotnet/dotnet.exe'
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $localSdk) {
        $notifsDotnet = $localSdk
    } elseif ($dotnetCommand -and ((& $dotnetCommand.Source --list-sdks) -match '^10\.0\.')) {
        $notifsDotnet = $dotnetCommand.Source
    } elseif ($Bootstrap) {
        $toolsDirectory = Join-Path $PSScriptRoot '.scratch/tools'
        New-Item -ItemType Directory -Force -Path $toolsDirectory | Out-Null
        $installer = Join-Path $toolsDirectory 'dotnet-install.ps1'
        Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer
        & $installer -Version '10.0.404' -InstallDir (Join-Path $PSScriptRoot '.scratch/dotnet') -NoPath
        if ($LASTEXITCODE -ne 0) { throw 'Не удалось установить .NET 10 SDK.' }
        $notifsDotnet = $localSdk
    } else {
        throw 'Нужен .NET 10 SDK. Запустите .\build.ps1 -Bootstrap или установите SDK самостоятельно.'
    }
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    & $notifsDotnet restore Notifs.slnx --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Не удалось восстановить зависимости.' }
    & $notifsDotnet build Notifs.slnx --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Ошибка сборки.' }
    if ($Test) {
        & $notifsDotnet run --project tests/Notifs.Scenarios --configuration Release --no-build
        if ($LASTEXITCODE -ne 0) { throw 'Сценарная проверка не пройдена.' }
    }
    if ($Run) {
        & $notifsDotnet run --project src/Notifs.App --configuration Release --no-build
        if ($LASTEXITCODE -ne 0) { throw 'Приложение завершилось с ошибкой.' }
    }
} finally { Pop-Location }
