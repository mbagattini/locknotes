# Genera l'icona di LockNotes (anteprime o .ico finale multi-risoluzione).
# Le dimensioni grandi (256/48/32) usano il disegno dettagliato, quelle piccole
# (24/20/16, tray) una variante semplificata.
#
# Uso:
#   icon_gen.ps1 -Mode preview            -> icon_preview_A/B/C.png nella cartella corrente
#   icon_gen.ps1 -Mode final -Design B    -> sovrascrive LockNotes\Assets\locknotes.ico

param(
    [ValidateSet('preview','final')] [string]$Mode = 'preview',
    [ValidateSet('A','B','C')] [string]$Design = 'A'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

function C([string]$hex) { [System.Drawing.ColorTranslator]::FromHtml($hex) }

function New-RoundedRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [float](2 * $r)
    $p.AddArc([float]$x, [float]$y, $d, $d, 180, 90)
    $p.AddArc([float]($x + $w - $d), [float]$y, $d, $d, 270, 90)
    $p.AddArc([float]($x + $w - $d), [float]($y + $h - $d), $d, $d, 0, 90)
    $p.AddArc([float]$x, [float]($y + $h - $d), $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

# Lucchetto: corpo pieno, archetto a tratto, buco serratura.
# Con $outlineColor disegna un bordo intorno a corpo e archetto (per contrasto
# su sfondi chiari).
function Draw-Lock($g, [float]$x, [float]$y, [float]$w, [float]$h, $bodyColor, $holeColor, [bool]$simple, $outlineColor = $null) {
    $bodyY = [float]($y + 0.42 * $h)
    $bodyH = [float](0.58 * $h)
    $pw = if ($simple) { [float](0.16 * $h) } else { [float](0.11 * $h) }
    $ow = [float](0.055 * $h)
    $aw = [float](0.54 * $w)
    $ax = [float]($x + ($w - $aw) / 2)
    $ay = [float]($y + $pw / 2)
    $ah = [float](0.62 * $h)
    $midY = [float]($ay + $ah / 2)
    $intoBody = [float]($bodyY + 0.1 * $bodyH)

    if ($outlineColor) {
        $openPen = New-Object System.Drawing.Pen($outlineColor, [float]($pw + 2 * $ow))
        $g.DrawArc($openPen, $ax, $ay, $aw, $ah, [float]180, [float]180)
        $g.DrawLine($openPen, $ax, $midY, $ax, $intoBody)
        $g.DrawLine($openPen, [float]($ax + $aw), $midY, [float]($ax + $aw), $intoBody)
    }
    $pen = New-Object System.Drawing.Pen($bodyColor, $pw)
    $g.DrawArc($pen, $ax, $ay, $aw, $ah, [float]180, [float]180)
    $g.DrawLine($pen, $ax, $midY, $ax, $intoBody)
    $g.DrawLine($pen, [float]($ax + $aw), $midY, [float]($ax + $aw), $intoBody)

    $bodyPath = New-RoundedRect $x $bodyY $w $bodyH ([float](0.14 * $h))
    if ($outlineColor) {
        $bodyPen = New-Object System.Drawing.Pen($outlineColor, [float](2 * $ow))
        $g.DrawPath($bodyPen, $bodyPath)
    }
    $brush = New-Object System.Drawing.SolidBrush $bodyColor
    $g.FillPath($brush, $bodyPath)

    $cx = [float]($x + $w / 2)
    $kr = if ($simple) { [float](0.11 * $h) } else { [float](0.09 * $h) }
    $kcy = [float]($bodyY + 0.38 * $bodyH)
    $hb = New-Object System.Drawing.SolidBrush $holeColor
    $g.FillEllipse($hb, [float]($cx - $kr), [float]($kcy - $kr), [float](2 * $kr), [float](2 * $kr))
    if (-not $simple) {
        $sw = [float](0.6 * $kr)
        $g.FillRectangle($hb, [float]($cx - $sw), $kcy, [float](2 * $sw), [float](0.40 * $bodyH))
    }
}

# Opzione A: blocco note con spirale (stile notepad.exe) + lucchetto ambra bordato
function Draw-DesignA($g, [bool]$simple) {
    if ($simple) {
        $bg = New-RoundedRect 8 8 240 240 56
        $g.FillPath((New-Object System.Drawing.SolidBrush (C '#2E3440')), $bg)
        Draw-Lock $g 58 36 140 184 (C '#EBCB8B') (C '#2E3440') $true
        return
    }
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.Point(0, 0)), (New-Object System.Drawing.Point(0, 256)),
        (C '#46526A'), (C '#272C38'))
    $bg = New-RoundedRect 8 8 240 240 48
    $g.FillPath($grad, $bg)

    # blocco note: pagina bianca con banda blu in alto e anelli a spirale
    $pad = New-RoundedRect 52 52 128 168 10
    $g.FillPath((New-Object System.Drawing.SolidBrush (C '#ECEFF4')), $pad)
    $band = New-RoundedRect 52 52 128 40 10
    $g.FillPath((New-Object System.Drawing.SolidBrush (C '#5E81AC')), $band)
    $g.FillRectangle((New-Object System.Drawing.SolidBrush (C '#5E81AC')),
        [float]52, [float]72, [float]128, [float]14) # base piatta della banda

    # anelli della spirale che attraversano il bordo superiore
    $ringPen = New-Object System.Drawing.Pen((C '#C7CEDA'), [float]7)
    $ringPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $ringPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    foreach ($rx in 66, 83, 100, 117, 134, 151, 168) {
        $g.DrawLine($ringPen, [float]$rx, [float]40, [float]$rx, [float]64)
    }

    # righe di testo
    $lineBrush = New-Object System.Drawing.SolidBrush (C '#A8B2C3')
    foreach ($ln in @(@(70, 108, 86), @(70, 132, 86), @(70, 156, 62))) {
        $lp = New-RoundedRect ([float]$ln[0]) ([float]$ln[1]) ([float]$ln[2]) 11 5
        $g.FillPath($lineBrush, $lp)
    }

    # lucchetto ambra con bordo scuro per staccare dalla pagina chiara
    Draw-Lock $g 122 114 84 102 (C '#EBCB8B') (C '#2E3440') $false (C '#2E3440')
}

