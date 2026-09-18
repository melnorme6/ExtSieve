[CmdletBinding()]
param(
    [string] $InnoCompilerPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-DotNet {
    param(
        [Parameter(Mandatory)]
        [string[]] $Arguments
    )

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Get-PublishedDependencyVersion {
    param(
        [Parameter(Mandatory)]
        [object] $Dependencies,

        [Parameter(Mandatory)]
        [string] $PackageId
    )

    $prefix = "$PackageId/"
    $match = $Dependencies.PSObject.Properties.Name |
        Where-Object { $_.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) } |
        Select-Object -First 1
    if (-not $match) {
        throw "Published dependency is missing: $PackageId"
    }

    return $match.Substring($prefix.Length)
}

function Resolve-InnoCompiler {
    param([string] $RequestedPath)

    [string[]] $candidates = @(
        $RequestedPath,
        (Join-Path ${env:ProgramFiles} 'Inno Setup 7\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe'),
        (Join-Path ${env:LOCALAPPDATA} 'Programs\Inno Setup 7\ISCC.exe')
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    foreach ($candidate in $candidates) {
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            continue
        }

        $resolved = [IO.Path]::GetFullPath($candidate)
        $observedVersion = (& $resolved --version 2>&1 | Out-String).Trim()
        if ($LASTEXITCODE -ne 0 -or $observedVersion -ne '7.1.0') {
            throw "Inno Setup 7.1.0 is required; observed $observedVersion at $resolved."
        }

        return $resolved
    }

    throw 'Inno Setup 7.1.0 x64 is required. Supply -InnoCompilerPath when it is not installed in the default location.'
}

function Copy-PackageNotice {
    param(
        [Parameter(Mandatory)]
        [string[]] $PackageRoots,

        [Parameter(Mandatory)]
        [string] $PackageId,

        [Parameter(Mandatory)]
        [string] $Version,

        [Parameter(Mandatory)]
        [string] $SourceName,

        [Parameter(Mandatory)]
        [string] $DestinationName
    )

    foreach ($packageRoot in $PackageRoots) {
        $packageDirectory = Join-Path $packageRoot (
            Join-Path $PackageId.ToLowerInvariant() $Version
        )
        $sourcePath = Join-Path $packageDirectory $SourceName
        if (Test-Path -LiteralPath $sourcePath -PathType Leaf) {
            Copy-Item -LiteralPath $sourcePath -Destination (
                Join-Path $script:packageDirectory $DestinationName
            )
            return
        }
    }

    throw "Required notice is missing from $PackageId ${Version}: $SourceName"
}

function Test-FileContainsBytes {
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [byte[]] $Pattern
    )

    $stream = [IO.File]::OpenRead($Path)
    try {
        $chunkSize = 65536
        $buffer = New-Object byte[] ($chunkSize + $Pattern.Length - 1)
        $retained = 0

        while (($read = $stream.Read($buffer, $retained, $chunkSize)) -gt 0) {
            $available = $retained + $read
            for ($offset = 0; $offset -le $available - $Pattern.Length; $offset++) {
                $matches = $true
                for ($index = 0; $index -lt $Pattern.Length; $index++) {
                    if ($buffer[$offset + $index] -ne $Pattern[$index]) {
                        $matches = $false
                        break
                    }
                }

                if ($matches) {
                    return $true
                }
            }

            $retained = [Math]::Min($Pattern.Length - 1, $available)
            if ($retained -gt 0) {
                [Array]::Copy($buffer, $available - $retained, $buffer, 0, $retained)
            }
        }
    }
    finally {
        $stream.Dispose()
    }

    return $false
}

$productDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$solutionPath = Join-Path $productDirectory 'ExtSieve.sln'
$projectPath = Join-Path $productDirectory 'src\ExtSieve.App\ExtSieve.App.csproj'
$buildPropsPath = Join-Path $productDirectory 'Directory.Build.props'
$artifactsDirectory = Join-Path $productDirectory 'artifacts'
$installerScriptPath = Join-Path $productDirectory 'packaging\windows\ExtSieve.iss'

[xml] $buildProps = Get-Content -LiteralPath $buildPropsPath -Raw
$version = [string] $buildProps.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) {
    throw 'Directory.Build.props does not define the product Version.'
}

