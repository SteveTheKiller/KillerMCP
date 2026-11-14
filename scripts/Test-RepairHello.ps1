param([Parameter(Mandatory = $true)][string]$AssemblyPath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath))
$appType = $assembly.GetType('KillerMCP.Notify.App', $true)
$app = [Activator]::CreateInstance($appType)
$app.InitializeComponent()
$app.ShutdownMode = 'OnExplicitShutdown'
$windowType = $assembly.GetType('KillerMCP.Notify.RepairConfirmWindow', $true)
$request = @{ Action = 'fixture'; Title = 'Fixture repair'; Tier = 'Safe'; Effects = 'No change'; Target = 'fixture'; Command = 'fixture preview' } | ConvertTo-Json -Compress
$encoded = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($request))
$window = $windowType.GetMethod('TryCreate').Invoke($null, @($encoded))
if (-not $window) { throw 'Repair confirmation fixture was refused.' }
try {
    $windowType.GetField('_armDelay', [Reflection.BindingFlags]'Instance,NonPublic').SetValue($window, 0)
    $field = $windowType.GetField('_verifyConsent', [Reflection.BindingFlags]'Instance,NonPublic')
    if ($field) {
        $deny = [Func[IntPtr,string,System.Threading.Tasks.Task[bool]]]{ param($owner, $message) [System.Threading.Tasks.Task]::FromResult([bool]$false) }
        $field.SetValue($window, $deny)
    }
    $handler = $windowType.GetMethod('Approve_Click', [Reflection.BindingFlags]'Instance,NonPublic')
    $handler.Invoke($window, @($null, [Windows.RoutedEventArgs]::new())) | Out-Null
    if ($window.Approved) { throw 'Clicking Approve authorized a repair without verified Windows Hello consent.' }
    Write-Output 'PASS: an automated Approve click cannot authorize without Windows Hello verification.'
} finally { $window.Close(); $app.Shutdown() }
