#Requires -Version 5.1
# Vector primitives, rendered at 4x for clean 16/32 px ribbon icons.
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'
$destination = Join-Path (Split-Path $PSScriptRoot -Parent) 'resources'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
foreach ($name in @('start', 'stop', 'status', 'config')) {
    foreach ($size in @(16, 32)) {
        $canvas = New-Object Drawing.Bitmap 128, 128
        $g = [Drawing.Graphics]::FromImage($canvas)
        $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.ScaleTransform(4, 4)
        $g.Clear([Drawing.Color]::Transparent)
        $baseColor = if ($name -eq 'config') { '#6B7280' } else { '#285A80' }
        $background = New-Object Drawing.SolidBrush ([Drawing.ColorTranslator]::FromHtml($baseColor))
        $white = New-Object Drawing.SolidBrush ([Drawing.Color]::White)
        $line = New-Object Drawing.Pen ([Drawing.Color]::White), 2
        $line.StartCap = $line.EndCap = [Drawing.Drawing2D.LineCap]::Round
        $g.FillEllipse($background, 1, 1, 30, 30)
        if ($name -eq 'config') {
            $g.TranslateTransform(16, 16)
            for ($i = 0; $i -lt 8; $i++) {
                $g.FillRectangle($white, -1.6, -11, 3.2, 5)
                $g.RotateTransform(45)
            }
            $g.FillEllipse($white, -8, -8, 16, 16)
            $g.FillEllipse($background, -4, -4, 8, 8)
        } else {
            # Two server shelves; identical base for all server operations.
            foreach ($y in @(7, 14)) {
                $g.DrawRectangle($line, 6, $y, 17, 5)
                $g.FillEllipse($white, 8, ($y + 1.5), 2, 2)
            }
            $g.DrawLine($line, 8, 23, 14, 23)
            $badgeColor = switch ($name) { 'start' { '#16854B' } 'stop' { '#CE3C3C' } 'status' { '#146CC0' } }
            $badge = New-Object Drawing.SolidBrush ([Drawing.ColorTranslator]::FromHtml($badgeColor))
            $g.FillEllipse($white, 15, 15, 17, 17)
            $g.FillEllipse($badge, 16, 16, 15, 15)
            switch ($name) {
                'start' {
                    $points = [Drawing.PointF[]]@([Drawing.PointF]::new(21, 19), [Drawing.PointF]::new(21, 28), [Drawing.PointF]::new(28, 23.5))
                    $g.FillPolygon($white, $points)
                }
                'stop' { $g.FillRectangle($white, 20, 20, 7, 7) }
                'status' {
                    $g.FillEllipse($white, 22, 18.5, 3, 3)
                    $g.FillRectangle($white, 22.25, 23, 2.5, 5)
                }
            }
            $badge.Dispose()
        }
        $g.Dispose()
        $output = New-Object Drawing.Bitmap $size, $size
        $render = [Drawing.Graphics]::FromImage($output)
        $render.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $render.DrawImage($canvas, 0, 0, $size, $size)
        $output.Save((Join-Path $destination "$name$size.png"), [Drawing.Imaging.ImageFormat]::Png)
        $render.Dispose(); $output.Dispose(); $canvas.Dispose()
        $line.Dispose(); $white.Dispose(); $background.Dispose()
    }
}
