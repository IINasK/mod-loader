$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$assets=Join-Path $PSScriptRoot 'Assets'
# These two files are generated together from the approved Estudo 02, not from a screenshot.
$expected=@{
 'ModLoader.svg'='E2ED35028B5B3104A1269CE234B04C052FBB131E24F8847C6342C11AE3CBE233'
 'ModLoader.ico'='9967FE9CE16BBA9AEC6ADED9E4B8E1E571C359C27DEAE35D8D0CB7288066F8F8'
}
foreach($name in $expected.Keys){
 if((Get-FileHash -LiteralPath (Join-Path $assets $name)).Hash -ne $expected[$name]){throw "Recurso da marca divergente: $name. Reexportar SVG e ICO juntos e revisar a identidade."}
}
$stream=[IO.File]::OpenRead((Join-Path $assets 'ModLoader.ico'))
$reader=New-Object IO.BinaryReader $stream
try{
 if($reader.ReadUInt16() -ne 0 -or $reader.ReadUInt16() -ne 1 -or $reader.ReadUInt16() -ne 7){throw 'ICO deve conter sete tamanhos'}
 $frames=@()
 foreach($size in @(16,24,32,48,64,128,256)){
  $w=$reader.ReadByte();$h=$reader.ReadByte();$null=$reader.ReadBytes(6)
  $length=$reader.ReadInt32();$offset=$reader.ReadInt32()
  $dimension=if($size -eq 256){0}else{$size}
  if($w -ne $dimension -or $h -ne $dimension){throw 'Dimensao ICO divergente'}
  $frames+=@{Size=$size;Length=$length;Offset=$offset}
 }
 foreach($frame in $frames){
  $stream.Position=$frame.Offset
  $png=New-Object IO.MemoryStream(,$reader.ReadBytes($frame.Length))
  $bitmap=New-Object Drawing.Bitmap $png
  try{if($bitmap.Width -ne $frame.Size -or $bitmap.Height -ne $frame.Size -or $bitmap.GetPixel(0,0).A -ne 0){throw 'Quadro ICO sem transparencia ou tamanho correto'}}finally{$bitmap.Dispose();$png.Dispose()}
 }
}finally{$reader.Dispose();$stream.Dispose()}