# Opzione B: lucchetto bianco su quadrato blu, flat
function Draw-DesignB($g, [bool]$simple) {
    if ($simple) {
        $bg = New-RoundedRect 8 8 240 240 56
        $g.FillPath((New-Object System.Drawing.SolidBrush (C '#5E81AC')), $bg)
        Draw-Lock $g 58 36 140 184 (C '#ECEFF4') (C '#5E81AC') $true
        return
    }
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.Point(0, 0)), (New-Object System.Drawing.Point(0, 256)),
        (C '#6A8DB8'), (C '#4F719B'))
    $bg = New-RoundedRect 8 8 240 240 48
    $g.FillPath($grad, $bg)
    Draw-Lock $g 72 46 112 152 (C '#ECEFF4') (C '#5E81AC') $false
}

function New-ShieldPath {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddLine([float]48, [float]40, [float]208, [float]40)
    $p.AddLine([float]208, [float]40, [float]208, [float]124)
    $p.AddBezier([float]208, [float]124, [float]208, [float]180, [float]164, [float]212, [float]128, [float]232)
    $p.AddBezier([float]128, [float]232, [float]92, [float]212, [float]48, [float]180, [float]48, [float]124)
    $p.CloseFigure()
    return $p
}

# Opzione C: scudo con serratura
function Draw-DesignC($g, [bool]$simple) {
    $shield = New-ShieldPath
    if ($simple) {
        $g.FillPath((New-Object System.Drawing.SolidBrush (C '#88C0D0')), $shield)
        $hb = New-Object System.Drawing.SolidBrush (C '#23303A')
        $g.FillEllipse($hb, [float]100, [float]80, [float]56, [float]56)
        $g.FillPolygon($hb, [System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF(118, 124)),
            (New-Object System.Drawing.PointF(138, 124)),
            (New-Object System.Drawing.PointF(150, 184)),
            (New-Object System.Drawing.PointF(106, 184))))
        return
    }
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.Point(0, 24)), (New-Object System.Drawing.Point(0, 236)),
        (C '#8FCAD9'), (C '#4E7B97'))
    $g.FillPath($grad, $shield)
    $pen = New-Object System.Drawing.Pen((C '#DEEAF2'), [float]6)
    $g.DrawPath($pen, $shield)
    $hb = New-Object System.Drawing.SolidBrush (C '#ECEFF4')
    $g.FillEllipse($hb, [float]106, [float]86, [float]44, [float]44)
    $g.FillPolygon($hb, [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF(120, 122)),
        (New-Object System.Drawing.PointF(136, 122)),
        (New-Object System.Drawing.PointF(146, 176)),
        (New-Object System.Drawing.PointF(110, 176))))
}

# Disegna a 256 e riscala alla dimensione richiesta
function Render-Design([string]$design, [int]$size, [bool]$simple) {
    $big = New-Object System.Drawing.Bitmap 256, 256
    $g = [System.Drawing.Graphics]::FromImage($big)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    switch ($design) {
        'A' { Draw-DesignA $g $simple }
        'B' { Draw-DesignB $g $simple }
        'C' { Draw-DesignC $g $simple }
    }
    $g.Dispose()
    if ($size -eq 256) { return $big }
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g2 = [System.Drawing.Graphics]::FromImage($bmp)
    $g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g2.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g2.DrawImage($big, (New-Object System.Drawing.Rectangle(0, 0, $size, $size)),
        0, 0, 256, 256, [System.Drawing.GraphicsUnit]::Pixel)
    $g2.Dispose()
    $big.Dispose()
    return $bmp
}

function Get-PngBytes($bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return , $ms.ToArray() # virgola: evita lo srotolamento dell'array

}

