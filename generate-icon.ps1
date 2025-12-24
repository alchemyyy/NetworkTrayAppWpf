# Generate Windows 11 ethernet glyph icon for the application
# Run this script once to create app.ico

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName PresentationCore

function Create-EthernetIcon {
    param([int]$size)

    $bitmap = New-Object System.Drawing.Bitmap($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $graphics.Clear([System.Drawing.Color]::Transparent)

    # Ethernet glyph character (E839)
    $glyph = [char]0xE839

    # Try Segoe Fluent Icons first (Windows 11), fall back to Segoe MDL2 Assets
    $fontFamily = $null
    try {
        $fontFamily = New-Object System.Drawing.FontFamily("Segoe Fluent Icons")
    } catch {
        try {
            $fontFamily = New-Object System.Drawing.FontFamily("Segoe MDL2 Assets")
        } catch {
            Write-Warning "Could not find Segoe Fluent Icons or Segoe MDL2 Assets font"
            return $bitmap
        }
    }

    # Use white color (for dark taskbar, which is more common)
    $brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)

    # Font size slightly smaller than icon to center nicely
    $fontSize = $size * 0.85
    $font = New-Object System.Drawing.Font($fontFamily, $fontSize, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)

    # Measure string to center it
    $stringFormat = New-Object System.Drawing.StringFormat
    $stringFormat.Alignment = [System.Drawing.StringAlignment]::Center
    $stringFormat.LineAlignment = [System.Drawing.StringAlignment]::Center

    $rect = New-Object System.Drawing.RectangleF(0, 0, $size, $size)
    $graphics.DrawString($glyph, $font, $brush, $rect, $stringFormat)

    $stringFormat.Dispose()
    $font.Dispose()
    $fontFamily.Dispose()
    $brush.Dispose()
    $graphics.Dispose()

    return $bitmap
}

# Create icons at multiple sizes
$sizes = @(16, 32, 48, 256)
$icons = @()

foreach ($size in $sizes) {
    $icons += Create-EthernetIcon -size $size
}

# Save as ICO using MemoryStream approach
$icoPath = Join-Path $PSScriptRoot "app.ico"

# ICO file format:
# Header: 6 bytes (reserved=0, type=1, count=N)
# Directory entries: 16 bytes each
# Image data: PNG or BMP data

$ms = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($ms)

# ICO Header
$writer.Write([uint16]0)           # Reserved
$writer.Write([uint16]1)           # Type (1 = ICO)
$writer.Write([uint16]$icons.Count) # Number of images

# We'll write directory entries after we know image offsets
$imageDataList = @()
foreach ($icon in $icons) {
    $imgMs = New-Object System.IO.MemoryStream
    $icon.Save($imgMs, [System.Drawing.Imaging.ImageFormat]::Png)
    $imageDataList += ,$imgMs.ToArray()
    $imgMs.Dispose()
}

# Calculate offsets
$headerSize = 6
$dirEntrySize = 16
$dataOffset = $headerSize + ($dirEntrySize * $icons.Count)

$currentOffset = $dataOffset
for ($i = 0; $i -lt $icons.Count; $i++) {
    $size = $sizes[$i]
    $imageData = $imageDataList[$i]

    # Directory entry
    $writer.Write([byte]$(if ($size -ge 256) { 0 } else { $size })) # Width (0 = 256)
    $writer.Write([byte]$(if ($size -ge 256) { 0 } else { $size })) # Height (0 = 256)
    $writer.Write([byte]0)           # Color palette
    $writer.Write([byte]0)           # Reserved
    $writer.Write([uint16]1)         # Color planes
    $writer.Write([uint16]32)        # Bits per pixel
    $writer.Write([uint32]$imageData.Length) # Image data size
    $writer.Write([uint32]$currentOffset)    # Offset to image data

    $currentOffset += $imageData.Length
}

# Write image data
foreach ($imageData in $imageDataList) {
    $writer.Write($imageData)
}

$writer.Flush()

# Save to file
[System.IO.File]::WriteAllBytes($icoPath, $ms.ToArray())

$writer.Dispose()
$ms.Dispose()

foreach ($icon in $icons) {
    $icon.Dispose()
}

Write-Host "Created app.ico with Ethernet glyph at sizes: $($sizes -join ', ')px"
