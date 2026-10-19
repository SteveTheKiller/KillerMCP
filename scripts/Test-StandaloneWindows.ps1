param(
    [Parameter(Mandatory = $true)][string]$AssemblyPath,
    [Parameter(Mandatory = $true)][string]$ScratchPath,
    [ValidateSet('InstallerWizard','SetupDialog','UpdateWindow','RepairConfirmWindow')][string]$Kind
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path $AssemblyPath))
$app = [Windows.Application]::new()
$app.ShutdownMode = 'OnExplicitShutdown'
$env:KILLERMCP_TEST_RUNTIME = '1'
switch ($Kind) {
    'InstallerWizard' {
        $type = $assembly.GetType('KillerMCP.Setup.InstallerWizard', $true)
        $constructor = $type.GetConstructor([Reflection.BindingFlags]'Instance,NonPublic', $null, [type[]]@([string],[bool]), $null)
        $window = $constructor.Invoke(@([string](Join-Path $ScratchPath 'install'), $true))
        $targetName = 'InstallButton'
    }
    'SetupDialog' {
        $type = $assembly.GetType('KillerMCP.Setup.SetupDialog', $true)
        $constructor = $type.GetConstructor([Reflection.BindingFlags]'Instance,NonPublic', $null, [type[]]@([string],[string],[bool],[string]), $null)
        $window = $constructor.Invoke(@('Uninstall KillerMCP?', 'This removes KillerMCP and disconnects it from compatible agent clients on this computer.', $true, 'Uninstall'))
        $targetName = 'OkButton'
    }
    'UpdateWindow' {
        $type = $assembly.GetType('KillerMCP.Notify.UpdateWindow', $true)
        $window = $type.GetConstructor([type[]]@([string[]])).Invoke(@(,[string[]]@('--installed','0.6.0','--latest','0.6.1')))
        $targetName = 'UpdateButton'
    }
    'RepairConfirmWindow' {
        $type = $assembly.GetType('KillerMCP.Notify.RepairConfirmWindow', $true)
        $request = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes('{"Title":"Confirm repair","Effects":"Review the proposed repair before continuing.","Command":"example repair"}'))
        $window = $type.GetMethod('TryCreate').Invoke($null, @($request))
        $targetName = 'DenyButton'
    }
}
try {
    $window.ShowInTaskbar = $false
    $window.WindowStartupLocation = 'Manual'
    $window.Left = -10000
    $window.Top = -10000
    $window.Show()
    $window.UpdateLayout()
    $target = $window.FindName($targetName)
    $panel = $window.Content.Child
    if ($panel -isnot [Windows.Controls.Grid]) { throw 'Expected standalone window root grid.' }
    $ring = [Activator]::CreateInstance($assembly.GetType('KillerFamily.Accessibility.FocusRing', $true), $true)
    $point = $target.TranslatePoint([Windows.Point]::new(2,2), $panel)
    $ring.HorizontalAlignment = 'Left'
    $ring.VerticalAlignment = 'Top'
    $ring.Width = $target.ActualWidth - 4
    $ring.Height = $target.ActualHeight - 4
    $ring.Margin = [Windows.Thickness]::new($point.X,$point.Y,0,0)
    [Windows.Controls.Grid]::SetColumnSpan($ring, [Math]::Max(1, $panel.ColumnDefinitions.Count))
    [Windows.Controls.Grid]::SetRowSpan($ring, [Math]::Max(1, $panel.RowDefinitions.Count))
    [Windows.Controls.Panel]::SetZIndex($ring, 100)
    $panel.Children.Add($ring) | Out-Null
    $ring.RaiseEvent([Windows.RoutedEventArgs]::new([Windows.FrameworkElement]::LoadedEvent))
    $window.UpdateLayout()
    if ($ring.BorderBrush.ToString() -ne '#FFFF2BBD' -and -not [Windows.SystemParameters]::HighContrast) { throw "$Kind does not resolve its local focus accent: $($ring.BorderBrush)." }
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new([int]$window.ActualWidth, [int]$window.ActualHeight, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($window)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.File]::Create((Join-Path $ScratchPath "$Kind.png"))
    try { $encoder.Save($stream) } finally { $stream.Dispose() }
    Write-Output "PASS: $Kind resolves the local focus accent through the rendered window resource tree."
} finally { $window.Close(); $app.Shutdown() }
