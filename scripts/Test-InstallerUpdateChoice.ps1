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
    $wizard.Show()
    $wizard.Measure([Windows.Size]::new($wizard.Width, $wizard.Height))
    $wizard.Arrange([Windows.Rect]::new(0, 0, $wizard.Width, $wizard.Height))
    $wizard.UpdateLayout()
    if ($RenderPath) {
        $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new([int]$wizard.Width, [int]$wizard.Height, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
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
    Write-Output "PASS: installer offers four notifier choices, selects $Expected, and exposes its accessible name."
} finally { $wizard.Close(); $app.Shutdown() }
