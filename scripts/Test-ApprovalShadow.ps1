param(
    [Parameter(Mandatory = $true)][string]$AssemblyPath,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [string]$LabMachine = ''
)
$ErrorActionPreference = 'Stop'
if ($env:COMPUTERNAME -eq 'SHREDDER' -or ($env:CI -ne 'true' -and (!$LabMachine -or $LabMachine -ine $env:COMPUTERNAME))) { throw 'Run this rendered UI check in the explicitly selected Windows lab or CI.' }
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.Drawing
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath))
$app = [Activator]::CreateInstance($assembly.GetType('KillerMCP.Notify.App', $true))
$app.InitializeComponent()
$app.ShutdownMode = 'OnExplicitShutdown'
$request = @{ Action = 'fixture'; Title = 'Fixture repair'; Tier = 'Safe'; Effects = 'No changes are made by this visual fixture.'; Target = 'fixture'; Command = 'fixture preview' } | ConvertTo-Json -Compress
$encoded = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($request))
$window = $assembly.GetType('KillerMCP.Notify.RepairConfirmWindow', $true).GetMethod('TryCreate').Invoke($null, @($encoded))
if (-not $window) { throw 'Repair confirmation fixture was refused.' }
$null = New-Item -ItemType Directory -Path $OutputDirectory -Force
function Render-Window([string]$name) {
    $window.UpdateLayout()
    $bitmap = New-Object Windows.Media.Imaging.RenderTargetBitmap ([int]$window.ActualWidth), ([int]$window.ActualHeight), 96, 96, ([Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($window)
    $encoder = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.File]::Create((Join-Path $OutputDirectory $name))
    try { $encoder.Save($stream) } finally { $stream.Dispose() }
    return $bitmap
}
try {
    $window.Show()
    $window.Dispatcher.Invoke([Action]{}, [Windows.Threading.DispatcherPriority]::ApplicationIdle)
    $root = [Windows.Controls.Border]$window.Content
    $bitmap = Render-Window 'approval.png'
    $width = $bitmap.PixelWidth
    $height = $bitmap.PixelHeight
    $pixels = New-Object byte[] ($width * $height * 4)
    $bitmap.CopyPixels($pixels, $width * 4, 0)
    $edgeAlpha = 0
    for ($y = 0; $y -lt $height; $y++) {
        for ($x = 0; $x -lt $width; $x++) {
            if ($x -eq 0 -or $y -eq 0 -or $x -eq ($width - 1) -or $y -eq ($height - 1)) {
                $edgeAlpha = [Math]::Max($edgeAlpha, $pixels[($y * $width + $x) * 4 + 3])
            }
        }
    }
    $shadowIndex = (([int]($height / 2) * $width) + [int]($root.Margin.Left - 2)) * 4 + 3
    $shadowAlpha = $pixels[$shadowIndex]
    $effect = $root.Effect
    $root.Effect = $null
    $absent = Render-Window 'without-shadow.png'
    $absentPixels = New-Object byte[] ($width * $height * 4)
    $absent.CopyPixels($absentPixels, $width * 4, 0)
    $root.Effect = $effect
    $screen = New-Object Drawing.Bitmap $width, $height
    $graphics = [Drawing.Graphics]::FromImage($screen)
    try {
        $origin = $window.PointToScreen((New-Object Windows.Point 0, 0))
        $graphics.CopyFromScreen([int]$origin.X, [int]$origin.Y, 0, 0, $screen.Size)
        $screen.Save((Join-Path $OutputDirectory 'owned-window.png'))
    } finally { $graphics.Dispose(); $screen.Dispose() }
    $result = [ordered]@{
        AssemblySha256 = (Get-FileHash -LiteralPath $AssemblyPath -Algorithm SHA256).Hash
        WindowWidth = $width; WindowHeight = $height; CardWidth = $root.ActualWidth
        MaxBoundaryAlpha = $edgeAlpha; PresentShadowAlpha = $shadowAlpha
        AbsentShadowAlpha = $absentPixels[$shadowIndex]
        Passed = ($edgeAlpha -eq 0 -and $shadowAlpha -gt 0 -and $absentPixels[$shadowIndex] -eq 0 -and $root.ActualWidth -eq 634)
    }
    $result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'result.json') -Encoding UTF8
    $result | ConvertTo-Json
    if (-not $result.Passed) { throw 'Approval shadow is clipped, missing, or changed the visible card width.' }
} finally { $window.Close(); $app.Shutdown() }
