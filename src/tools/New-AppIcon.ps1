<#
.SYNOPSIS
    Generates the application icon (src\InstantVolumeReducer\Resources\AppIcon.ico).

.DESCRIPTION
    Draws the "normal" tray icon (blue speaker with two sound waves) at 16, 24, 32, 48 and 256 px and
    writes them into one .ico file with PNG-compressed entries. The shapes mirror the drawing code in
    src\InstantVolumeReducer\UI\TrayIcons.cs (16x16 design grid); keep the two in sync if the icon changes.

    The generated .ico is committed, so this script only needs to run again when the icon design changes.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File src\tools\New-AppIcon.ps1
#>
param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\InstantVolumeReducer\Resources\AppIcon.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$designSize = 16.0
$color = [System.Drawing.Color]::FromArgb(0x2D, 0x7D, 0xD2)
$sizes = 16, 24, 32, 48, 256

function New-IconPng([int]$size) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.ScaleTransform($size / $designSize, $size / $designSize)

        # Speaker (same points as TrayIcons.DrawSpeaker).
        $brush = New-Object System.Drawing.SolidBrush $color
        $points = [System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF 1.0, 5.5),
            (New-Object System.Drawing.PointF 4.5, 5.5),
            (New-Object System.Drawing.PointF 8.5, 2.0),
            (New-Object System.Drawing.PointF 8.5, 14.0),
            (New-Object System.Drawing.PointF 4.5, 10.5),
            (New-Object System.Drawing.PointF 1.0, 10.5)
        )
        $graphics.FillPolygon($brush, $points)
        $brush.Dispose()

        # Sound waves (same arcs as TrayIcons.DrawSoundWaves).
        $pen = New-Object System.Drawing.Pen $color, 1.6
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $graphics.DrawArc($pen, 6.5, 5.0, 6.0, 6.0, -50.0, 100.0)
        $graphics.DrawArc($pen, 5.0, 2.0, 10.0, 12.0, -50.0, 100.0)
        $pen.Dispose()
    }
    finally {
        $graphics.Dispose()
    }

    $stream = New-Object System.IO.MemoryStream
    try {
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        return , $stream.ToArray()
    }
    finally {
        $stream.Dispose()
        $bitmap.Dispose()
    }
}

$images = @()
foreach ($size in $sizes) {
    $images += , @{ Size = $size; Data = (New-IconPng $size) }
}

$output = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $output
try {
    # ICONDIR header: reserved, type (1 = icon), image count.
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$images.Count)

    # ICONDIRENTRY per image; the PNG data follows after all entries.
    $offset = 6 + 16 * $images.Count
    foreach ($image in $images) {
        $dimension = if ($image.Size -ge 256) { 0 } else { $image.Size }  # 0 means 256
        $writer.Write([byte]$dimension)          # width
        $writer.Write([byte]$dimension)          # height
        $writer.Write([byte]0)                   # palette colour count
        $writer.Write([byte]0)                   # reserved
        $writer.Write([uint16]1)                 # colour planes
        $writer.Write([uint16]32)                # bits per pixel
        $writer.Write([uint32]$image.Data.Length)
        $writer.Write([uint32]$offset)
        $offset += $image.Data.Length
    }
    foreach ($image in $images) {
        $writer.Write([byte[]]$image.Data)
    }
    $writer.Flush()

    $fullPath = [System.IO.Path]::GetFullPath($OutputPath)
    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($fullPath)) | Out-Null
    [System.IO.File]::WriteAllBytes($fullPath, $output.ToArray())
    Write-Output "Wrote $fullPath ($($output.Length) bytes, sizes: $($sizes -join ', '))"
}
finally {
    $writer.Dispose()
    $output.Dispose()
}
