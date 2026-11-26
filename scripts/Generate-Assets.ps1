$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assets = Join-Path $PSScriptRoot '..\Assets'
$source = [System.Drawing.Image]::FromFile((Join-Path $assets 'AppIconSource.png'))

function Save-Logo([string] $Name, [int] $Width, [int] $Height, [int] $IconSize) {
    $bitmap = [System.Drawing.Bitmap]::new($Width, $Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.DrawImage($source, [int](($Width - $IconSize) / 2), [int](($Height - $IconSize) / 2), $IconSize, $IconSize)
        $bitmap.Save((Join-Path $assets $Name), [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

try {
    foreach ($scale in 100, 125, 150, 200, 400) {
        foreach ($size in 44, 71, 150, 310) {
            $pixels = [int][Math]::Round($size * $scale / 100)
            Save-Logo "Square$($size)x$($size)Logo.scale-$scale.png" $pixels $pixels $pixels
        }
        $width = [int][Math]::Round(310 * $scale / 100)
        $height = [int][Math]::Round(150 * $scale / 100)
        $iconSize = [int][Math]::Round(120 * $scale / 100)
        Save-Logo "Wide310x150Logo.scale-$scale.png" $width $height $iconSize
        Save-Logo "SplashScreen.scale-$scale.png" $width $height $iconSize
    }
    foreach ($size in 16, 24, 32, 48, 256) {
        Save-Logo "Square44x44Logo.targetsize-$size`_altform-unplated.png" $size $size $size
        Save-Logo "Square44x44Logo.targetsize-$size`_altform-lightunplated.png" $size $size $size
    }
    Save-Logo 'StoreLogo.png' 50 50 50
    Save-Logo 'LockScreenLogo.scale-200.png' 48 48 48

    $sizes = @(16, 24, 32, 48, 256)
    $images = @($sizes | ForEach-Object {
        ,([System.IO.File]::ReadAllBytes((Join-Path $assets "Square44x44Logo.targetsize-$_`_altform-unplated.png")))
    })
    $stream = [System.IO.File]::Create((Join-Path $assets 'AppIcon.ico'))
    $writer = [System.IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$sizes.Count)
        $offset = 6 + 16 * $sizes.Count
        for ($i = 0; $i -lt $sizes.Count; $i++) {
            $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
            $writer.Write([byte]$dimension)
            $writer.Write([byte]$dimension)
            $writer.Write([uint16]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$images[$i].Length)
            $writer.Write([uint32]$offset)
            $offset += $images[$i].Length
        }
        foreach ($image in $images) { $writer.Write([byte[]]$image) }
    }
    finally { $writer.Dispose() }
}
finally { $source.Dispose() }
