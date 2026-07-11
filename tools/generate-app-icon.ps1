param(
    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

function New-IconBitmap {
    param([int]$Size)

    $bitmap = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

    try {
        $scale = $Size / 128.0

        $bgRect = New-Object System.Drawing.RectangleF(8 * $scale, 8 * $scale, 112 * $scale, 112 * $scale)
        $bgPath = New-Object System.Drawing.Drawing2D.GraphicsPath
        $radius = 22 * $scale
        $diameter = $radius * 2
        $bgPath.AddArc($bgRect.X, $bgRect.Y, $diameter, $diameter, 180, 90)
        $bgPath.AddArc($bgRect.Right - $diameter, $bgRect.Y, $diameter, $diameter, 270, 90)
        $bgPath.AddArc($bgRect.Right - $diameter, $bgRect.Bottom - $diameter, $diameter, $diameter, 0, 90)
        $bgPath.AddArc($bgRect.X, $bgRect.Bottom - $diameter, $diameter, $diameter, 90, 90)
        $bgPath.CloseFigure()

        $bgBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($bgRect, [System.Drawing.Color]::FromArgb(255, 25, 43, 66), [System.Drawing.Color]::FromArgb(255, 11, 17, 26), [System.Drawing.Drawing2D.LinearGradientMode]::Vertical)
        $graphics.FillPath($bgBrush, $bgPath)
        $bgBrush.Dispose()
        $bgPath.Dispose()

        $cPen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, 17 * $scale)
        $cPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Flat
        $cPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Flat
        $graphics.DrawArc($cPen, 27 * $scale, 27 * $scale, 67 * $scale, 69 * $scale, 42, 276)
        $cPen.Dispose()

        $barSpecs = @(
            @{ X = 66; Height = 20; Color = [System.Drawing.Color]::FromArgb(255, 255, 132, 18) },
            @{ X = 80; Height = 31; Color = [System.Drawing.Color]::FromArgb(255, 255, 160, 25) },
            @{ X = 94; Height = 43; Color = [System.Drawing.Color]::FromArgb(255, 255, 194, 48) }
        )
        $baseY = 91
        $barWidth = 10
        foreach ($bar in $barSpecs) {
            $rect = New-Object System.Drawing.RectangleF($bar.X * $scale, ($baseY - $bar.Height) * $scale, $barWidth * $scale, $bar.Height * $scale)
            $path = New-Object System.Drawing.Drawing2D.GraphicsPath
            $r = 3 * $scale
            $d = $r * 2
            $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
            $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
            $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
            $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
            $path.CloseFigure()
            $brush = New-Object System.Drawing.SolidBrush($bar.Color)
            $graphics.FillPath($brush, $path)
            $brush.Dispose()
            $path.Dispose()
        }
    }
    finally {
        $graphics.Dispose()
    }

    return $bitmap
}

function Convert-BitmapToPngBytes {
    param([System.Drawing.Bitmap]$Bitmap)
    $stream = New-Object System.IO.MemoryStream
    $Bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    return $stream.ToArray()
}

function Write-UInt16Le {
    param([System.IO.BinaryWriter]$Writer, [int]$Value)
    $Writer.Write([uint16]$Value)
}

function Write-UInt32Le {
    param([System.IO.BinaryWriter]$Writer, [int]$Value)
    $Writer.Write([uint32]$Value)
}

$sizes = @(256, 64, 48, 32, 16)
$entries = @()

foreach ($size in $sizes) {
    $bitmap = New-IconBitmap -Size $size
    try {
        $pngBytes = Convert-BitmapToPngBytes -Bitmap $bitmap
        $entries += [PSCustomObject]@{
            Size = $size
            Bytes = $pngBytes
        }
    }
    finally {
        $bitmap.Dispose()
    }
}

$outputDirectory = Split-Path -Parent $OutputPath
if ($outputDirectory) {
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
}

$stream = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($stream)
try {
    Write-UInt16Le $writer 0
    Write-UInt16Le $writer 1
    Write-UInt16Le $writer $entries.Count

    $offset = 6 + ($entries.Count * 16)
    foreach ($entry in $entries) {
        $sizeByte = if ($entry.Size -eq 256) { 0 } else { $entry.Size }
        $writer.Write([byte]$sizeByte)
        $writer.Write([byte]$sizeByte)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        Write-UInt16Le $writer 1
        Write-UInt16Le $writer 32
        Write-UInt32Le $writer $entry.Bytes.Length
        Write-UInt32Le $writer $offset
        $offset += $entry.Bytes.Length
    }

    foreach ($entry in $entries) {
        $writer.Write($entry.Bytes)
    }

    [System.IO.File]::WriteAllBytes($OutputPath, $stream.ToArray())
}
finally {
    $writer.Dispose()
    $stream.Dispose()
}
