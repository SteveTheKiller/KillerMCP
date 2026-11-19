param(
    [Parameter(Mandatory = $true)][string]$AssemblyPath,
    [Parameter(Mandatory = $true)][string]$ScratchPath,
    [ValidateSet('check','off','download','install')][string]$Expected = 'check',
    [string]$RenderPath
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path $AssemblyPath))
$app = [Windows.Application]::new()
$app.ShutdownMode = 'OnExplicitShutdown'
$env:KILLERMCP_TEST_RUNTIME = '1'
$destination = Join-Path $ScratchPath 'install'
$type = $assembly.GetType('KillerMCP.Setup.InstallerWizard', $true)
$constructor = $type.GetConstructor([Reflection.BindingFlags]'Instance,NonPublic', $null, [type[]]@([string], [bool]), $null)
$wizard = $constructor.Invoke([object[]]@([string]$destination, $true))
try {
    $box = $wizard.FindName('UpdateBehaviorBox')
    if ($null -eq $box) { throw 'Installer has no update behavior dropdown.' }
    $choices = @($box.Items | ForEach-Object { "$($_.Tag):$($_.Content)" })
    if (($choices -join '|') -cne 'off:Do not check|check:Check and ask|download:Download and ask|install:Install automatically') { throw 'Installer choices do not match the notifier.' }
    if ($box.SelectedItem.Tag -ne $Expected) { throw "Expected $Expected, got $($box.SelectedItem.Tag)." }
    if ([Windows.Automation.AutomationProperties]::GetName($box) -ne 'Update behavior') { throw 'Dropdown has no accessible name.' }
    $wizard.ShowInTaskbar = $false
    $wizard.WindowStartupLocation = 'Manual'
    $wizard.Left = -10000
    $wizard.Top = -10000
    # The missing-runtime notice and download link are the tallest state; the window must grow to fit them (reported at 125% on 2560x1440).
    $wizard.FindName('RuntimeStatus').Text = '.NET 10 runtime required before installation'
    $wizard.FindName('RuntimeStatus').Visibility = 'Visible'
    $wizard.FindName('RuntimeLink').Visibility = 'Visible'
    $wizard.Show()
    $wizard.UpdateLayout()
    $panel = $wizard.FindName('Status').Parent
    foreach ($name in 'UpdateBehaviorBox', 'Status', 'InstallButton') {
        $element = $wizard.FindName($name)
        $bottom = $element.TransformToAncestor($panel).Transform([Windows.Point]::new(0, $element.ActualHeight)).Y
        if ($bottom -gt $panel.ActualHeight + 0.5) { throw "$name is cut off: bottom $bottom, panel $($panel.ActualHeight)." }
    }
    # The dropdown sits in a star row, so a short window crops it under the status line instead of shrinking it.
    $status = $wizard.FindName('Status')
    $boxBottom = $box.TransformToAncestor($panel).Transform([Windows.Point]::new(0, $box.ActualHeight)).Y
    $statusTop = $status.TransformToAncestor($panel).Transform([Windows.Point]::new(0, 0)).Y - $status.Margin.Top
    if ($boxBottom -gt $statusTop + 0.5) { throw "Update behavior dropdown is cut off: bottom $boxBottom, row ends at $statusTop." }
    if ($RenderPath) {
        $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new([int]$wizard.ActualWidth, [int]$wizard.ActualHeight, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
        $bitmap.Render($wizard)
        $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
        $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
        $stream = [IO.File]::Create($RenderPath)
        try { $encoder.Save($stream) } finally { $stream.Dispose() }
        $box.IsDropDownOpen = $true
        $popup = $box.Template.FindName('PART_Popup', $box)
        $popup.Child.Measure([Windows.Size]::new(210, 220))
        $popup.Child.Arrange([Windows.Rect]::new(0, 0, 210, $popup.Child.DesiredSize.Height))
        $popup.Child.UpdateLayout()
        $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new(210, [int]$popup.Child.ActualHeight, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
        $bitmap.Render($popup.Child)
        $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
        $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
        $stream = [IO.File]::Create($RenderPath + '.dropdown.png')
        try { $encoder.Save($stream) } finally { $stream.Dispose() }
        $box.IsDropDownOpen = $false
    }
    Write-Output "PASS: installer offers four notifier choices, selects $Expected, exposes its accessible name, and fits the runtime notice without clipping ($([int]$wizard.ActualHeight) px tall)."
} finally { $wizard.Close(); $app.Shutdown() }
