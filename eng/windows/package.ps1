param (
    $output = "dist",
    $config = "Release",
    $framework = "net10.0",
    $netRuntime = "win-x64",
    $isRelease = $true,
    $isPackage = $true,
    $isPortable = $false
)

$ErrorActionPreference = "Stop";
$PrevPath = $(Get-Location).Path;
$nl = [Environment]::NewLine;

function asBool {
    param ($value)

    if ($value -is [bool]) {
        return $value;
    }

    switch ("$value".ToLowerInvariant()) {
        "true" { return $true; }
        "1" { return $true; }
        "false" { return $false; }
        "0" { return $false; }
        default { return [bool]$value; }
    }
}

$isRelease = asBool $isRelease;
$isPackage = asBool $isPackage;
$isPortable = asBool $isPortable;

$Projects = @(
    "OpenTabletDriver.Daemon",
    "OpenTabletDriver.Console"
);

$UIProjects = @(
    "OpenTabletDriver.UX.Wpf"
);

$Options = @(
    "--configuration", "$config",
    "--runtime", "$netRuntime",
    "--no-self-contained",
    "--output", "$output",
    "/p:PublishSingleFile=true",
    "/p:PublishTrimmed=false",
    "/p:DebugType=embedded",
    "/p:SuppressNETCoreSdkPreviewMessage=true",
    "/p:VersionSuffix=$env:VERSION_SUFFIX"
);

if (!($isRelease)) {
    $Options += "/p:SourceRevisionId=$(git rev-parse --short HEAD)";
}

Write-Output "The powershell script is deprecated! Please use the BASH build system instead"

$prevErrorActionPreference = $ErrorActionPreference;
$ErrorActionPreference = "Continue";
$gitVersion = $(git describe --tags --abbrev=0 2>$null)
$gitDescribeExitCode = $LASTEXITCODE;
$ErrorActionPreference = $prevErrorActionPreference;
if ($gitDescribeExitCode -eq 0 -and ![string]::IsNullOrWhiteSpace($gitVersion)) {
  $gitVersion = $gitVersion.replace('v','');
  Write-Output "Git reports version $gitVersion";
} else {
  $gitVersion = $null;
}

function exitWithError {
    param ($message)

    Set-Location $PrevPath;
    Write-Error $message;
    exit 1;
}

# Change dir to repo root
Set-Location $PSScriptRoot/../..;

# Sanity check
if (!(Test-Path "./OpenTabletDriver")) {
    exitWithError "Could not find OpenTabletDriver folder!";
}

if (Test-Path "$output") {
    Write-Output "Cleaning old build outputs...";
    try {
        Get-ChildItem -Path "$output" | ForEach-Object {
            if ($_.Name -ne "userdata") {
                Remove-Item -Path $_.FullName -Recurse -Force;
            }
        }
    } catch {
        exitWithError "Could not clean old build dirs. Please manually remove contents of ./bin folder.";
    }
}

dotnet restore --verbosity quiet > $null
dotnet clean --configuration $config --verbosity quiet > $null;

Write-Output "Runtime = $netRuntime";
New-Item -ItemType Directory -Force -Path "$output" > $null;
if ($isPortable) {
    New-Item -ItemType Directory -Force -Path "$output/userdata" > $null;
}

foreach ($project in $Projects) {
    Write-Output "${nl}Building $project...$nl";
    dotnet publish $project $Options --framework $framework;
    if ($LASTEXITCODE -ne 0) {
        exitWithError "Build failed!";
    }
}

foreach ($project in $UIProjects) {
    Write-Output "${nl}Building $project...$nl";
    dotnet publish $project $Options --framework ${framework}-windows;
    if ($LASTEXITCODE -ne 0) {
        exitWithError "Build failed!";
    }
}

Write-Output "${nl}Build finished! Binaries created in $output";

if ($isPackage) {
    Write-Output "${nl}Creating package...";
    Copy-Item -Path $PSScriptRoot/convert_to_portable.bat -Destination $output;
    if (![string]::IsNullOrWhiteSpace($gitVersion)) {
      $zipPath = "$output/OpenTabletDriver-$gitVersion_$netRuntime.zip";
    } else {
      Write-Output "${nl}Unable to determine release version, using fallback naming for zip";
      $zipPath = "$output/OpenTabletDriver_$netRuntime.zip";
    }
    if (Test-Path $zipPath) {
        Remove-Item $zipPath;
    }
    Compress-Archive -Path "$output/*" -DestinationPath $zipPath;
    Write-Output "Packaging finished! Zip created in $zipPath";
}

Set-Location $PrevPath;
