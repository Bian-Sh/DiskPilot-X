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
function Brush-Hex($brush){if($brush -is [Windows.Media.SolidColorBrush]){return $brush.Color.ToString()};return "非实色画刷"}
function Text-Channel([byte]$channel){$value=$channel/255.0;if($value -le 0.04045){return $value/12.92};return [Math]::Pow(($value+0.055)/1.055,2.4)}
function Text-Luminance($color){return 0.2126*(Text-Channel $color.R)+0.7152*(Text-Channel $color.G)+0.0722*(Text-Channel $color.B)}
function Text-Contrast($foreground,$background){$left=Text-Luminance $foreground;$right=Text-Luminance $background;return ([Math]::Max($left,$right)+0.05)/([Math]::Min($left,$right)+0.05)}
function Text-Composite($foreground,$background,[double]$opacity=1){$alpha=$foreground.A/255.0*$opacity;return [Windows.Media.Color]::FromRgb([byte][Math]::Round($foreground.R*$alpha+$background.R*(1-$alpha)),[byte][Math]::Round($foreground.G*$alpha+$background.G*(1-$alpha)),[byte][Math]::Round($foreground.B*$alpha+$background.B*(1-$alpha)))}
function Text-Nodes($node){if($node -is [Windows.Controls.TextBlock]){Write-Output $node};for($i=0;$i -lt [Windows.Media.VisualTreeHelper]::GetChildrenCount($node);$i++){Text-Nodes ([Windows.Media.VisualTreeHelper]::GetChild($node,$i))}}
try {
    $store=[DiskPilotX.Core.Services.Settings.SettingsService]::new((Join-Path (Split-Path -Parent $OutputPath) '文本主题独立配置.json'))
    $settings=[DiskPilotX.Core.Models.AppSettings]::new()
    $settings.ThemeProfile=[DiskPilotX.Core.Models.Theme.ThemeProfile]::VisionProLight.Clone()
    $store.SaveSettings($settings)
    $manager=[DiskPilotX.App.Services.ThemeManager]::new($store)
    $manager.Initialize();$manager.Refresh()
    $window=[DiskPilotX.App.MainWindow]::new()
    $window.ShowActivated=$false
    [void][Windows.Interop.WindowInteropHelper]::new($window).EnsureHandle()
    $flags=[Reflection.BindingFlags]'NonPublic,Instance'
    $window.GetType().GetField('_displayPathOverride',$flags).SetValue($window,'C:/Windows')
    $window.GetType().GetMethod('RebuildBreadcrumbBar',$flags).Invoke($window,@())|Out-Null
    $root=$window.FindName('RootGrid')
    $root.Measure([Windows.Size]::new(1200,800));$root.Arrange([Windows.Rect]::new(0,0,1200,800));$root.UpdateLayout()
    $navigation=@($window.FindName('BreadcrumbBar').Children|Where-Object {$_ -is [Windows.Controls.Button]})
    if($navigation.Count -eq 0){throw '没有创建实际面包屑控件。'}
    $treemap=$window.FindName('TreemapView').FindName('Treemap')
    $tip=$treemap.GetType().GetField('_toolTip',$flags).GetValue($treemap)
    $menu=[Windows.Controls.ContextMenu]::new()
    $window.GetType().GetMethod('EnsureBreadcrumbMenuTemplate',$flags).Invoke($window,@($menu))|Out-Null
    $menu.ItemTemplate.Seal()
    $menuText=$menu.ItemTemplate.LoadContent();$menuText.DataContext=[pscustomobject]@{Name='子目录';Path='C:/Windows'}
    $menuItem=[Windows.Controls.MenuItem]::new();$menuItem.Style=$menu.ItemContainerStyle;$menuItem.Header=$menuText
    $menuItem.Measure([Windows.Size]::new(300,40));$menuItem.Arrange([Windows.Rect]::new(0,0,300,40));$menuItem.ApplyTemplate()|Out-Null
    $modal=$window.FindName('ModalHost')
    $message=$modal.GetType().GetMethod('CreateMessageView',$flags).Invoke($modal,@('主题标题回归','主题正文回归',[DiskPilotX.App.Controls.ModalMessageType]0,[DiskPilotX.App.Controls.ModalButtonFlags]1))
    $message.Measure([Windows.Size]::new(500,700));$message.Arrange([Windows.Rect]::new(0,0,500,700))
    $modalTexts=@(Text-Nodes $message)
    $titleText=$modalTexts|Where-Object {$_.Text -eq '主题标题回归'}|Select-Object -First 1
    $bodyText=$modalTexts|Where-Object {$_.Text -eq '主题正文回归'}|Select-Object -First 1
    if(-not $titleText -or -not $bodyText){throw '没有找到实际弹窗标题或正文。'}
    $legacy=[DiskPilotX.App.Views.SettingsDialog]::new()
    $legacy.ShowActivated=$false
    [void][Windows.Interop.WindowInteropHelper]::new($legacy).EnsureHandle()
    $legacy.Content.Measure([Windows.Size]::new(450,540));$legacy.Content.Arrange([Windows.Rect]::new(0,0,450,540))
    $legacyTexts=@(Text-Nodes $legacy.Content)
    $legacyHint=$legacyTexts|Where-Object {$_.Text -eq [DiskPilotX.App.Localization.L]::Text('AnthropicBaseUrlHint')}|Select-Object -First 1
    $legacyPrivacy=$legacyTexts|Where-Object {$_.Text -eq [DiskPilotX.App.Localization.L]::Text('DataPrivacyText')}|Select-Object -First 1
    $treeHost=$window.FindName('TreeViewControl')
    $treeTemplate=$treeHost.FindName('FileTreeView').ItemTemplate
    $treeTemplate.Seal()
    $treeRow=$treeTemplate.LoadContent()
    $entry=[DiskPilotX.Core.Models.FileEntry]::new();$entry.Name='目录回归';$entry.Path='C:/Windows';$entry.IsDirectory=$true
    $entry.Children.Add([DiskPilotX.Core.Models.FileEntry]::new())
    $stats=[Collections.Generic.Dictionary[DiskPilotX.Core.Models.FileEntry,DiskPilotX.App.ViewModels.TreeEntryStats]]::new()
    $treeNode=[DiskPilotX.App.ViewModels.TreeViewItemViewModel]::new($entry,0,'Name',$false,$stats,$null,$true)
    $treeRow.DataContext=$treeNode
    $treeRow.Measure([Windows.Size]::new(1200,24));$treeRow.Arrange([Windows.Rect]::new(0,0,1200,24))
    $app.Dispatcher.Invoke([Action]{},[Windows.Threading.DispatcherPriority]::ApplicationIdle)
    $treeRow.UpdateLayout()
    $treeTexts=@(Text-Nodes $treeRow)
    $treeArrow=$treeTexts|Where-Object {$_.Text -eq '▸'}|Select-Object -First 1
    $treePercent=$treeTexts|Where-Object {$_.Text -eq $treeNode.ParentPercentText}|Select-Object -First 1
    if(-not $treeArrow -or -not $treePercent){throw '没有找到实际树行箭头或百分比文字。'}
    $percentLayer=[Windows.Media.VisualTreeHelper]::GetParent($treePercent)
    $percentFrame=$percentLayer.Children|Where-Object {$_ -is [Windows.Controls.Border] -and [double]::IsNaN($_.Width)}|Select-Object -First 1
    $percentFill=$percentLayer.Children|Where-Object {$_ -is [Windows.Controls.Border] -and -not [double]::IsNaN($_.Width)}|Select-Object -First 1
    $converter=[DiskPilotX.App.Converters.MarkdownToFlowDocumentConverter]::new()
    $document=$converter.Convert('**正文**',[Windows.Documents.FlowDocument],$null,[Globalization.CultureInfo]::InvariantCulture)
    $log=[DiskPilotX.App.Logging.UiLogEntry]::new([DateTimeOffset]::Now,[Serilog.Events.LogEventLevel]::Information,'日志正文',$null)
    $logBrush=$log.MessageBrush;$timeBrush=$log.TimeBrush
    $severityBrushes=[Collections.Generic.List[object]]::new()
    foreach($level in [Enum]::GetValues([Serilog.Events.LogEventLevel])){
        $severity=[DiskPilotX.App.Logging.UiLogEntry]::new([DateTimeOffset]::Now,$level,'级别正文',$null)
        $severityBrushes.Add([pscustomobject]@{Level=$level.ToString();Label=$severity.LevelBrush;Body=$severity.MessageBrush})
    }
    $custom=[DiskPilotX.Core.Models.Theme.ThemeProfile]::VisionProDark.Clone()
    $custom.Name='自定义';$custom.TextPrimaryColor='#FFF2E8BC';$custom.TextSecondaryColor='#FFCADBEF';$custom.TextTertiaryColor='#FF97B4D0'
    $snapshots=[Collections.Generic.List[object]]::new();$failures=[Collections.Generic.List[string]]::new()
    foreach($profile in @([DiskPilotX.Core.Models.Theme.ThemeProfile]::VisionProLight.Clone(),[DiskPilotX.Core.Models.Theme.ThemeProfile]::VisionProDark.Clone(),$custom)) {
        $manager.ApplyTheme($profile)
        $app.Dispatcher.Invoke([Action]{},[Windows.Threading.DispatcherPriority]::ApplicationIdle)
        $root.UpdateLayout()
        $state=[ordered]@{Theme=$profile.Name;Primary=$profile.TextPrimaryColor;Secondary=$profile.TextSecondaryColor;Tertiary=$profile.TextTertiaryColor;BreadcrumbButton=(Brush-Hex $navigation[0].Foreground);BreadcrumbText=(Brush-Hex $navigation[0].Content.Foreground);BreadcrumbChevron=(Brush-Hex $navigation[1].Content.Foreground);Menu=(Brush-Hex $menuItem.Foreground);MenuText=(Brush-Hex $menuText.Foreground);Tooltip=(Brush-Hex $tip.Foreground);ModalTitle=(Brush-Hex $titleText.Foreground);ModalBody=(Brush-Hex $bodyText.Foreground);Markdown=(Brush-Hex $document.Foreground);Log=(Brush-Hex $logBrush);LogTime=(Brush-Hex $timeBrush);LegacyHint=(Brush-Hex $legacyHint.Foreground);LegacyPrivacy=(Brush-Hex $legacyPrivacy.Foreground);ConsoleBackground=(Brush-Hex $window.FindName('ConsoleFrame').Background);TreeArrow=(Brush-Hex $treeArrow.Foreground);TreePercent=(Brush-Hex $treePercent.Foreground);PrimaryFrozen=([Windows.Media.SolidColorBrush]$app.FindResource('VP.TextPrimaryBrush')).IsFrozen}
        foreach($role in @('BreadcrumbButton','BreadcrumbText','BreadcrumbChevron','Menu','MenuText','Tooltip','ModalTitle','Markdown','Log','TreeArrow','TreePercent')){if($state[$role] -ne $profile.TextPrimaryColor){$failures.Add($profile.Name+'/'+$role+': '+$state[$role]+' != '+$profile.TextPrimaryColor)}}
        if($state.ModalBody -ne $profile.TextSecondaryColor){$failures.Add($profile.Name+'/ModalBody')}
        if($state.LogTime -ne $profile.TextTertiaryColor){$failures.Add($profile.Name+'/LogTime')}
        foreach($role in @('LegacyHint','LegacyPrivacy')){if($state[$role] -ne $profile.TextSecondaryColor){$failures.Add($profile.Name+'/'+$role)}}
        if($state.ConsoleBackground -ne $profile.BackgroundAltColor){$failures.Add($profile.Name+'/ConsoleBackground')}
        $background=([Windows.Media.SolidColorBrush]$window.FindName('ConsoleFrame').Background).Color
        $contrasts=@(foreach($severity in $severityBrushes){
            $labelRatio=Text-Contrast $severity.Label.Color $background
            $bodyRatio=Text-Contrast $severity.Body.Color $background
            if($labelRatio+0.00001 -lt 4.5 -or $bodyRatio+0.00001 -lt 4.5){$failures.Add($profile.Name+'/日志对比度/'+$severity.Level)}
            [pscustomobject]@{Level=$severity.Level;Label=(Brush-Hex $severity.Label);Body=(Brush-Hex $severity.Body);LabelRatio=[Math]::Round($labelRatio,3);BodyRatio=[Math]::Round($bodyRatio,3)}
        })
        $outerTreeBase=Text-Composite ([Windows.Media.SolidColorBrush]$app.FindResource('VP.SurfaceBrush')).Color $root.Background.Color
        $barBase=Text-Composite $percentFrame.Background.Color $outerTreeBase
        $barColor=Text-Composite $percentFill.Background.Color $barBase $percentFill.Opacity
        $barRatio=Text-Contrast $treePercent.Foreground.Color $barColor
        $state['PercentTextContrast']=[Math]::Round($barRatio,3)
        if($barRatio+0.00001 -lt 4.5){$failures.Add($profile.Name+'/百分比文字对比度')}
        $state['LogContrasts']=$contrasts
        $snapshots.Add([pscustomobject]$state)
    }
    $result=[pscustomobject]@{Snapshots=$snapshots.ToArray();Failures=$failures.ToArray()}
    [IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputPath),($result|ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
    $result|ConvertTo-Json -Depth 6
    if($failures.Count -gt 0){throw '实际文本控件没有跟随主题更新。'}
} finally {
    if($legacy){$legacy.Close()}
    if($window){$window.GetType().GetField('_closeConfirmed',[Reflection.BindingFlags]'NonPublic,Instance').SetValue($window,$true);$window.Close()}
    if($clientFactory){$clientFactory.Dispose()}
    $app.Shutdown()
    [System.Runtime.Loader.AssemblyLoadContext]::Default.remove_Resolving($resolver)
}
