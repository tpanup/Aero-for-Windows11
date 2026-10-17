Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Drawing.Drawing2D

$projectDir = "C:\Users\User\Desktop\ATW11"
$icoPath = Join-Path $projectDir "app.ico"

function New-RoundRectPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-TopRoundRectPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddLine($x + $w, $y + $h, $x, $y + $h)
    $p.CloseFigure()
    return $p
}

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::Transparent)

    $m = [float]($size * 0.07)
    $w = [float]($size - $m * 2)
    $h = [float]($size - $m * 2)
    $r = [float]($size * 0.13)
    $barH = [float]($h * 0.30)

    # 外壳（深一档的边）
    $outer = New-RoundRectPath $m $m $w $h $r
    $edgeBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(180, 40, 62, 90))
    $g.FillPath($edgeBrush, $outer)

    # 玻璃主体
    $bodyRect = New-Object System.Drawing.RectangleF ($m + 1), ($m + 1), ($w - 2), ($h - 2)
    $body = New-RoundRectPath ($m + 1) ($m + 1) ($w - 2) ($h - 2) ($r - 1)
    $bodyBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush ($bodyRect), `
        ([System.Drawing.Color]::FromArgb(255, 150, 176, 210)), `
        ([System.Drawing.Color]::FromArgb(255, 104, 134, 172)), 90.0
    $g.FillPath($bodyBrush, $body)

    # 标题栏（更亮）
    $barRect = New-Object System.Drawing.RectangleF ($m + 1), ($m + 1), ($w - 2), $barH
    $bar = New-TopRoundRectPath ($m + 1) ($m + 1) ($w - 2) $barH ($r - 1)
    $barBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush ($barRect), `
        ([System.Drawing.Color]::FromArgb(255, 226, 239, 252)), `
        ([System.Drawing.Color]::FromArgb(255, 168, 194, 226)), 90.0
    $g.FillPath($barBrush, $bar)

    # 客户区（浅白玻璃）
    $cx = [float]($m + 1 + $size * 0.06)
    $cy = [float]($m + 1 + $barH)
    $cw = [float]($w - 2 - $size * 0.12)
    $ch = [float]($h - 2 - $barH - $size * 0.06)
    if ($cw -gt 1 -and $ch -gt 1) {
        $client = New-RoundRectPath $cx $cy $cw $ch ($size * 0.035)
        $clientBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush `
            (New-Object System.Drawing.RectangleF $cx, $cy, $cw, $ch), `
            ([System.Drawing.Color]::FromArgb(255, 250, 253, 255)), `
            ([System.Drawing.Color]::FromArgb(255, 226, 238, 250)), 90.0
        $g.FillPath($clientBrush, $client)

        # 客户区内侧 1px 白高光
        $penW = [math]::Max(1.0, $size / 48.0)
        $hiPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(230, 255, 255, 255)), $penW
        $g.DrawPath($hiPen, $client)
        $hiPen.Dispose()
        $client.Dispose()
        $clientBrush.Dispose()
    }

    # 顶部 1px 反光
    $topPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(200, 255, 255, 255)), ([math]::Max(1.0, $size / 64.0))
    $inset = [float]($m + 2)
    $g.DrawLine($topPen, $inset + $r * 0.5, $inset, ($m + $w) - 2 - $r * 0.5, $inset)
    $topPen.Dispose()

    # 三个窗口按钮（大尺寸才画得清）
    if ($size -ge 32) {
        $btnD = [float]($barH * 0.34)
        $gap = [float]($btnD * 0.65)
        $bx = [float]($m + $w - 3 - $btnD * 3 - $gap * 2)
        $by = [float]($m + 1 + ($barH - $btnD) / 2)
        $btnBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(235, 255, 255, 255))
        for ($i = 0; $i -lt 3; $i++) {
            $x = $bx + $i * ($btnD + $gap)
            if ($i -eq 2) {
                # 关闭按钮：红
                $btnBrush.Color = [System.Drawing.Color]::FromArgb(235, 226, 96, 84)
            }
            $g.FillEllipse($btnBrush, $x, $by, $btnD, $btnD)
        }
        $btnBrush.Dispose()
    }

    $edgeBrush.Dispose()
    $bodyBrush.Dispose()
    $barBrush.Dispose()
    $body.Dispose()
    $bar.Dispose()
    $outer.Dispose()
    $g.Dispose()
    return $bmp
}

# 生成各尺寸 PNG 字节
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$entries = @()
foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $entries += [pscustomobject]@{ Size = $s; Data = $ms.ToArray() }
    $ms.Dispose()
    $bmp.Dispose()
}

# 组装 ICO
$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $out
$bw.Write([uint16]0)                       # reserved
$bw.Write([uint16]1)                       # type = icon
$bw.Write([uint16]$entries.Count)
$offset = 6 + 16 * $entries.Count
foreach ($e in $entries) {
    $dim = $e.Size
    if ($dim -ge 256) { $dim = 0 }
    $bw.Write([byte]$dim)
    $bw.Write([byte]$dim)
    $bw.Write([byte]0)                     # colorCount
    $bw.Write([byte]0)                     # reserved
    $bw.Write([uint16]1)                   # planes
    $bw.Write([uint16]32)                  # bitCount
    $bw.Write([uint32]$e.Data.Length)
    $bw.Write([uint32]$offset)
    $offset += $e.Data.Length
}
foreach ($e in $entries) { $bw.Write($e.Data) }
$bw.Flush()
[System.IO.File]::WriteAllBytes($icoPath, $out.ToArray())
$bw.Dispose(); $out.Dispose()

Write-Output ("图标已生成：" + $icoPath + "  " + [math]::Round((Get-Item $icoPath).Length/1KB,1) + " KB  含 " + $entries.Count + " 个尺寸")
