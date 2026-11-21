[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$Dotnet)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifacts = Join-Path $repository '.artifacts'
$staging = Join-Path $artifacts ('staging/' + [Guid]::NewGuid().ToString('N'))
$package = Join-Path $staging 'Notifs-win-x64'
New-Item -ItemType Directory -Force -Path $package | Out-Null
& $Dotnet restore (Join-Path $repository 'src/Notifs.App') -p:NotifsPortable=true --locked-mode
if ($LASTEXITCODE -ne 0) { throw 'Не удалось восстановить зафиксированные зависимости поставки.' }
& $Dotnet publish (Join-Path $repository 'src/Notifs.App') -c Release -p:NotifsPortable=true --no-restore -m:1 -o $package
if ($LASTEXITCODE -ne 0) { throw 'Не удалось собрать автономную поставку.' }
Copy-Item -LiteralPath (Join-Path $repository 'docs/release/README.txt') -Destination (Join-Path $package 'README.txt')
Copy-Item -LiteralPath (Join-Path $repository 'licenses') -Destination (Join-Path $package 'licenses') -Recurse
foreach ($required in @('Notifs.App.exe', 'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'PresentationFramework.dll', 'e_sqlite3.dll', 'Assets/notifs.ico', 'Assets/notification.wav')) {
    if (!(Test-Path -LiteralPath (Join-Path $package $required))) { throw "В поставке отсутствует $required" }
}
$files = @(Get-ChildItem -LiteralPath $package -File -Recurse | Sort-Object FullName | ForEach-Object {
    [ordered]@{ path = $_.FullName.Substring($package.Length + 1).Replace('\', '/'); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
$manifest = [ordered]@{ format = 1; platform = 'win-x64'; sdk = (& $Dotnet --version).Trim(); runtime = '10.0.15'; sourceCommit = (& git -C $repository rev-parse HEAD).Trim(); uncommittedChanges = [bool](& git -C $repository status --porcelain); acceptance = 'See docs/qa/issue-10-release.md; unperformed native checks are not a pass'; files = $files }
$utf8 = New-Object Text.UTF8Encoding($false)
[IO.File]::WriteAllText((Join-Path $package 'release-manifest.json'), ($manifest | ConvertTo-Json -Depth 6), $utf8)
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zipPath = Join-Path $staging 'Notifs-win-x64.zip'
$archive = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem -LiteralPath $package -File -Recurse | Sort-Object FullName) {
        $relative = 'Notifs-win-x64/' + $file.FullName.Substring($package.Length + 1).Replace('\', '/')
        $entry = $archive.CreateEntry($relative, [IO.Compression.CompressionLevel]::Optimal)
        $entry.LastWriteTime = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
        $inputStream = [IO.File]::OpenRead($file.FullName)
        $outputStream = $entry.Open()
        try { $inputStream.CopyTo($outputStream) } finally { $outputStream.Dispose(); $inputStream.Dispose() }
    }
} finally { $archive.Dispose() }
$destination = [IO.Path]::GetFullPath((Join-Path $artifacts 'Notifs-win-x64'))
if (!$destination.StartsWith($artifacts + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Выходная папка должна оставаться в .artifacts.' }
if (![IO.Path]::GetFullPath($package).StartsWith($artifacts + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Исходная папка должна оставаться в .artifacts.' }
if (Test-Path -LiteralPath $destination) { Remove-Item -LiteralPath $destination -Recurse -Force }
Move-Item -LiteralPath $package -Destination $destination
$releaseZip = Join-Path $artifacts 'Notifs-win-x64.zip'
Copy-Item -LiteralPath $zipPath -Destination $releaseZip -Force
[IO.File]::WriteAllText(($releaseZip + '.sha256'), ((Get-FileHash -LiteralPath $releaseZip -Algorithm SHA256).Hash.ToLowerInvariant() + '  Notifs-win-x64.zip' + [Environment]::NewLine), $utf8)
Write-Host "EXE: $(Join-Path $destination 'Notifs.App.exe')"
Write-Host "ZIP: $releaseZip"
