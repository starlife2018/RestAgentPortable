param([string]$OutputName = "RestAgentPortable.exe")
$ErrorActionPreference = 'Stop'
$projectPath = $PSScriptRoot
$frameworkPath = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$compilerPath = Join-Path $frameworkPath 'csc.exe'
if (!(Test-Path -LiteralPath $compilerPath)) { throw '.NET Framework compiler not found.' }

Add-Type -AssemblyName System.Drawing
$iconPath = Join-Path $projectPath 'App.ico'
$iconStream = [IO.File]::Create($iconPath)
$writer = New-Object IO.BinaryWriter($iconStream)
$sizes = @(256, 128, 64, 48, 32, 24, 16)
$sourceImage = [Drawing.Image]::FromFile((Join-Path $projectPath 'App.png'))
$images = @()
foreach ($size in $sizes) {
    $bitmap = New-Object Drawing.Bitmap($size, $size)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.DrawImage($sourceImage, [Drawing.Rectangle]::new(0, 0, $size, $size))
    $memory = New-Object IO.MemoryStream
    $bitmap.Save($memory, [Drawing.Imaging.ImageFormat]::Png)
    $images += ,$memory.ToArray()
    $memory.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
$sourceImage.Dispose()
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($index = 0; $index -lt $sizes.Count; $index++) {
    $dimension = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
    $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$images[$index].Length); $writer.Write([uint32]$offset)
    $offset += $images[$index].Length
}
foreach ($bytes in $images) { $writer.Write([byte[]]$bytes) }
$writer.Dispose()

$references = @('System.dll', 'System.Core.dll', 'System.Net.Http.dll', 'System.Web.Extensions.dll', 'System.Xml.dll', 'WPF/WindowsBase.dll', 'WPF/PresentationCore.dll', 'WPF/PresentationFramework.dll', 'System.Xaml.dll')
$arguments = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/utf8output', ('/out:' + (Join-Path $projectPath $OutputName)), ('/win32icon:' + $iconPath), ('/resource:' + (Join-Path $projectPath 'Main.xaml') + ',Main.xaml'), ('/resource:' + $iconPath + ',App.ico'))
foreach ($reference in $references) { $arguments += '/reference:' + (Join-Path $frameworkPath $reference) }
$arguments += Join-Path $projectPath 'App.cs'
$arguments += Join-Path $projectPath 'Navigation.cs'
$arguments += Join-Path $projectPath 'UiIcons.cs'
$arguments += Join-Path $projectPath 'Themes.cs'
$arguments += Join-Path $projectPath 'ThemeSettings.cs'
$arguments += Join-Path $projectPath 'SelfTest.cs'
$arguments += Join-Path $projectPath 'SelfTest.Themes.cs'
$arguments += Join-Path $projectPath 'HeaderCatalog.cs'
$arguments += Join-Path $projectPath 'ParameterCatalog.cs'
$arguments += Join-Path $projectPath 'BodylessTransport.cs'
$arguments += Join-Path $projectPath 'CodeEditor.cs'
$editorPath = Join-Path $projectPath 'vendor/AvalonEdit/lib/net462/ICSharpCode.AvalonEdit.dll'
$arguments += '/reference:' + $editorPath
$arguments += '/resource:' + $editorPath + ',ICSharpCode.AvalonEdit.dll'
$arguments += '/resource:' + (Join-Path $projectPath 'Json.xshd') + ',Json.xshd'
$arguments += '/resource:' + (Join-Path $projectPath 'vendor/AvalonEdit-LICENSE.txt') + ',AvalonEdit-LICENSE.txt'
$arguments += '/resource:' + (Join-Path $projectPath 'HttpFieldRegistry.csv') + ',HttpFieldRegistry.csv'
& $compilerPath $arguments
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output ("Build complete: " + $OutputName)
