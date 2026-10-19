param(
    [Parameter(Mandatory = $true)][string]$AssemblyPath,
    [ValidateSet('Accent', 'Fuchsia')][string]$AccentKey = 'Accent',
    [string]$RenderPath
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path $AssemblyPath))
$ring = [Activator]::CreateInstance($assembly.GetType('KillerFamily.Accessibility.FocusRing', $true), $true)
$ring.Resources[$AccentKey] = [Windows.Media.BrushConverter]::new().ConvertFromString('#ff2bbd')
$ring.Width = 100
$ring.Height = 28
$ring.RaiseEvent([Windows.RoutedEventArgs]::new([Windows.FrameworkElement]::LoadedEvent))
$ring.Measure([Windows.Size]::new(100, 28))
$ring.Arrange([Windows.Rect]::new(0, 0, 100, 28))
$bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new(100, 28, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
$bitmap.Render($ring)
$pixels = New-Object byte[] (100 * 28 * 4)
$bitmap.CopyPixels($pixels, 400, 0)
$visible = 0
for ($i = 0; $i -lt $pixels.Length; $i += 4) {
    if ($pixels[$i + 3] -gt 200 -and ($pixels[$i] -gt 100 -or $pixels[$i + 1] -gt 100 -or $pixels[$i + 2] -gt 100)) { $visible++ }
}
if ($RenderPath) {
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.File]::Create($RenderPath)
    try { $encoder.Save($stream) } finally { $stream.Dispose() }
}
if ($visible -lt 200) { throw "Keyboard outline is invisible on the standalone black surface ($visible visible pixels)." }
if ($ring.BorderThickness.Left -ne 2) { throw 'Focus outline geometry changed.' }
Write-Output "PASS: $AccentKey focus outline has $visible visible rendered pixels; thickness remains 2."
