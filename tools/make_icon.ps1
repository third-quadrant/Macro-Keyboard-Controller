param(
    [string]$Source256 = 'C:\Users\user\Downloads\macropad_256.png',
    [string]$Source32 = 'C:\Users\user\Downloads\macropad_32.png',
    [string]$Out = 'C:\Users\user\orca\workspaces\new macro keyboard controller\fireworm\src\Fireworm.App\Assets\app.ico'
)

Add-Type -AssemblyName System.Drawing

$frames = @()
function Add-Frame([System.Drawing.Image]$image, [int]$size, [string]$mode) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.InterpolationMode = if ($mode -eq 'nearest') {
        [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    } else {
        [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    }
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.DrawImage($image, 0, 0, $size, $size)
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $script:frames += [pscustomobject]@{ Size = $size; Data = $ms.ToArray() }
    $ms.Dispose()
}

$big = [System.Drawing.Image]::FromFile($Source256)
$small = [System.Drawing.Image]::FromFile($Source32)

Add-Frame $small 16 'nearest'
Add-Frame $big 24 'bicubic'
Add-Frame $small 32 'nearest'
Add-Frame $big 48 'bicubic'
Add-Frame $big 64 'nearest'
Add-Frame $big 128 'nearest'
Add-Frame $big 256 'nearest'

$big.Dispose()
$small.Dispose()

New-Item -ItemType Directory -Force (Split-Path -Parent $Out) | Out-Null
$stream = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($stream)
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]$frames.Count)

$offset = 6 + (16 * $frames.Count)
foreach ($frame in $frames) {
    $dim = if ($frame.Size -ge 256) { 0 } else { $frame.Size }
    $writer.Write([byte]$dim)
    $writer.Write([byte]$dim)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]$frame.Data.Length)
    $writer.Write([uint32]$offset)
    $offset += $frame.Data.Length
}

foreach ($frame in $frames) { $writer.Write($frame.Data) }
$writer.Flush()
[System.IO.File]::WriteAllBytes($Out, $stream.ToArray())
$writer.Dispose()
$stream.Dispose()

$file = Get-Item $Out
Write-Output ("ICO=" + $file.FullName)
Write-Output ("SIZES=" + (($frames | ForEach-Object { $_.Size }) -join ','))
Write-Output ("BYTES=" + $file.Length)
