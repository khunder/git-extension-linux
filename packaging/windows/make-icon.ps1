# Regenerates git-extensions.ico (PNG-compressed, 16..256 px) from the Linux icon PNGs.
#   pwsh packaging/windows/make-icon.ps1
$ErrorActionPreference = 'Stop'
$iconDir = Join-Path $PSScriptRoot '..\linux\icons' | Resolve-Path
$out = Join-Path $PSScriptRoot 'git-extensions.ico'
$sizes = 16, 32, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) { , [System.IO.File]::ReadAllBytes((Join-Path $iconDir "git-extensions-$s.png")) }

$ms = [System.IO.MemoryStream]::new()
$bw = [System.IO.BinaryWriter]::new($ms)
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $d = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }   # 0 means 256 in the ICO directory
    $bw.Write([byte]$d); $bw.Write([byte]$d); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$pngs[$i].Length); $bw.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Flush()
[System.IO.File]::WriteAllBytes($out, $ms.ToArray())
Write-Host "Wrote $out ($($ms.Length) bytes)"
