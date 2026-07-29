# Builds the 1280x640 card GitHub shows when the repository link is shared.
#
# Written after the artwork changed and the card still carried the old book: it had been made by
# hand, so there was nothing to re-run. The measurements below were sampled off that first card,
# which is why they are what they are — the layout is its layout, not a fresh opinion.
param(
    [string] $Artwork = "Icone.png",
    [string] $Destination = "social-preview.png"
)

Add-Type -AssemblyName System.Drawing

$width = 1280
$height = 640

$card = New-Object System.Drawing.Bitmap $width, $height
$g = [System.Drawing.Graphics]::FromImage($card)
$g.SmoothingMode = 'AntiAlias'
$g.InterpolationMode = 'HighQualityBicubic'
$g.PixelOffsetMode = 'HighQuality'
$g.TextRenderingHint = 'ClearTypeGridFit'

# The background lightens towards the bottom right, as the first card did.
$corner = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    (New-Object System.Drawing.Point 0, 0),
    (New-Object System.Drawing.Point $width, $height),
    [System.Drawing.Color]::FromArgb(0x17, 0x1B, 0x26),
    [System.Drawing.Color]::FromArgb(0x1F, 0x24, 0x34))
$g.FillRectangle($corner, 0, 0, $width, $height)
$corner.Dispose()

# The book, upright and centred in the left third.
$art = [System.Drawing.Image]::FromFile((Resolve-Path $Artwork))
$artHeight = 300
$artWidth = [int][Math]::Round($art.Width * ($artHeight / $art.Height))
$g.DrawImage($art, [int](270 - ($artWidth / 2)), [int](320 - ($artHeight / 2)), $artWidth, $artHeight)
$art.Dispose()

function Write-Line([string] $text, [string] $family, [int] $size, [string] $style, [int] $x, [int] $y, [int] $r, [int] $gr, [int] $b) {
    $font = New-Object System.Drawing.Font $family, $size, ([System.Drawing.FontStyle]$style), ([System.Drawing.GraphicsUnit]::Pixel)
    $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb($r, $gr, $b))
    $g.DrawString($text, $font, $brush, [float]$x, [float]$y)
    $brush.Dispose()
    $font.Dispose()
}

# The y here is where the text box starts, not where the ink does; these were tuned until the
# bands of ink landed on the same rows as the first card's, to the pixel.
$face = "Segoe UI"
Write-Line "FFXI Macro Editor"  $face 76 Bold    496 191  0xEE 0xF2 0xFA
Write-Line "Your 800 macros, on one screen." $face 38 Regular 498 289  0x6B 0xB1 0xFF

# The gold rule, 89 by 3, exactly where it sat.
$rule = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0xE0, 0xA6, 0x3C))
$g.FillRectangle($rule, 501, 361, 89, 3)
$rule.Dispose()

Write-Line "40 books, 400 sets, 8000 macros - edited like text," $face 27 Regular 498 390  0xE8 0xEC 0xF4
Write-Line "byte-exact with the game's own format."               $face 27 Regular 498 424  0xE8 0xEC 0xF4

$g.Dispose()
$card.Save($Destination, [System.Drawing.Imaging.ImageFormat]::Png)
$card.Dispose()

"{0}: {1}x{2}, {3} bytes" -f (Split-Path $Destination -Leaf), $width, $height, (Get-Item $Destination).Length
