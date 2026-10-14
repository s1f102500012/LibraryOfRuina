param(
    [ValidateSet("0.107.1", "0.111.0")]
    [string[]]$Targets = @("0.111.0"),
    [string]$DevRoot = "D:\sls2-resource",
    [string]$GameDir = "D:\SteamLibrary\steamapps\common\Slay the Spire 2",
    [string]$GameRefsRoot = "",
    [string]$LibraryOfRuinaLibRoot = "",
    [string]$ActLikeIt2Root = "",
    [string]$OutputPath = "",
    [switch]$InspectOnly
)
$ErrorActionPreference = "Stop"
if (!$GameRefsRoot) { $GameRefsRoot = Join-Path $DevRoot "versioned-dll-backups" }
if (!$LibraryOfRuinaLibRoot) { $LibraryOfRuinaLibRoot = Join-Path $DevRoot "LibraryOfRuinaLib" }
if (!$ActLikeIt2Root) { $ActLikeIt2Root = Join-Path $DevRoot "ActLikeIt2" }
if (!$OutputPath) { $OutputPath = Join-Path $PSScriptRoot "sts2-devrefs.zip" }
$Targets = @($Targets | Select-Object -Unique)
$requiredGame = @("sts2.dll", "GodotSharp.dll", "0Harmony.dll", "Steamworks.NET.dll")

function Read-ModManifest([string]$Root, [string]$Id) {
    foreach ($name in @("$Id.json", "mod_manifest.json")) {
        $path = Join-Path $Root $name
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            $value = Get-Content -LiteralPath $path -Raw -Encoding utf8 | ConvertFrom-Json
            if ($value.id -eq $Id) {
                return [pscustomobject]@{ Path = $path; Version = [version]($value.version -replace "^v", "") }
            }
        }
    }
    return $null
}
function Resolve-GameRefs([string]$Target) {
    foreach ($relative in @("$Target/game-refs", "$Target")) {
        $candidate = Join-Path $GameRefsRoot $relative
        $absent = @($requiredGame | Where-Object { !(Test-Path -LiteralPath (Join-Path $candidate $_) -PathType Leaf) })
        if ($absent.Count -eq 0) {
            return [pscustomobject]@{ Path = $candidate; Basis = "versioned-backup-directory" }
        }
    }
    $releaseInfo = Join-Path $GameDir "release_info.json"
    if (Test-Path -LiteralPath $releaseInfo -PathType Leaf) {
        $info = Get-Content -LiteralPath $releaseInfo -Raw -Encoding utf8 | ConvertFrom-Json
        if (($info.version -replace "^v", "") -eq $Target) {
            $candidate = Join-Path $GameDir "data_sts2_windows_x86_64"
            $absent = @($requiredGame | Where-Object { !(Test-Path -LiteralPath (Join-Path $candidate $_) -PathType Leaf) })
            if ($absent.Count -eq 0) {
                return [pscustomobject]@{ Path = $candidate; Basis = "installed-game-release_info.json" }
            }
        }
    }
    throw "Missing complete game references for $Target. Expected $GameRefsRoot/$Target/game-refs/ ($($requiredGame -join ', ')), or a matching installed game's release_info.json."
}
function Resolve-Dependency([string]$Id, [version]$Minimum, [string]$Target, [string]$SourceRoot, [string]$WorkshopId) {
    $gameParent = [System.IO.Directory]::GetParent([System.IO.Path]::GetFullPath($GameDir))
    $steamApps = if ($gameParent -and $gameParent.Parent) { $gameParent.Parent.FullName } else { $GameDir }
    $roots = @(
        $SourceRoot,
        (Join-Path $DevRoot "mods_disabled/$Id"),
        (Join-Path $GameDir "mods/$Id"),
        (Join-Path $steamApps "workshop/content/2868840/$WorkshopId")
    ) | Select-Object -Unique
    $rejected = [System.Collections.Generic.List[string]]::new()
    foreach ($root in $roots) {
        $manifest = Read-ModManifest $root $Id
        if (!$manifest) { continue }
        if ($manifest.Version -lt $Minimum) {
            $rejected.Add("$root has version $($manifest.Version), need >=$Minimum")
            continue
        }
        $relatives = @(
            "lib/$Target/$Id.dll",
            "build/variants/$Target/$Id.dll",
            "build/bin/LibraryLib/$Target/Release/net9.0/$Id.dll",
            "build/bin/$Id/$Target/Release/net9.0/$Id.dll",
            "build/bin/$Id/$Target/Release/$Id.dll"
        )
        foreach ($relative in $relatives) {
            $dll = Join-Path $root $relative
            if (!(Test-Path -LiteralPath $dll -PathType Leaf)) { continue }
            $identity = [System.Reflection.AssemblyName]::GetAssemblyName($dll)
            if ($identity.Name -ne $Id) {
                $rejected.Add("$dll is $($identity.Name); an implementation named $Id is required")
                continue
            }
            $marker = Join-Path (Split-Path $dll -Parent) "compat-target.txt"
            if ((Test-Path -LiteralPath $marker -PathType Leaf) -and
                (Get-Content -LiteralPath $marker -Raw).Trim() -ne $Target) {
                $rejected.Add("$dll has a mismatched compatibility marker")
                continue
            }
            return [pscustomobject]@{
                Dll = $dll; Manifest = $manifest.Path
                Version = $manifest.Version.ToString(); AssemblyName = $identity.Name
                AssemblyVersion = $identity.Version.ToString()
            }
        }
    }
    $detail = if ($rejected.Count) { " Rejected: " + ($rejected -join "; ") } else { "" }
    throw "Missing $Id >=$Minimum implementation for $Target. Checked local source outputs, installed bundles, and Workshop.$detail"
}