# Entry ICO classica (BITMAPINFOHEADER + BGRA bottom-up + AND mask vuota)
function Get-BmpIconBytes($bmp) {
    $s = $bmp.Width
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter $ms
    $bw.Write([int]40); $bw.Write([int]$s); $bw.Write([int]($s * 2))
    $bw.Write([int16]1); $bw.Write([int16]32)
    $bw.Write([int]0); $bw.Write([int]0)
    $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0)
    $rect = New-Object System.Drawing.Rectangle(0, 0, $s, $s)
    $data = $bmp.LockBits($rect,
        [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $row = New-Object byte[] ($s * 4)
    for ($y = $s - 1; $y -ge 0; $y--) {
        [System.Runtime.InteropServices.Marshal]::Copy(
            [IntPtr]($data.Scan0.ToInt64() + $y * $data.Stride), $row, 0, $s * 4)
        $bw.Write($row)
    }
    $bmp.UnlockBits($data)
    $maskStride = [int][math]::Ceiling($s / 32.0) * 4
    $bw.Write((New-Object byte[] ($maskStride * $s)))
    $bw.Flush()
    return , $ms.ToArray() # virgola: evita lo srotolamento dell'array
}

function Write-Ico($entries, $path) {
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter $ms
    $bw.Write([int16]0); $bw.Write([int16]1); $bw.Write([int16]$entries.Count)
    $offset = 6 + 16 * $entries.Count
    foreach ($e in $entries) {
        $sz = if ($e.Size -ge 256) { 0 } else { $e.Size }
        $bw.Write([byte]$sz); $bw.Write([byte]$sz)
        $bw.Write([byte]0); $bw.Write([byte]0)
        $bw.Write([int16]1); $bw.Write([int16]32)
        $bw.Write([int]$e.Bytes.Length); $bw.Write([int]$offset)
        $offset += $e.Bytes.Length
    }
    foreach ($e in $entries) { $bw.Write([byte[]]$e.Bytes) }
    $bw.Flush()
    [System.IO.File]::WriteAllBytes($path, $ms.ToArray())
}

if ($Mode -eq 'preview') {
    foreach ($d in 'A', 'B', 'C') {
        $cv = New-Object System.Drawing.Bitmap 640, 330
        $g = [System.Drawing.Graphics]::FromImage($cv)
        $g.Clear((C '#1C1C1C'))
        $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias
        $font = New-Object System.Drawing.Font('Segoe UI', 11)
        $tb = New-Object System.Drawing.SolidBrush (C '#BBBBBB')

        $detailed = Render-Design $d 256 $false
        $g.DrawImage($detailed, 24, 28, 256, 256)
        $g.DrawString('grande (finestra, desktop)', $font, $tb, 24, 292)

        $s16 = Render-Design $d 16 $true
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
        $g.DrawImage($s16, (New-Object System.Drawing.Rectangle(330, 60, 128, 128)),
            0, 0, 16, 16, [System.Drawing.GraphicsUnit]::Pixel)
        $g.DrawString('tray 16 px (zoom 8x)', $font, $tb, 330, 196)

        $g.DrawString('dimensioni reali (32 / 24 / 16):', $font, $tb, 330, 240)
        $s32 = Render-Design $d 32 $false
        $s24 = Render-Design $d 24 $true
        $g.DrawImage($s32, (New-Object System.Drawing.Rectangle(330, 268, 32, 32)),
            0, 0, 32, 32, [System.Drawing.GraphicsUnit]::Pixel)
        $g.DrawImage($s24, (New-Object System.Drawing.Rectangle(372, 272, 24, 24)),
            0, 0, 24, 24, [System.Drawing.GraphicsUnit]::Pixel)
        $g.DrawImage($s16, (New-Object System.Drawing.Rectangle(406, 276, 16, 16)),
            0, 0, 16, 16, [System.Drawing.GraphicsUnit]::Pixel)

        $g.Dispose()
        $cv.Save("$root\icon_preview_$d.png")
        $cv.Dispose(); $detailed.Dispose(); $s16.Dispose(); $s32.Dispose(); $s24.Dispose()
        Write-Output "icon_preview_$d.png"
    }
}
else {
    $entries = @()
    $b256 = Render-Design $Design 256 $false
    $entries += @{ Size = 256; Bytes = (Get-PngBytes $b256) }
    $b256.Dispose()
    foreach ($s in 48, 32) {
        $b = Render-Design $Design $s $false
        $entries += @{ Size = $s; Bytes = (Get-BmpIconBytes $b) }
        $b.Dispose()
    }
    foreach ($s in 24, 20, 16) {
        $b = Render-Design $Design $s $true
        $entries += @{ Size = $s; Bytes = (Get-BmpIconBytes $b) }
        $b.Dispose()
    }
    $out = Join-Path $root 'LockNotes\Assets\locknotes.ico'
    Write-Ico $entries $out
    Write-Output "scritto $out ($($entries.Count) dimensioni)"
}
