param(
    [Parameter(Mandatory = $true)][string]$ArchivePath,
    [string]$OutputDirectory = "",
    [ValidateRange(1, 30)][int]$PartSizeMiB = 30
)
$ErrorActionPreference = "Stop"
function Split-ReferenceArchive {
    param(
        [Parameter(Mandatory = $true)][string]$ArchivePath,
        [string]$OutputDirectory = "",
        [ValidateRange(1, 30)][int]$PartSizeMiB = 30
    )
    $sourcePath = [System.IO.Path]::GetFullPath($ArchivePath)
    if (!(Test-Path -LiteralPath $sourcePath -PathType Leaf)) { throw "Archive not found: $sourcePath" }
    $source = Get-Item -LiteralPath $sourcePath
    if ($source.Length -le 0 -or $source.Length -gt 2GB) { throw "Expected a nonempty ZIP no larger than 2 GiB." }
    if (!$OutputDirectory) { $OutputDirectory = "$sourcePath.parts" }
    $OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
    if (Test-Path -LiteralPath $OutputDirectory) { throw "Output directory already exists; choose a new -OutputDirectory: $OutputDirectory" }
    $capacity = [long]$PartSizeMiB * 1MB
    $partCount = [int][Math]::Ceiling($source.Length / [double]$capacity)
    $sourceHash = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $parent = [System.IO.Path]::GetDirectoryName($OutputDirectory)
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
    $stage = Join-Path $parent (".devrefs-parts-" + [guid]::NewGuid().ToString("N"))
    $inputStream = $null
    try {
        New-Item -ItemType Directory -Path $stage | Out-Null
        Add-Type -AssemblyName System.IO.Compression
        $inputStream = [System.IO.File]::OpenRead($sourcePath)
        $baseName = [System.IO.Path]::GetFileNameWithoutExtension($sourcePath)
        $utf8 = [System.Text.UTF8Encoding]::new($false)
        for ($index = 1; $index -le $partCount; $index++) {
            $count = [int][Math]::Min($capacity, $source.Length - $inputStream.Position)
            $buffer = [byte[]]::new($count)
            $offset = 0
            while ($offset -lt $count) {
                $read = $inputStream.Read($buffer, $offset, $count - $offset)
                if ($read -eq 0) { throw "Archive changed or ended while splitting." }
                $offset += $read
            }
            $sha = [System.Security.Cryptography.SHA256]::Create()
            try { $chunkHash = [System.BitConverter]::ToString($sha.ComputeHash($buffer)).Replace("-", "").ToLowerInvariant() }
            finally { $sha.Dispose() }
            $metadata = [ordered]@{
                format = "sts2-devrefs-parts/v1"
                archive_name = $source.Name
                archive_size = $source.Length
                archive_sha256 = $sourceHash
                part_index = $index
                part_count = $partCount
                part_capacity = $capacity
                chunk_size = $count
                chunk_sha256 = $chunkHash
            }
            $partName = ("{0}.part{1:D3}.zip" -f $baseName, $index)
            $partPath = Join-Path $stage $partName
            $outputStream = [System.IO.File]::Create($partPath)
            $zip = $null
            try {
                $zip = [System.IO.Compression.ZipArchive]::new($outputStream, [System.IO.Compression.ZipArchiveMode]::Create, $false)
                $entry = $zip.CreateEntry("chunk.bin", [System.IO.Compression.CompressionLevel]::NoCompression)
                $entryStream = $entry.Open()
                try { $entryStream.Write($buffer, 0, $count) } finally { $entryStream.Dispose() }
                $manifestEntry = $zip.CreateEntry("part-manifest.json", [System.IO.Compression.CompressionLevel]::NoCompression)
                $writer = [System.IO.StreamWriter]::new($manifestEntry.Open(), $utf8)
                try { $writer.Write(($metadata | ConvertTo-Json -Compress)) } finally { $writer.Dispose() }
            } finally {
                if ($zip) { $zip.Dispose() }
                $outputStream.Dispose()
            }
            if ((Get-Item -LiteralPath $partPath).Length -gt 32MB) { throw "A generated part exceeds the 32 MiB transfer limit." }
        }
        if ($inputStream.Position -ne $source.Length -or $inputStream.Length -ne $source.Length) { throw "Archive size changed while splitting." }
        $inputStream.Dispose()
        $inputStream = $null
        [System.IO.Directory]::Move($stage, $OutputDirectory)
        Write-Output "Created $partCount uploadable ZIP parts in: $OutputDirectory"
        Write-Output "Upload every *.part*.zip file in this directory. Keep the original ZIP."
        Write-Output "Original archive SHA256: $sourceHash"
    } finally {
        if ($inputStream) { $inputStream.Dispose() }
        if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
    }
}

Split-ReferenceArchive -ArchivePath $ArchivePath -OutputDirectory $OutputDirectory -PartSizeMiB $PartSizeMiB
