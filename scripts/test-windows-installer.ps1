[CmdletBinding()]
param(
    [string] $InstallerPath,
    [string] $InnoCompilerPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-InnoCompiler {
    param([string] $RequestedPath)

    [string[]] $candidates = @(
        $RequestedPath,
        (Join-Path ${env:ProgramFiles} 'Inno Setup 7\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe'),
        (Join-Path ${env:LOCALAPPDATA} 'Programs\Inno Setup 7\ISCC.exe')
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            $resolved = [IO.Path]::GetFullPath($candidate)
            $observedVersion = (& $resolved --version 2>&1 | Out-String).Trim()
            if ($LASTEXITCODE -ne 0 -or $observedVersion -ne '7.1.0') {
                throw "Inno Setup 7.1.0 is required; observed $observedVersion."
            }
            return $resolved
        }
    }

    throw 'Inno Setup 7.1.0 x64 is required.'
}

function Invoke-Executable {
    param([string] $Path, [string[]] $Arguments)

    $process = Start-Process -FilePath $Path -ArgumentList $Arguments -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        throw "$([IO.Path]::GetFileName($Path)) failed with exit code $($process.ExitCode)."
    }
}

function Get-ExtSieveUninstallEntries {
    $root = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall'
    if (-not (Test-Path -LiteralPath $root)) {
        return @()
    }

    return @(Get-ChildItem -LiteralPath $root | Where-Object {
        (Get-ItemProperty -LiteralPath $_.PSPath -ErrorAction SilentlyContinue).DisplayName -eq 'ExtSieve'
    })
}

function Assert-InstalledVersion {
    param([string] $ExecutablePath, [string] $ExpectedVersion)

    if (-not (Test-Path -LiteralPath $ExecutablePath -PathType Leaf)) {
        throw "Installed executable is missing: $ExecutablePath"
    }
    $version = (Get-Item -LiteralPath $ExecutablePath).VersionInfo.ProductVersion
    if ($version -ne $ExpectedVersion) {
        throw "Installed product version is $version; expected $ExpectedVersion."
    }
}

