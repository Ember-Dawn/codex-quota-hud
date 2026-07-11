param(
    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

function New-RoundedRectPath {
    param(
        [Parameter(Mandatory = $true)]
        [System.Drawing.RectangleF]$Rect,
        [Parameter(Mandatory = $true)]
        [single]$Radius
    )

    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = [single]($Radius * 2.0)
    $path.AddArc($Rect.X, $Rect.Y, $diameter, $diameter, 180.0, 90.0)
    $path.AddArc([single]($Rect.Right - $diameter), $Rect.Y, $diameter, $diameter, 270.0, 90.0)
    $path.AddArc([single]($Rect.Right - $diameter), [single]($Rect.Bottom - $diameter), $diameter, $diameter, 0.0, 90.0)
    $path.AddArc($Rect.X, [single]($Rect.Bottom - $diameter), $diameter, $diameter, 90.0, 90.0)
    $path.CloseFigure()
    return $path
}

function New-IconBitmap {
    param([int]$Size)

    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

    try {
        $scale = [double]$Size / 128.0
        function S([double]$Value) {
            return [single]($Value * $scale)
        }

        $bgRect = [System.Drawing.RectangleF]::new((S 8), (S 8), (S 112), (S 112))
        $bgPath = New-RoundedRectPath -Rect $bgRect -Radius (S 22)
        try {
            $bgBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
                $bgRect,
                [System.Drawing.Color]::FromArgb(255, 25, 43, 66),
                [System.Drawing.Color]::FromArgb(255, 11, 17, 26),
                [System.Drawing.Drawing2D.LinearGradientMode]::Vertical)
            try {
                $graphics.FillPath($bgBrush, $bgPath)
            }
            finally {
                $bgBrush.Dispose()
            }
        }
        finally {
            $bgPath.Dispose()
        }

        $cPen = [System.Drawing.Pen]::new([System.Drawing.Color]::White, (S 17))
        try {
            $cPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Flat
            $cPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Flat
            $graphics.DrawArc($cPen, (S 27), (S 27), (S 67), (S 69), 42.0, 276.0)
        }
        finally {
            $cPen.Dispose()
        }

        $barSpecs = @(
            [PSCustomObject]@{ X = 66.0; Height = 20.0; Color = [System.Drawing.Color]::FromArgb(255, 255, 132, 18) },
            [PSCustomObject]@{ X = 80.0; Height = 31.0; Color = [System.Drawing.Color]::FromArgb(255, 255, 160, 25) },
            [PSCustomObject]@{ X = 94.0; Height = 43.0; Color = [System.Drawing.Color]::FromArgb(255, 255, 194, 48) }
        )
        $baseY = 91.0
        $barWidth = 10.0
        foreach ($bar in $barSpecs) {
            $rect = [System.Drawing.RectangleF]::new(
                (S $bar.X),
                (S ($baseY - $bar.Height)),
                (S $barWidth),
                (S $bar.Height))
            $path = New-RoundedRectPath -Rect $rect -Radius (S 3)
            try {
                $brush = [System.Drawing.SolidBrush]::new($bar.Color)
                try {
                    $graphics.FillPath($brush, $path)
                }
                finally {
                    $brush.Dispose()
                }
            }
            finally {
                $path.Dispose()
            }
        }
    }
    finally {
        $graphics.Dispose()
    }

    return $bitmap
}

function Convert-BitmapToPngBytes {
    param([System.Drawing.Bitmap]$Bitmap)
    $stream = [System.IO.MemoryStream]::new()
    try {
        $Bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        return $stream.ToArray()
    }
    finally {
        $stream.Dispose()
    }
}

function Write-UInt16Le {
    param([System.IO.BinaryWriter]$Writer, [int]$Value)
    $Writer.Write([uint16]$Value)
}

function Write-UInt32Le {
    param([System.IO.BinaryWriter]$Writer, [int64]$Value)
    $Writer.Write([uint32]$Value)
}

$sizes = @(256, 64, 48, 32, 16)
$entries = New-Object System.Collections.Generic.List[object]

foreach ($size in $sizes) {
    $bitmap = New-IconBitmap -Size $size
    try {
        $pngBytes = Convert-BitmapToPngBytes -Bitmap $bitmap
        $entries.Add([PSCustomObject]@{
            Size = [int]$size
            Bytes = $pngBytes
        })
    }
    finally {
        $bitmap.Dispose()
    }
}

$outputDirectory = Split-Path -Parent $OutputPath
if ($outputDirectory) {
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
}

$stream = [System.IO.MemoryStream]::new()
$writer = [System.IO.BinaryWriter]::new($stream)
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
