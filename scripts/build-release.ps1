$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$converterDir = Split-Path -Parent $scriptDir
$outputDir = Join-Path $scriptDir "output"
$issPath = Join-Path $scriptDir "LceWorldConverter.iss"

$clean = $false
$version = $null
$config = "Release"
$runtimeList = @("win-x64", "linux-x64", "osx-x64", "osx-arm64")

for ($index = 0; $index -lt $args.Count; $index++) {
    switch -Regex ($args[$index]) {
        '^--clean$|^-Clean$' {
            $clean = $true
            continue
        }
        '^--version$|^-Version$' {
            if ($index + 1 -ge $args.Count) {
                throw "Missing value for --version"
            }

            $index++
            $version = $args[$index]
            continue
        }
        '^--config$|^-Config$' {
            if ($index + 1 -ge $args.Count) {
                throw "Missing value for --config"
            }

            $index++
            $config = $args[$index]
            continue
        }
        '^--runtime$|^-Runtime$' {
            if ($index + 1 -ge $args.Count) {
                throw "Missing value for --runtime"
            }

            $index++
            $runtimeList = $args[$index].Split(',', [System.StringSplitOptions]::RemoveEmptyEntries) | ForEach-Object { $_.Trim() }
            continue
        }
        default {
            throw "Unknown argument: $($args[$index])"
        }
    }
}

function Normalize-Version([string]$value) {
    $trimmed = $value.Trim()
    if ([string]::IsNullOrWhiteSpace($trimmed)) {
        throw "Version cannot be empty."
    }

    if ($trimmed.StartsWith('v', [System.StringComparison]::OrdinalIgnoreCase)) {
        return "v$($trimmed.Substring(1))"
    }

    return "v$trimmed"
}

function Get-VersionMetadata([string]$versionLabelValue) {
    $packageVersion = $versionLabelValue.TrimStart('v')
    $match = [System.Text.RegularExpressions.Regex]::Match(
        $packageVersion,
        '^(\d+)(?:\.(\d+))?(?:\.(\d+))?(?:\.(\d+))?(?:[-+].*)?$')

    if (-not $match.Success) {
        throw "Version '$packageVersion' must start with 1 to 4 numeric components (for example 2.2.1 or 2.2.1.0)."
    }

    $parts = @()
    for ($i = 1; $i -le 4; $i++) {
        $groupValue = $match.Groups[$i].Value
        if ([string]::IsNullOrWhiteSpace($groupValue)) {
            $parts += '0'
        }
        else {
            $parts += $groupValue
        }
    }

    return @{
        PackageVersion = $packageVersion
        AssemblyVersion = ($parts -join '.')
        FileVersion = ($parts -join '.')
        InformationalVersion = $versionLabelValue
    }
}

function Remove-IfExists([string]$path) {
    if (Test-Path $path) {
        Remove-Item -Recurse -Force $path
    }
}