$runtimeIdentifier = 'win-x64'
$packageName = "ExtSieve-$version-$runtimeIdentifier"
$packageDirectory = Join-Path $artifactsDirectory $packageName
$archivePath = Join-Path $artifactsDirectory "$packageName.zip"
$manifestPath = Join-Path $artifactsDirectory "$packageName.manifest.sha256"
$archiveChecksumPath = "$archivePath.sha256"
$installerPath = Join-Path $artifactsDirectory "ExtSieve-Setup-$version-$runtimeIdentifier.exe"
$installerManifestPath = Join-Path $artifactsDirectory "ExtSieve-Setup-$version-$runtimeIdentifier.manifest.sha256"
$installerChecksumPath = "$installerPath.sha256"
$innoCompiler = Resolve-InnoCompiler $InnoCompilerPath

New-Item -ItemType Directory -Path $artifactsDirectory -Force | Out-Null
foreach ($path in @(
    $packageDirectory,
    $archivePath,
    $manifestPath,
    $archiveChecksumPath,
    $installerPath,
    $installerManifestPath,
    $installerChecksumPath
)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
}

Push-Location $productDirectory
try {
    Invoke-DotNet -Arguments @('restore', $solutionPath, '--locked-mode')
    Invoke-DotNet -Arguments @(
        'build', $solutionPath,
        '--configuration', 'Release',
        '--no-restore'
    )
    Invoke-DotNet -Arguments @(
        'test', $solutionPath,
        '--configuration', 'Release',
        '--no-build',
        '--no-restore'
    )
    Invoke-DotNet -Arguments @(
        'publish', $projectPath,
        '--configuration', 'Release',
        '--runtime', $runtimeIdentifier,
        '--self-contained', 'true',
        '--no-restore',
        '--output', $packageDirectory,
        '-p:PublishSingleFile=false',
        '-p:PublishTrimmed=false',
        '-p:PublishAot=false',
        '-p:DebugSymbols=false',
        '-p:DebugType=None'
    )
}
finally {
    Pop-Location
}

foreach ($document in @(
    'LICENSE',
    'README.md',
    'CHANGELOG.md',
    'SECURITY.md',
    'THIRD_PARTY_NOTICES.md'
)) {
    Copy-Item -LiteralPath (Join-Path $productDirectory $document) -Destination $packageDirectory
}

$assetsPath = Join-Path $productDirectory 'src\ExtSieve.App\obj\project.assets.json'
$dependenciesPath = Join-Path $packageDirectory 'ExtSieve.App.deps.json'
$assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
$dependencies = Get-Content -LiteralPath $dependenciesPath -Raw | ConvertFrom-Json
[string[]] $packageRoots = @($assets.packageFolders.PSObject.Properties.Name)

$runtimePackage = 'Microsoft.NETCore.App.Runtime.win-x64'
$runtimeDependency = "runtimepack.$runtimePackage"
$runtimeVersion = Get-PublishedDependencyVersion $dependencies.libraries $runtimeDependency
$anglePackage = 'Avalonia.Angle.Windows.Natives'
$angleVersion = Get-PublishedDependencyVersion $dependencies.libraries $anglePackage
$skiaPackage = 'SkiaSharp.NativeAssets.Win32'
$skiaVersion = Get-PublishedDependencyVersion $dependencies.libraries $skiaPackage

Copy-PackageNotice $packageRoots $runtimePackage $runtimeVersion `
    'LICENSE.TXT' 'DOTNET_LICENSE.txt'
Copy-PackageNotice $packageRoots $runtimePackage $runtimeVersion `
    'THIRD-PARTY-NOTICES.TXT' 'DOTNET_THIRD_PARTY_NOTICES.txt'
Copy-PackageNotice $packageRoots $anglePackage $angleVersion `
    'LICENSE' 'ANGLE_LICENSE.txt'
Copy-PackageNotice $packageRoots $skiaPackage $skiaVersion `
    'LICENSE.txt' 'SKIASHARP_LICENSE.txt'
