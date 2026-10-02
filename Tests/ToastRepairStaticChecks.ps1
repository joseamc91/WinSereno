$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
$count=0
function Check([bool]$value,[string]$name) { if(-not $value) {throw $name}; $script:count++ }
function Source([string]$relative) { [IO.File]::ReadAllText((Join-Path $project $relative)) }
$xaml=Source 'Views\MainWindow.xaml'
$toast=Source 'Views\TaskExecutionPanel.xaml'
$main=Source 'ViewModels\MainViewModel.cs'
$card=Source 'ViewModels\RepairTaskViewModel.cs'
[xml]$xml=$xaml
$ns=[Xml.XmlNamespaceManager]::new($xml.NameTable)
$ns.AddNamespace('p','http://schemas.microsoft.com/winfx/2006/xaml/presentation')
$ns.AddNamespace('x','http://schemas.microsoft.com/winfx/2006/xaml')
$layer=$xml.SelectSingleNode('//p:Canvas[@x:Name="ToastLayer"]',$ns)
$toastHost=$xml.SelectSingleNode('//p:ContentControl[@x:Name="TaskExecutionHost"]',$ns)
Check ($null -ne $layer -and $toastHost.ParentNode -eq $layer) 'One Canvas overlay hosts existing execution panel'
Check ($layer.GetAttribute('Grid.RowSpan') -eq '3' -and $layer.ParentNode.GetAttribute('Grid.Column') -eq '1') 'Overlay restricted to entire main area'
Check ($toastHost.GetAttribute('Canvas.Right') -eq '0' -and $toastHost.GetAttribute('Canvas.Bottom') -eq '0') 'Bottom right anchoring'
Check ($layer.ParentNode.SelectSingleNode('p:Grid.RowDefinitions',$ns).ChildNodes.Count -eq 3) 'No operation row in vertical flow'
Check (-not $xaml.Contains('Grid.Row="3"')) 'Old footer removed'
Check ((Source 'Views\MainWindow.xaml.cs').Contains('TaskExecutionHost.Content = new TaskExecutionPanel()')) 'Existing code-behind injection retained'
Check ((Source 'Views\MainWindow.xaml.cs').Contains('System.Math.Min(500, e.NewSize.Width)')) 'Width clamps to available main area'
Check ((Source 'Views\MainWindow.xaml.cs').Contains('TaskExecutionHost.MaxHeight')) 'Tall toast constrained to viewport'
Check ($toast.Contains('TextWrapping="Wrap"') -and -not $toast.Contains('TextTrimming=')) 'Wrapping without text truncation'
Check (-not $toast.Contains('Binding Progress.StdOut') -and -not $toast.Contains('Binding Progress.StdErr')) 'No full stream blocks in toast'
Check ($toast.Contains('Binding CanDismissTaskPanel') -and $toast.Contains('Binding DismissTaskPanelCommand')) 'X uses existing dismissal policy'
Check ($toast.Contains('ToastCloseBrush') -and $toast.Contains('Foreground="{DynamicResource ToastTextBrush}"')) 'Red close style with white foreground'
Check ($toast.Contains('Binding CanCancelToast') -and $toast.Contains('Binding CancelTaskCommand')) 'Cancelable-only control'
Check ($toast.Contains('Binding DetailsCommand')) 'Existing detail entry point'
Check ($toast.Contains('Binding ToastPercentage, Mode=OneWay') -and $toast.Contains('IsIndeterminate="False"')) 'Real read-only progress'
Check ($main.Contains('if (count > 0) DiagnosticToastCounts.Add')) 'Zero counters omitted structurally'
foreach($counter in @('HealthyCount','AttentionCount','ErrorCount','NotCheckedCount')) { Check ($main.Contains('Diagnosis.'+$counter)) "Structured counter $counter" }
Check (-not $main.Contains('UserSummary.Split') -and -not $main.Contains('Regex.Match')) 'No summary parsing'
$panel=Source 'Services\OperationPanelState.cs'
Check (-not $panel.Contains('Timer') -and -not $panel.Contains('Task.Delay')) 'No timed dismissal'
Check ($main.Contains('panel.Observe') -and $main.Contains('panel.Dismiss')) 'One existing panel state'
Check ($main.Contains('CurrentSection == NavigationSection.Repair ? ""')) 'Repair notice participates in collapsed notice logic'
Check ($main.Contains('Comprueba y repara componentes de Windows mediante acciones explícitas; las herramientas administrativas solicitan confirmación y permisos de administrador antes de ejecutarse.')) 'Exact Repair description'
Check (-not $xaml.Contains('Control específico sobre las comprobaciones, reparaciones y mantenimiento.')) 'Redundant Repair subtitle removed'
foreach($technical in @('DISM /CheckHealth','DISM /ScanHealth','DISM /RestoreHealth','SFC /scannow','DISM /StartComponentCleanup','CHKDSK')) { Check ($card.Contains('return "'+$technical+'"')) "Exact annotation $technical" }
Check ($xaml.Contains('<Run Text="{Binding DisplayTitle, Mode=OneWay}"') -and $xaml.Contains('<Run Text="{Binding DisplayTechnicalName, Mode=OneWay}"')) 'Inline friendly and technical names'
Check (-not $xaml.Contains('Text="{Binding TechnicalName}"')) 'No permanent separate technical line'
Check ($xaml.Contains('Text="{Binding DisplayDescription}"')) 'Description uses presentation field'
Check ($xaml.Contains('ScanHealth → RestoreHealth si es necesario → SFC') -and $xaml.Contains('Value="repair.complete"')) 'Complete Repair retained'
Check ($xaml.Contains('Value="Sin resultados durante esta sesión."')) 'Absent session result remains collapsed'
foreach($theme in @('Light','Dark')) {
 [xml]$colors=Source "Themes\$theme.xaml"
 foreach($key in @('ToastBackgroundBrush','ToastTextBrush','ToastCloseBrush','ToastGoodBrush','ToastWarningBrush','ToastErrorBrush','ToastMutedBrush')) { Check ((Source "Themes\$theme.xaml").Contains('x:Key="'+$key+'"')) "$theme resource $key" }
}
foreach($file in @('Views\MainWindow.xaml','Views\TaskExecutionPanel.xaml')) { $text=Source $file; [xml]$valid=$text; Check (-not $text.Contains('xmlns:local') -and -not $text.Contains('x:Static m:')) "No compiled local type dependency $file" }
Check ((Source 'app.manifest').Contains('level="asInvoker"')) 'Manifest retained'
$pageScroll=$xml.SelectSingleNode('//p:ScrollViewer[@x:Name="PageScroll"]',$ns)
$focusSetter=$pageScroll.SelectSingleNode('p:ScrollViewer.Style/p:Style/p:Style.Triggers/p:DataTrigger[@Value="Repair"]/p:Setter[@Property="FocusVisualStyle"]',$ns)
Check ($null -ne $focusSetter -and $focusSetter.GetAttribute('Value') -eq '{x:Null}') 'Only Repair viewport suppresses native giant focus visual'
Check (-not $pageScroll.HasAttribute('Focusable')) 'Viewport remains keyboard-focusable for scrolling'
$repairLists=@($xml.SelectNodes('//p:ItemsControl[@ItemsSource="{Binding RealRepairTasks}" or @ItemsSource="{Binding RepairTools}"]',$ns))
Check ($repairLists.Count -eq 3) 'Three Repair presentation-only list containers'
foreach($list in $repairLists) { Check ($list.GetAttribute('Focusable') -eq 'False') 'Non-interactive Repair list not focusable' }
Check (-not (Source 'Themes\Styles.xaml').Contains('Property="FocusVisualStyle"')) 'No global suppression of keyboard focus'
Check ((Source 'Themes\Styles.xaml').Contains('IsKeyboardFocused') -and (Source 'Themes\Styles.xaml').Contains('IsKeyboardFocusWithin')) 'Buttons and sidebar focus indicators retained'
Check ($toast.Contains('Effect="{DynamicResource ToastShadowEffect}"')) 'Toast shadow resolves from current theme'
function ThemeColor([string]$theme,[string]$key) {
 [xml]$doc=Source "Themes\$theme.xaml"
 $node=$doc.DocumentElement.ChildNodes | Where-Object { $_ -is [Xml.XmlElement] } | Where-Object { $_.GetAttribute('Key','http://schemas.microsoft.com/winfx/2006/xaml') -eq $key }
 return $node.GetAttribute('Color')
}
Check ((ThemeColor 'Light' 'ToastBackgroundBrush') -eq '#141E2D' -and (ThemeColor 'Light' 'ToastBorderBrush') -eq '#344359') 'Light toast appearance retained'
Check ((ThemeColor 'Dark' 'ToastBackgroundBrush') -eq '#090F1B') 'Dark deeper blue-black surface'
Check ((ThemeColor 'Dark' 'ToastBorderBrush') -eq '#4B6280') 'Dark stronger edge'
foreach($theme in @('Light','Dark')) {
 Check ((ThemeColor $theme 'ToastCloseBrush') -eq '#B02D42' -and (ThemeColor $theme 'ToastTextBrush') -eq '#FFFFFF') "$theme red X / white text retained"
 Check ((Source "Themes\$theme.xaml").Contains('x:Key="ToastShadowEffect"')) "$theme shadow resource exists"
}
foreach($theme in @('Light','Dark')) {
 [xml]$colors=Source "Themes\$theme.xaml"
 $shadow=$colors.DocumentElement.ChildNodes | Where-Object { $_ -is [Xml.XmlElement] -and $_.LocalName -eq 'DropShadowEffect' }
 Check ($shadow.GetAttribute('Key','http://schemas.microsoft.com/winfx/2006/xaml') -eq 'ToastShadowEffect') "$theme framework-only shadow resource"
 if($theme -eq 'Light') { Check ($shadow.GetAttribute('BlurRadius') -eq '12' -and $shadow.GetAttribute('ShadowDepth') -eq '2' -and $shadow.GetAttribute('Opacity') -eq '0.25') 'Light shadow parameters unchanged' }
 else { Check ($shadow.GetAttribute('BlurRadius') -eq '20' -and $shadow.GetAttribute('ShadowDepth') -eq '3' -and $shadow.GetAttribute('Opacity') -eq '0.45') 'Dark shadow strengthened' }
}
Check (-not $toast.Contains('x:Static') -and -not $xaml.Contains('xmlns:local')) 'No local WPF type references introduced'
Write-Output "$count comprobaciones estáticas correctas; no EXE, herramientas reales ni UAC."
