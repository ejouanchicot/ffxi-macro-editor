# Builds the Windows icon from the artwork: seven square sizes, each a PNG inside the .ico.
# Windows picks a size rather than scaling one, so a 256 squeezed into 16 pixels turns to mud.
# The artwork is taller than it is wide, so it is centred on a transparent square first.
param(
    [Parameter(Mandatory = $true)] [string] $Source,
    [Parameter(Mandatory = $true)] [string] $Destination
)

Add-Type -AssemblyName System.Drawing

$art = [System.Drawing.Image]::FromFile($Source)
$sizes = 16, 24, 32, 48, 64, 128, 256
$frames = @()

foreach ($size in $sizes) {
    $canvas = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($canvas)
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)

    # Fit inside the square, keeping the proportions, centred.
    $scale = [Math]::Min($size / $art.Width, $size / $art.Height)
    $w = [int][Math]::Round($art.Width * $scale)
    $h = [int][Math]::Round($art.Height * $scale)
    $g.DrawImage($art, [int](($size - $w) / 2), [int](($size - $h) / 2), $w, $h)
    $g.Dispose()

    $buffer = New-Object System.IO.MemoryStream
    $canvas.Save($buffer, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames += , @{ Size = $size; Bytes = $buffer.ToArray() }
    $buffer.Dispose()
    $canvas.Dispose()
}

$art.Dispose()

# ICO container: a six-byte header, then a sixteen-byte entry per image, then the images.
$out = [System.IO.File]::Create($Destination)
$writer = New-Object System.IO.BinaryWriter $out

$writer.Write([UInt16]0)                       # reserved
$writer.Write([UInt16]1)                       # type: icon
$writer.Write([UInt16]$frames.Count)

$offset = 6 + (16 * $frames.Count)
foreach ($frame in $frames) {
    $writer.Write([Byte]($(if ($frame.Size -ge 256) { 0 } else { $frame.Size })))   # 0 means 256
    $writer.Write([Byte]($(if ($frame.Size -ge 256) { 0 } else { $frame.Size })))
    $writer.Write([Byte]0)                     # palette colours
    $writer.Write([Byte]0)                     # reserved
    $writer.Write([UInt16]1)                   # colour planes
    $writer.Write([UInt16]32)                  # bits per pixel
    $writer.Write([UInt32]$frame.Bytes.Length)
    $writer.Write([UInt32]$offset)
    $offset += $frame.Bytes.Length
}

foreach ($frame in $frames) {
    $writer.Write($frame.Bytes)
}

$writer.Flush()
$writer.Dispose()
$out.Dispose()

"{0}: {1} sizes, {2} bytes" -f (Split-Path $Destination -Leaf), $frames.Count, (Get-Item $Destination).Length