function Invoke-InstalledArchiveSmoke {
    param([string] $InstallDirectory, [string] $ScratchDirectory)

    $helperDirectory = Join-Path $ScratchDirectory 'installed-archive-smoke'
    $projectPath = Join-Path $helperDirectory 'InstalledArchiveSmoke.csproj'
    $programPath = Join-Path $helperDirectory 'Program.cs'
    New-Item -ItemType Directory -Path $helperDirectory -Force | Out-Null
    $coreAssemblyPath = [Security.SecurityElement]::Escape(
        (Join-Path $InstallDirectory 'ExtSieve.Core.dll'))
    $project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="ExtSieve.Core">
      <HintPath>$coreAssemblyPath</HintPath>
    </Reference>
  </ItemGroup>
</Project>
"@
    $helper = @'
using System.IO.Compression;
using ExtSieve.Core.Models;
using ExtSieve.Core.Services;

var scratch = Path.GetFullPath(args[0]);
var source = Path.Combine(scratch, "archive-source");
var nested = Path.Combine(source, "nested");
var destination = Path.Combine(scratch, "installed-smoke.zip");
Directory.CreateDirectory(nested);
File.WriteAllText(Path.Combine(source, "root.txt"), "root");
File.WriteAllBytes(Path.Combine(nested, "payload.bin"), [0, 1, 2, 3]);
var scan = await new FolderScanner().ScanAsync(source, null, CancellationToken.None);
if (scan.Files.Count != 2) throw new InvalidOperationException("Scan count mismatch.");
var selected = scan.Files.Select(file => file.ExtensionKey)
    .ToHashSet(StringComparer.OrdinalIgnoreCase);
var request = new ArchiveRequest(
    source, destination, selected, ArchiveMode.PreserveStructure, scan.Files);
var plan = new ArchivePlanner().CreatePlan(request);
var result = await new ZipArchiveService().CreateAsync(
    destination, plan, false, null, CancellationToken.None);
if (result.EntryCount != 2) throw new InvalidOperationException("Archive count mismatch.");
using var archive = ZipFile.OpenRead(destination);
var entries = archive.Entries.Select(entry => entry.FullName)
    .OrderBy(name => name, StringComparer.Ordinal).ToArray();
if (!entries.SequenceEqual(["nested/payload.bin", "root.txt"], StringComparer.Ordinal))
    throw new InvalidOperationException("Archive entries mismatch.");
'@
    [IO.File]::WriteAllText(
        $projectPath,
        $project,
        [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText(
        $programPath,
        $helper,
        [Text.UTF8Encoding]::new($false))
    & dotnet run --project $projectPath --configuration Release -- $ScratchDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "Installed archive smoke process failed with exit code $LASTEXITCODE."
    }
}

$productDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
[xml] $properties = Get-Content -LiteralPath (
    Join-Path $productDirectory 'Directory.Build.props') -Raw
$version = [string] $properties.Project.PropertyGroup.Version
if ($version -ne '1.0.0') {
    throw "This lifecycle gate expects the approved 1.0.0 candidate; observed $version."
}

if ([string]::IsNullOrWhiteSpace($InstallerPath)) {
    $InstallerPath = Join-Path $productDirectory "artifacts\ExtSieve-Setup-$version-win-x64.exe"
}
$InstallerPath = [IO.Path]::GetFullPath($InstallerPath)
if (-not (Test-Path -LiteralPath $InstallerPath -PathType Leaf)) {
    throw "Installer is missing: $InstallerPath"
}

$innoCompiler = Resolve-InnoCompiler $InnoCompilerPath
$installDirectory = Join-Path ${env:LOCALAPPDATA} 'Programs\ExtSieve'
$installedExecutable = Join-Path $installDirectory 'ExtSieve.App.exe'
$settingsPath = Join-Path ${env:LOCALAPPDATA} 'ExtSieve\settings.json'
$settingsDirectory = Split-Path -Parent $settingsPath
$startMenuShortcut = Join-Path ([Environment]::GetFolderPath(
    [Environment+SpecialFolder]::Programs)) 'ExtSieve.lnk'
$desktopShortcut = Join-Path ([Environment]::GetFolderPath(
    [Environment+SpecialFolder]::DesktopDirectory)) 'ExtSieve.lnk'
$temporaryDirectory = Join-Path ([IO.Path]::GetTempPath()) (
    'extsieve-installer-lifecycle-' + [Guid]::NewGuid().ToString('N'))
$ownerSettingsExisted = Test-Path -LiteralPath $settingsPath -PathType Leaf
[byte[]] $ownerSettings = if ($ownerSettingsExisted) {
    [IO.File]::ReadAllBytes($settingsPath)
} else {
    [byte[]]::new(0)
}
$launchedProcess = $null

if (@(Get-ExtSieveUninstallEntries).Count -ne 0 -or
    (Test-Path -LiteralPath $installDirectory)) {
    throw 'A pre-existing ExtSieve installation was found; the lifecycle test will not modify it.'
}

New-Item -ItemType Directory -Path $temporaryDirectory | Out-Null
try {
    $userArchive = Join-Path $temporaryDirectory 'user-created.zip'
    [IO.File]::WriteAllText($userArchive, 'owner data')
    New-Item -ItemType Directory -Path $settingsDirectory -Force | Out-Null
    $testSettings = [Text.UTF8Encoding]::new($false).GetBytes(
        "{`n  `"Language`": `"Italian`",`n  `"Theme`": `"Dark`"`n}`n")
    [IO.File]::WriteAllBytes($settingsPath, $testSettings)

    Invoke-Executable $InstallerPath @(
        '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/NOCLOSEAPPLICATIONS')
    Assert-InstalledVersion $installedExecutable $version
    if (@(Get-ExtSieveUninstallEntries).Count -ne 1) {
        throw 'The clean install did not create exactly one Installed Apps entry.'
    }
    if (-not (Test-Path -LiteralPath $startMenuShortcut -PathType Leaf)) {
        throw 'The Start Menu shortcut is missing.'
    }
    if (Test-Path -LiteralPath $desktopShortcut) {
        throw 'The optional Desktop shortcut was created without being selected.'
    }

    Invoke-InstalledArchiveSmoke $installDirectory $temporaryDirectory
    $launchedProcess = Start-Process -FilePath $installedExecutable -PassThru
    Start-Sleep -Seconds 2
    if ($launchedProcess.HasExited) {
        throw 'The installed application did not remain running after launch.'
    }
    [void] $launchedProcess.CloseMainWindow()
    if (-not $launchedProcess.WaitForExit(5000)) {
        Stop-Process -Id $launchedProcess.Id -Force
        $launchedProcess.WaitForExit()
    }
    $launchedProcess = $null

    Invoke-Executable $InstallerPath @(
        '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/TASKS=desktopicon')
    if (-not (Test-Path -LiteralPath $desktopShortcut -PathType Leaf)) {
        throw 'Selecting the optional Desktop shortcut task did not create the shortcut.'
    }

    $upgradeVersion = '1.0.1'
    $upgradePayload = Join-Path $temporaryDirectory 'upgrade-payload'
    New-Item -ItemType Directory -Path $upgradePayload | Out-Null
    Copy-Item -Path (Join-Path $installDirectory '*') -Destination $upgradePayload -Recurse -Force
    [string[]] $publishArguments = @(
        'publish',
        (Join-Path $productDirectory 'src\ExtSieve.App\ExtSieve.App.csproj'),
        '--configuration', 'Release',
        '--runtime', 'win-x64',
        '--self-contained', 'true',
        '--no-restore',
        '--output', $upgradePayload,
        '-p:Version=1.0.1',
        '-p:PublishSingleFile=false',
        '-p:PublishTrimmed=false',
        '-p:PublishAot=false',
        '-p:DebugSymbols=false',
        '-p:DebugType=None'
    )
    & dotnet @publishArguments
    if ($LASTEXITCODE -ne 0) {
        throw 'The controlled 1.0.1 application fixture could not be published.'
    }
    $upgradeInstaller = Join-Path $temporaryDirectory "ExtSieve-Setup-$upgradeVersion-win-x64.exe"
    [string[]] $innoArguments = @(
        "/DAppVersion=$upgradeVersion",
        "/DPayloadDir=$upgradePayload",
        "/DArtifactDir=$temporaryDirectory",
        '/Qp',
        (Join-Path $productDirectory 'packaging\windows\ExtSieve.iss')
    )
    & $innoCompiler @innoArguments
    if ($LASTEXITCODE -ne 0 -or
        -not (Test-Path -LiteralPath $upgradeInstaller -PathType Leaf)) {
        throw 'The controlled upgrade installer fixture could not be compiled.'
    }

    $launchedProcess = Start-Process -FilePath $installedExecutable -PassThru
    Start-Sleep -Seconds 2
    Invoke-Executable $upgradeInstaller @(
        '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART',
        '/CLOSEAPPLICATIONS', '/NORESTARTAPPLICATIONS')
    $launchedProcess.Refresh()
    if (-not $launchedProcess.HasExited) {
        throw 'Upgrade did not close the running application safely.'
    }
    $launchedProcess = $null
    Assert-InstalledVersion $installedExecutable $upgradeVersion
    if (@(Get-ExtSieveUninstallEntries).Count -ne 1) {
        throw 'Upgrade created a duplicate Installed Apps entry.'
    }
    if ([Convert]::ToBase64String([IO.File]::ReadAllBytes($settingsPath)) -ne
        [Convert]::ToBase64String($testSettings)) {
        throw 'Upgrade changed the user settings file.'
    }

    $launchedProcess = Start-Process -FilePath $installedExecutable -PassThru
    Start-Sleep -Seconds 2
    $uninstaller = Join-Path $installDirectory 'unins000.exe'
    $blockedUninstall = Start-Process -FilePath $uninstaller -ArgumentList @(
        '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') -Wait -PassThru
    $launchedProcess.Refresh()
    if ($launchedProcess.HasExited -or
        -not (Test-Path -LiteralPath $installedExecutable -PathType Leaf) -or
        @(Get-ExtSieveUninstallEntries).Count -ne 1) {
        throw "In-use uninstall was not blocked safely (exit code $($blockedUninstall.ExitCode))."
    }
    [void] $launchedProcess.CloseMainWindow()
    if (-not $launchedProcess.WaitForExit(5000)) {
        throw 'The application did not close normally after the blocked uninstall.'
    }
    $launchedProcess = $null
    Invoke-Executable $uninstaller @(
        '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART')
    if (Test-Path -LiteralPath $installDirectory) {
        throw 'Uninstall left the installer-owned application directory.'
    }
    if (@(Get-ExtSieveUninstallEntries).Count -ne 0) {
        throw 'Uninstall left an Installed Apps entry.'
    }
    if ((Test-Path -LiteralPath $startMenuShortcut) -or
        (Test-Path -LiteralPath $desktopShortcut)) {
        throw 'Uninstall left an installer-created shortcut.'
    }
    if ([Convert]::ToBase64String([IO.File]::ReadAllBytes($settingsPath)) -ne
        [Convert]::ToBase64String($testSettings)) {
        throw 'Uninstall changed or removed user settings.'
    }
    if (-not (Test-Path -LiteralPath $userArchive -PathType Leaf)) {
        throw 'Uninstall removed a user-created archive.'
    }

    Invoke-Executable $InstallerPath @(
        '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART')
    Assert-InstalledVersion $installedExecutable $version
    $uninstaller = Join-Path $installDirectory 'unins000.exe'
    Invoke-Executable $uninstaller @(
        '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART')
    if (Test-Path -LiteralPath $installDirectory) {
        throw 'The reinstall/uninstall cycle left installer-owned files.'
    }

Write-Host 'Windows installer lifecycle passed: install, launch, archive, optional shortcut, upgrade, in-use blocking, uninstall, settings preservation, and reinstall.'
}
finally {
    if ($null -ne $launchedProcess -and -not $launchedProcess.HasExited) {
        Stop-Process -Id $launchedProcess.Id -Force -ErrorAction SilentlyContinue
    }
    $cleanupUninstaller = Join-Path $installDirectory 'unins000.exe'
    if (Test-Path -LiteralPath $cleanupUninstaller -PathType Leaf) {
        Start-Process -FilePath $cleanupUninstaller -ArgumentList @(
            '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') -Wait | Out-Null
    }
    if ($ownerSettingsExisted) {
        New-Item -ItemType Directory -Path $settingsDirectory -Force | Out-Null
        [IO.File]::WriteAllBytes($settingsPath, $ownerSettings)
    }
    elseif (Test-Path -LiteralPath $settingsPath) {
        Remove-Item -LiteralPath $settingsPath -Force
    }
    if (Test-Path -LiteralPath $temporaryDirectory) {
        Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
    }
}
