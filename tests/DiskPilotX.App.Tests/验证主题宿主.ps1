#Requires -Version 7.6
# 在独立进程中验证实际 WPF 宿主、背景像素与系统磨砂状态，使用隔离配置。
param([Parameter(Mandatory=$true)][string]$PublishDirectory,[Parameter(Mandatory=$true)][string]$OutputPath)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework
$PublishDirectory=[IO.Path]::GetFullPath($PublishDirectory)
$env:PATH=$PublishDirectory+';'+$env:PATH
$resolver=[Func[System.Runtime.Loader.AssemblyLoadContext,System.Reflection.AssemblyName,System.Reflection.Assembly]]{param($context,$name) $path=Join-Path $PublishDirectory ($name.Name+'.dll'); if(Test-Path -LiteralPath $path){return $context.LoadFromAssemblyPath($path)};return $null}
[System.Runtime.Loader.AssemblyLoadContext]::Default.add_Resolving($resolver)
[void][Reflection.Assembly]::LoadFrom((Join-Path $PublishDirectory 'TinyHarness.dll'))
[void][Reflection.Assembly]::LoadFrom((Join-Path $PublishDirectory 'DiskPilotX.Core.dll'))
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $PublishDirectory 'DiskPilot-X.dll'))
$app=[DiskPilotX.App.App]::new()
$app.InitializeComponent()
$app.ShutdownMode=[Windows.ShutdownMode]::OnExplicitShutdown
Add-Type -TypeDefinition 'using System;using System.Runtime.InteropServices;public static class ThemeHostNativeState{[DllImport("dwmapi.dll")]public static extern int DwmGetWindowAttribute(IntPtr h,int a,out int v,int s);}'
$window=$null
$clientFactory=$null
try {
    $settingsPath=Join-Path (Split-Path -Parent $OutputPath) '宿主回归独立配置.json'
    $store=[DiskPilotX.Core.Services.Settings.SettingsService]::new($settingsPath)
    $saved=[DiskPilotX.Core.Models.AppSettings]::new()
    $saved.ThemeProfile=[DiskPilotX.Core.Models.Theme.ThemeProfile]::VisionProLight.Clone()
    $saved.ThemeProfile.GlassEnabled=$true
    $saved.ThemeProfile.GlassOpacity=0
    $store.SaveSettings($saved)
    $manager=[DiskPilotX.App.Services.ThemeManager]::new($store)
    $manager.Initialize()
    $manager.Refresh()
    $window=[DiskPilotX.App.MainWindow]::new()
    $window.ShowActivated=$false
    $manager.RegisterMainWindow($window)
    $clientFactory=[DiskPilotX.Core.Services.Llm.ModelHttpClientFactory]::new()
    $tester=[DiskPilotX.Core.Services.Assistant.ModelConnectionTester]::new([DiskPilotX.Core.Services.Llm.ModelClientFactory]::new($clientFactory))
    $vm=[DiskPilotX.App.ViewModels.SettingsViewModel]::new($store,$tester,$manager)
    $page=$window.FindName('SettingsPage')
    $page.DataContext=$vm
    $page.Visibility=[Windows.Visibility]::Visible
    $root=$window.FindName('RootGrid')
    $root.Measure([Windows.Size]::new(1200,800))
    $root.Arrange([Windows.Rect]::new(0,0,1200,800))
    $root.UpdateLayout()
    $app.Dispatcher.Invoke([Action]{},[Windows.Threading.DispatcherPriority]::DataBind)
    $picker=$page.FindName('ThemeSectionControl').FindName('ThemeColorPicker')
    $hex=$picker.FindName('HexTextBox')
    $handle=[Windows.Interop.WindowInteropHelper]::new($window).Handle
    $samples=[Collections.Generic.List[object]]::new()
    foreach($color in @('#FF267A52','#FFA34C76')) {
        $hex.Text=$color
        $hex.RaiseEvent([Windows.RoutedEventArgs]::new([Windows.UIElement]::LostFocusEvent))
        $app.Dispatcher.Invoke([Action]{},[Windows.Threading.DispatcherPriority]::DataBind)
        $root.UpdateLayout()
        $nativeType=0
        [void][ThemeHostNativeState]::DwmGetWindowAttribute($handle,38,[ref]$nativeType,4)
        $hostColor=([Windows.Media.SolidColorBrush]$root.Background).Color
        $cardColor=([Windows.Media.SolidColorBrush]$page.FindName('CardBorder').Background).Color
        $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new(1200,800,96,96,[Windows.Media.PixelFormats]::Pbgra32)
        $bitmap.Render($root)
        $pixel=[byte[]]::new(4)
        $bitmap.CopyPixels([Windows.Int32Rect]::new(100,300,1,1),$pixel,4,0)
        $hostPixel='{0:X2}{1:X2}{2:X2}{3:X2}' -f $pixel[3],$pixel[2],$pixel[1],$pixel[0]
        $samples.Add([pscustomobject]@{Input=$color;Picker=$picker.SelectedColor.ToString();ViewModel=$vm.ThemeBackgroundColor.ToString();Profile=$manager.CurrentProfile.BackgroundColor;Host=$hostColor.ToString();HostAlpha=[int]$hostColor.A;HostPixel=$hostPixel;Card=$cardColor.ToString();CardAlpha=[int]$cardColor.A;DwmBackdrop=$nativeType;GlassEnabled=$vm.GlassEnabled})
    }
    $vm.GlassOpacityPercent=100
    $app.Dispatcher.Invoke([Action]{},[Windows.Threading.DispatcherPriority]::DataBind)
    $fullColor=([Windows.Media.SolidColorBrush]$root.Background).Color
    $vm.GlassEnabled=$false
    $nativeOff=0
    [void][ThemeHostNativeState]::DwmGetWindowAttribute($handle,38,[ref]$nativeOff,4)
    $vm.GlassEnabled=$true
    $nativeOn=0
    [void][ThemeHostNativeState]::DwmGetWindowAttribute($handle,38,[ref]$nativeOn,4)
    $result=[pscustomobject]@{Samples=$samples.ToArray();FullHost=$fullColor.ToString();FullAlpha=[int]$fullColor.A;DwmOff=$nativeOff;DwmOn=$nativeOn}
    $json=$result|ConvertTo-Json -Depth 6
    [IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputPath),$json,[Text.UTF8Encoding]::new($false))
    $json
    if(($samples|Where-Object {$_.Input -ne $_.ViewModel -or $_.Input -ne $_.Profile -or ($_.Host.Substring(3) -ne $_.Input.Substring(3)) -or $_.HostAlpha -le 0 -or $_.CardAlpha -ne 255 -or $_.DwmBackdrop -ne 3}).Count -gt 0 -or $nativeOff -ne 1 -or $nativeOn -ne 3 -or $fullColor.A -ne 255 -or $samples[0].HostPixel -eq $samples[1].HostPixel){throw '宿主背景或原生磨砂状态没有通过回归检查。'}
} catch {
    $failure=$_.Exception; while($failure){Write-Output ($failure.GetType().FullName+': '+$failure.Message);$failure=$failure.InnerException};throw
} finally {
    if($window){$window.GetType().GetField('_closeConfirmed',[Reflection.BindingFlags]'NonPublic,Instance').SetValue($window,$true);$window.Close()}
    if($clientFactory){$clientFactory.Dispose()}
    $app.Shutdown()
    [System.Runtime.Loader.AssemblyLoadContext]::Default.remove_Resolving($resolver)
}