Copy-PackageNotice $packageRoots $skiaPackage $skiaVersion `
    'THIRD-PARTY-NOTICES.txt' 'SKIASHARP_THIRD_PARTY_NOTICES.txt'

Get-ChildItem -LiteralPath $packageDirectory -Recurse -File -Filter '*.pdb' |
    Remove-Item -Force

$forbiddenFiles = Get-ChildItem -LiteralPath $packageDirectory -Recurse -File |
    Where-Object { $_.Extension -in @('.pdb', '.log', '.dmp') }
if ($forbiddenFiles) {
    throw "Package contains forbidden files: $($forbiddenFiles.FullName -join ', ')"
}

$executablePath = Join-Path $packageDirectory 'ExtSieve.App.exe'
if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    throw "Published executable is missing: $executablePath"
}

$privatePathPatterns = @(
    [Text.Encoding]::UTF8.GetBytes($productDirectory),
    [Text.Encoding]::Unicode.GetBytes($productDirectory)
)
foreach ($file in Get-ChildItem -LiteralPath $packageDirectory -File -Filter 'ExtSieve.*') {
    foreach ($pattern in $privatePathPatterns) {
        if (Test-FileContainsBytes $file.FullName $pattern) {
            throw "Package file contains the private build path: $($file.Name)"
        }
    }
}

[string[]] $manifestLines = @(Get-ChildItem -LiteralPath $packageDirectory -Recurse -File |
    ForEach-Object {
        $packagePrefix = $packageDirectory.TrimEnd(
            [IO.Path]::DirectorySeparatorChar,
            [IO.Path]::AltDirectorySeparatorChar
        ) + [IO.Path]::DirectorySeparatorChar
        $relativePath = $_.FullName.Substring($packagePrefix.Length).
            Replace([IO.Path]::DirectorySeparatorChar, '/')
        $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $relativePath"
    })
[Array]::Sort($manifestLines, [StringComparer]::Ordinal)

[IO.File]::WriteAllLines(
    $manifestPath,
    $manifestLines,
    [Text.UTF8Encoding]::new($false)
)

Add-Type -AssemblyName System.IO.Compression
$archiveStream = [IO.File]::Open(
    $archivePath,
    [IO.FileMode]::CreateNew,
    [IO.FileAccess]::Write,
    [IO.FileShare]::None
)
$archive = $null
try {
    $archive = [IO.Compression.ZipArchive]::new(
        $archiveStream,
        [IO.Compression.ZipArchiveMode]::Create,
        $false
    )
    $packagePrefix = $packageDirectory.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar
    ) + [IO.Path]::DirectorySeparatorChar
    [IO.FileInfo[]] $packageFiles = @(Get-ChildItem -LiteralPath $packageDirectory -Recurse -File)
    [Array]::Sort($packageFiles, [Comparison[IO.FileInfo]] {
        param([IO.FileInfo] $left, [IO.FileInfo] $right)
        [StringComparer]::Ordinal.Compare($left.FullName, $right.FullName)
    })
    foreach ($file in $packageFiles) {
        $relativePath = $file.FullName.Substring($packagePrefix.Length).
            Replace([IO.Path]::DirectorySeparatorChar, '/')
        $entry = $archive.CreateEntry(
            $relativePath,
            [IO.Compression.CompressionLevel]::Optimal
        )
        $entry.LastWriteTime = $file.LastWriteTime
        $inputStream = $file.OpenRead()
        $entryStream = $entry.Open()
        try { $inputStream.CopyTo($entryStream) }
        finally {
            $entryStream.Dispose()
            $inputStream.Dispose()
        }
    }
}
finally {
    if ($null -ne $archive) { $archive.Dispose() }
    else { $archiveStream.Dispose() }
}

$archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText(
    $archiveChecksumPath,
    "$archiveHash  $([IO.Path]::GetFileName($archivePath))`n",
    [Text.UTF8Encoding]::new($false)
)

& $innoCompiler `
    "/DAppVersion=$version" `
    "/DPayloadDir=$packageDirectory" `
    "/DArtifactDir=$artifactsDirectory" `
    '/Qp' `
    $installerScriptPath
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE."
}
if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
    throw "Installer output is missing: $installerPath"
}

Copy-Item -LiteralPath $manifestPath -Destination $installerManifestPath
$installerHash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText(
    $installerChecksumPath,
    "$installerHash  $([IO.Path]::GetFileName($installerPath))`n",
    [Text.UTF8Encoding]::new($false)
)

Write-Host "Package directory: $packageDirectory"
Write-Host "Package archive:   $archivePath"
Write-Host "File manifest:     $manifestPath"
Write-Host "Archive checksum:  $archiveChecksumPath"
Write-Host "Installer:         $installerPath"
Write-Host "Installer manifest: $installerManifestPath"
Write-Host "Installer checksum: $installerChecksumPath"