function Get-IsccPath {
    $command = Get-Command iscc.exe -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $candidates = @(
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
        'C:\Program Files\Inno Setup 6\ISCC.exe'
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    throw "Inno Setup compiler (ISCC.exe) was not found."
}

function Publish-SingleFile([string]$projectPath, [string]$runtime, [string]$publishDir, [string[]]$versionProps) {
    & dotnet publish $projectPath `
        -c $config `
        -r $runtime `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -p:PublishTrimmed=false `
        -o $publishDir `
        @versionProps | Out-Host
}

function New-PortableReadme([string]$path, [string]$title, [string]$versionLabelValue, [string[]]$lines) {
    $content = @(
        $title
        "Version: $versionLabelValue"
        ""
        $lines
    ) -join [Environment]::NewLine

    Set-Content -Path $path -Value $content -Encoding ASCII
}

function Get-ExecutableNameForRuntime([string]$baseName, [string]$runtime) {
    if ($runtime.StartsWith("win-", [System.StringComparison]::OrdinalIgnoreCase)) {
        return "$baseName.exe"
    }

    return $baseName
}

function New-ZipFromDirectory([string]$sourceDir, [string]$destinationZip) {
    if (Test-Path $destinationZip) {
        Remove-Item $destinationZip -Force
    }

    Compress-Archive -Path (Join-Path $sourceDir "*") -DestinationPath $destinationZip
}

if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Missing required --version <tag>. Example: --version v2.2.0"
}

if (-not $runtimeList -or $runtimeList.Count -eq 0) {
    throw "At least one runtime must be supplied."
}

$versionLabel = Normalize-Version $version
$versionMetadata = Get-VersionMetadata $versionLabel
$normalizedVersion = $versionMetadata.PackageVersion
$publicArtifactBaseName = "LCE-Save-Converter-$versionLabel"
$stageRoot = Join-Path ([System.IO.Path]::GetTempPath()) "LceSaveConverter-build"
$stageStamp = Get-Date -Format "yyyyMMdd-HHmmss"
$stagingDir = Join-Path $stageRoot "$publicArtifactBaseName-$stageStamp"
$guiArtifactZipPath = Join-Path $outputDir "LCE-Save-Converter-GUI-$versionLabel-win-x64.zip"
$installerBaseName = "LCE-Save-Converter-$versionLabel-setup"
$installerExePath = Join-Path $outputDir "$installerBaseName.exe"
$isccPath = Get-IsccPath

$publishProps = @(
    "/p:Version=$normalizedVersion",
    "/p:AssemblyVersion=$($versionMetadata.AssemblyVersion)",
    "/p:FileVersion=$($versionMetadata.FileVersion)",
    "/p:InformationalVersion=$($versionMetadata.InformationalVersion)"
)

if ($clean) {
    try {
        Remove-IfExists $stageRoot
    }
    catch {
        Write-Warning "Could not fully clean staging folders: $($_.Exception.Message)"
    }

    Get-ChildItem $outputDir -ErrorAction SilentlyContinue | ForEach-Object {
        if ($_.Name -like "LCE-Save-Converter*-$versionLabel*") {
            Remove-Item -LiteralPath $_.FullName -Recurse -Force
        }
    }
}

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
New-Item -ItemType Directory -Force -Path $stageRoot | Out-Null
New-Item -ItemType Directory -Force -Path $stagingDir | Out-Null

$builtArtifacts = New-Object System.Collections.Generic.List[string]

Push-Location $converterDir
try {
    foreach ($runtime in $runtimeList) {
        $runtimePublishDir = Join-Path $stagingDir "publish\$runtime\cli"
        $runtimePackageDir = Join-Path $stagingDir "package\$runtime\cli"
        New-Item -ItemType Directory -Force -Path $runtimePublishDir | Out-Null
        New-Item -ItemType Directory -Force -Path $runtimePackageDir | Out-Null

        Publish-SingleFile ".\LceWorldConverter.Cli\LceWorldConverter.Cli.csproj" $runtime $runtimePublishDir $publishProps

        $publishedCliName = Get-ExecutableNameForRuntime "LceWorldConverter.Cli" $runtime
        $cliPath = Join-Path $runtimePublishDir $publishedCliName
        if (-not (Test-Path $cliPath)) {
            throw "CLI executable not found for ${runtime}: $cliPath"
        }

        $cliName = Get-ExecutableNameForRuntime "LceWorldConverter" $runtime
        Copy-Item -Path $cliPath -Destination (Join-Path $runtimePackageDir $cliName) -Force
        New-PortableReadme (Join-Path $runtimePackageDir "README.txt") "LCE Save Converter CLI" $versionLabel @(
            ""
            "Files:"
            "- $cliName : Command-line interface"
            ""
            "Usage:"
            "- Run the executable directly from this folder."
        )

        $runtimeZipPath = Join-Path $outputDir "$publicArtifactBaseName-$runtime.zip"
        New-ZipFromDirectory $runtimePackageDir $runtimeZipPath
        $builtArtifacts.Add($runtimeZipPath)
    }

    $guiPublishDir = Join-Path $stagingDir "publish\win-x64\gui"
    $guiPackageDir = Join-Path $stagingDir "package\win-x64\gui"
    $installerRoot = Join-Path $stagingDir "package\win-x64\installer"
    New-Item -ItemType Directory -Force -Path $guiPublishDir | Out-Null
    New-Item -ItemType Directory -Force -Path $guiPackageDir | Out-Null
    New-Item -ItemType Directory -Force -Path $installerRoot | Out-Null

    Publish-SingleFile ".\LceWorldConverter.Gui\LceWorldConverter.Gui.csproj" "win-x64" $guiPublishDir $publishProps

    $guiExe = Join-Path $guiPublishDir "LceWorldConverter.Gui.exe"
    $winCliExe = Join-Path (Join-Path $stagingDir "publish\win-x64\cli") "LceWorldConverter.Cli.exe"
    if (-not (Test-Path $guiExe)) {
        throw "GUI executable not found: $guiExe"
    }
    if (-not (Test-Path $winCliExe)) {
        throw "Windows CLI executable not found: $winCliExe"
    }

    Copy-Item -Path $guiExe -Destination (Join-Path $guiPackageDir "LceWorldConverter.Gui.exe") -Force
    New-PortableReadme (Join-Path $guiPackageDir "README.txt") "LCE Save Converter GUI" $versionLabel @(
        ""
        "Files:"
        "- LceWorldConverter.Gui.exe : Desktop GUI"
        ""
        "Usage:"
        "- Run LceWorldConverter.Gui.exe directly."
    )

    Copy-Item -Path $guiExe -Destination (Join-Path $installerRoot "LceWorldConverter.Gui.exe") -Force
    Copy-Item -Path $winCliExe -Destination (Join-Path $installerRoot "LceWorldConverter.exe") -Force
    New-PortableReadme (Join-Path $installerRoot "README.txt") "LCE Save Converter" $versionLabel @(
        ""
        "Files:"
        "- LceWorldConverter.Gui.exe : Desktop GUI"
        "- LceWorldConverter.exe     : Command-line interface"
        ""
        "Usage:"
        "- Run LceWorldConverter.Gui.exe directly, or use the installer."
    )

    New-ZipFromDirectory $guiPackageDir $guiArtifactZipPath
    $builtArtifacts.Add($guiArtifactZipPath)

    $isccArgs = @(
        "/DMyAppVersion=$versionLabel",
        "/DMyAppRoot=$installerRoot",
        "/DMyOutputDir=$outputDir",
        "/DMyInstallerBaseName=$installerBaseName",
        $issPath
    )

    & $isccPath @isccArgs | Out-Host

    if (-not (Test-Path $installerExePath)) {
        throw "Installer was not created: $installerExePath"
    }

    $builtArtifacts.Add($installerExePath)
}
finally {
    Pop-Location
}

$checksumsPath = Join-Path $outputDir "$publicArtifactBaseName-sha256.txt"
if (Test-Path $checksumsPath) {
    Remove-Item $checksumsPath -Force
}

$checksumLines = foreach ($artifact in $builtArtifacts) {
    $hash = (Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash *$([System.IO.Path]::GetFileName($artifact))"
}
Set-Content -Path $checksumsPath -Value $checksumLines -Encoding ASCII

try {
    Remove-IfExists $stagingDir
    if ((Test-Path $stageRoot) -and -not (Get-ChildItem $stageRoot -Force | Select-Object -First 1)) {
        Remove-Item $stageRoot -Force
    }
}
catch {
    Write-Warning "Could not remove staging folder '$stagingDir': $($_.Exception.Message)"
}

Write-Host ""
Write-Host "Artifacts:"
foreach ($artifact in $builtArtifacts) {
    Write-Host "  $artifact"
}
Write-Host "Checksums:"
Write-Host "  $checksumsPath"
Write-Host "Version:"
Write-Host "  $versionLabel"
Write-Host "Config:"
Write-Host "  $config"