$resolved = [System.Collections.Generic.List[object]]::new()
$missing = [System.Collections.Generic.List[string]]::new()
foreach ($target in $Targets) {
    $refs = $null; $library = $null; $act = $null
    try { $refs = Resolve-GameRefs $target } catch { $missing.Add($_.Exception.Message) }
    try { $library = Resolve-Dependency "LibraryOfRuinaLib" ([version]"1.3.5") $target $LibraryOfRuinaLibRoot "3747541096" } catch { $missing.Add($_.Exception.Message) }
    try { $act = Resolve-Dependency "ActLikeIt2" ([version]"0.2.3") $target $ActLikeIt2Root "3808369281" } catch { $missing.Add($_.Exception.Message) }
    if ($refs -and $library -and $act) {
        foreach ($name in $requiredGame) {
            $identity = [System.Reflection.AssemblyName]::GetAssemblyName((Join-Path $refs.Path $name))
            $expected = [System.IO.Path]::GetFileNameWithoutExtension($name)
            if ($identity.Name -ne $expected) { throw "Invalid assembly identity for $name : $($identity.Name)" }
        }
        $resolved.Add([pscustomobject]@{ Target = $target; Game = $refs; Library = $library; Act = $act })
    }
}
if ($missing.Count) {
    foreach ($message in $missing) { [Console]::Error.WriteLine($message) }
    exit 1
}
if ($InspectOnly) {
    $resolved | ConvertTo-Json -Depth 8
    exit 0
}
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $OutputPath) { throw "Output already exists; choose a new -OutputPath: $OutputPath" }
$stage = Join-Path ([System.IO.Path]::GetTempPath()) ("sts2-devrefs-" + [guid]::NewGuid().ToString("N"))
$files = [System.Collections.Generic.List[object]]::new()
$sourceItems = [System.Collections.Generic.List[object]]::new()
$destinations = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
try {
    New-Item -ItemType Directory -Path $stage | Out-Null
    foreach ($item in $resolved) {
        foreach ($file in Get-ChildItem -LiteralPath $item.Game.Path -File -Filter "*.dll") {
            $relative = "versioned-dll-backups/$($item.Target)/game-refs/$($file.Name)"
            $sourceItems.Add([pscustomobject]@{ Source = $file.FullName; Path = $relative })
        }
        foreach ($pair in @(@("LibraryOfRuinaLib", $item.Library), @("ActLikeIt2", $item.Act))) {
            $id = $pair[0]; $dependency = $pair[1]
            $sourceItems.Add([pscustomobject]@{ Source = $dependency.Dll; Path = ".cloud-deps/$id/lib/$($item.Target)/$id.dll" })
            if ($destinations.Add(".cloud-deps/$id/$id.json")) {
                $sourceItems.Add([pscustomobject]@{ Source = $dependency.Manifest; Path = ".cloud-deps/$id/$id.json" })
            }
        }
    }
    foreach ($item in $sourceItems) {
        $destination = Join-Path $stage $item.Path
        New-Item -ItemType Directory -Force -Path (Split-Path $destination -Parent) | Out-Null
        Copy-Item -LiteralPath $item.Source -Destination $destination
        $files.Add([ordered]@{
            path = $item.Path
            size = (Get-Item -LiteralPath $destination).Length
            sha256 = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
        })
    }
    $package = [ordered]@{
        format = "sts2-cloud-devrefs/v1"
        created_utc = [DateTime]::UtcNow.ToString("o")
        targets = $Targets
        game_target_basis = @($resolved | ForEach-Object { [ordered]@{ target = $_.Target; basis = $_.Game.Basis } })
        files = $files.ToArray()
    }
    $utf8 = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText((Join-Path $stage "devrefs-manifest.json"), ($package | ConvertTo-Json -Depth 10), $utf8)
    New-Item -ItemType Directory -Force -Path (Split-Path $OutputPath -Parent) | Out-Null
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $OutputPath)
    Write-Output "Created: $OutputPath"
    Write-Output "Targets: $($Targets -join ', '); files: $($files.Count)"
    Write-Output "SHA256: $((Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256).Hash)"
    Write-Output "Upload this ZIP to the cloud task. It contains compile references only; no PCK, save files or credentials."
} finally {
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
}
