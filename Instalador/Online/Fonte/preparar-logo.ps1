$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$assets=Join-Path $PSScriptRoot 'Assets'
$source=Join-Path $assets 'ModLoader.svg'
$destination=Join-Path $assets 'ModLoader.ico'
$preview=Join-Path (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))) 'logo-preview.png'
$settings=New-Object System.Xml.XmlReaderSettings
$settings.DtdProcessing=[System.Xml.DtdProcessing]::Prohibit
$settings.XmlResolver=$null
$reader=[System.Xml.XmlReader]::Create($source,$settings)
try{$svg=New-Object System.Xml.XmlDocument;$svg.XmlResolver=$null;$svg.Load($reader)}finally{$reader.Dispose()}
$culture=[Globalization.CultureInfo]::InvariantCulture
$view=@($svg.DocumentElement.GetAttribute('viewBox').Split(' ')|ForEach-Object {[single]::Parse($_,$culture)})
if($view.Count -ne 4 -or $view[0] -ne 0 -or $view[1] -ne 0 -or $view[2] -le 0 -or $view[3] -le 0){throw 'viewBox da logo inválido'}
$background=$svg.DocumentElement.SelectSingleNode('*[local-name()="rect"]')
if($null -eq $background){throw 'Fundo da logo ausente'}
$backgroundColor=[Drawing.ColorTranslator]::FromHtml($background.GetAttribute('fill'))
$shapes=@($svg.DocumentElement.SelectNodes('*[local-name()="polygon"]')|ForEach-Object {
    $points=@($_.GetAttribute('points').Split(' ')|Where-Object {$_}|ForEach-Object {
        $xy=$_.Split(',');if($xy.Count -ne 2){throw 'Ponto vetorial inválido'}
        New-Object Drawing.PointF([single]::Parse($xy[0],$culture)),([single]::Parse($xy[1],$culture))
    })
    if($points.Count -lt 3){throw 'Polígono vetorial incompleto'}
    [pscustomobject]@{Color=[Drawing.ColorTranslator]::FromHtml($_.GetAttribute('fill'));Points=[Drawing.PointF[]]$points}
})
function Render-Logo([int]$size){
    $bitmap=New-Object Drawing.Bitmap -ArgumentList @($size,$size,([Drawing.Imaging.PixelFormat]::Format32bppArgb))
    $graphics=[Drawing.Graphics]::FromImage($bitmap)
    try{
        $graphics.Clear($backgroundColor)
        $graphics.SmoothingMode=[Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.PixelOffsetMode=[Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $scale=[single][Math]::Min($size/$view[2],$size/$view[3])
        $matrix=New-Object Drawing.Drawing2D.Matrix $scale,0,0,$scale,([single](($size-$view[2]*$scale)/2)),([single](($size-$view[3]*$scale)/2))
        try{$graphics.Transform=$matrix}finally{$matrix.Dispose()}
        foreach($shape in $shapes){$brush=New-Object Drawing.SolidBrush $shape.Color;try{$graphics.FillPolygon($brush,$shape.Points)}finally{$brush.Dispose()}}
    }finally{$graphics.Dispose()}
    return ,$bitmap
}
$frames=@()
foreach($size in @(16,24,32,48,64,128,256)){
    $bitmap=Render-Logo $size;$stream=New-Object IO.MemoryStream
    try{$bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png);$frames+=[pscustomobject]@{Size=$size;Bytes=$stream.ToArray()}}finally{$stream.Dispose();$bitmap.Dispose()}
}
$output=New-Object IO.MemoryStream;$writer=New-Object IO.BinaryWriter $output
try{
    $writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$frames.Count)
    $offset=[uint32](6+16*$frames.Count)
    foreach($frame in $frames){
        $dimension=if($frame.Size -eq 256){[byte]0}else{[byte]$frame.Size}
        $writer.Write($dimension);$writer.Write($dimension);$writer.Write([byte]0);$writer.Write([byte]0)
        $writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$frame.Bytes.Length);$writer.Write($offset)
        $offset+=[uint32]$frame.Bytes.Length
    }
    foreach($frame in $frames){$writer.Write([byte[]]$frame.Bytes)}
    $writer.Flush();[IO.File]::WriteAllBytes($destination,$output.ToArray())
}finally{$writer.Dispose();$output.Dispose()}
$bitmap=Render-Logo 640
try{$bitmap.Save($preview,[Drawing.Imaging.ImageFormat]::Png)}finally{$bitmap.Dispose()}
Get-Item -LiteralPath $source,$destination,$preview|Select-Object FullName,Length
