$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'LocalizationSource.ps1')
$count=0
function Check([bool]$ok,[string]$label){if(-not $ok){throw $label};$script:count++}
function Source([string]$file){(Read-LocalizedSource $file)}
$vm=Source 'ViewModels/CleanupViewModel.cs';$main=Source 'ViewModels/MainViewModel.cs';$runner=Source 'Services/MaintenanceTaskRunner.cs';$analysis=Source 'Services/CleanupAnalysisService.cs'
[xml]$xml=Source 'Views/MainWindow.xaml';$ns=[Xml.XmlNamespaceManager]::new($xml.NameTable);$ns.AddNamespace('p','http://schemas.microsoft.com/winfx/2006/xaml/presentation');$ns.AddNamespace('x','http://schemas.microsoft.com/winfx/2006/xaml')
$page=$xml.SelectSingleNode('//p:StackPanel[@x:Name="CleanupPage"]',$ns)
Check ($null -ne $page) 'Cleanup keeps one explicit page layout'
Check ($main.Contains('Analiza el espacio que puede recuperarse y elige qué categorías quieres limpiar.')) 'One simple page description'
Check ($main.Contains('public string PageNotice => "";')) 'Cleanup notice empty, other notices retained'
Check ($xml.OuterXml.Contains('Binding HasPageNotice, Converter={StaticResource BoolVisibility}')) 'Existing full-collapse infrastructure reused'
$analyze=$page.SelectSingleNode('.//p:Button[@Command="{Binding Cleanup.AnalyzeCommand}"]',$ns)
Check ($analyze.GetAttribute('Style') -eq '{StaticResource PrimaryButton}') 'Analyze uses existing accent primary style'
Check ($vm.Contains('hasRun ? "Analizar de nuevo" : "Analizar"')) 'Reanalysis label'
Check ($page.OuterXml.Contains('Limpiar categorías marcadas') -and -not $page.OuterXml.Contains('Limpiar seleccionados')) 'Batch action clearly names marked categories'
Check (-not $page.OuterXml.Contains('Analizar con permisos de administrador') -and -not $main.Contains('AnalyzeWindowsTempCommand') -and -not $vm.Contains('CanAnalyzeElevated')) 'No separate elevated-analysis UI or dead command'
Check (-not $page.OuterXml.Contains('Potencialmente limpiable es una estimación, no una garantía')) 'Redundant footer removed with its margin'
Check (-not $page.OuterXml.Contains('Binding Details') -and -not $page.OuterXml.Contains('FileCount') -and -not $page.OuterXml.Contains('InaccessibleCount')) 'Technical counters hidden only from category presentation'
Check ($page.OuterXml.Contains('Binding Summary') -and $page.OuterXml.Contains('Binding DisplayPath') -and $page.OuterXml.Contains('Binding ElevatedAnalysisText')) 'Amounts, readable paths and real administrative timestamp retained'
Check ($page.OuterXml.Contains('Binding HasAmounts') -and $page.OuterXml.Contains('Binding Status')) 'Unknown category shows only state, no duplicate empty estimate'
Check ($page.SelectNodes('.//p:Expander[@IsExpanded="True"]',$ns).Count -eq 0) 'Details remain collapsed by default'
Check ($vm.Contains('Result.FileCount.ToString') -and $vm.Contains('Result.Information') -and $vm.Contains('row.Details') -and $main.Contains('row.Details')) 'Technical model/details still feed logs, Activity and live toast details'
Check ($vm.Contains('text.AppendLine(row.Name') -and $vm.Contains('outcome.StdOut = text.ToString()') -and $vm.Contains('Análisis de Limpieza |')) 'Full four-category report logged and retained'
foreach($command in @('UserTemp','WindowsTemp','Thumbnails','RecycleBin')){
 $category=@{UserTemp='UserTemporary';WindowsTemp='WindowsTemporary';Thumbnails='ThumbnailCache';RecycleBin='RecycleBin'}[$command]
 Check ($main.Contains('!Operations.IsActive && Cleanup.CanClean(CleanupCategory.'+$category+')')) "Analysis validity gates action: $category"
 Check ($main.Contains('if (Operations.IsActive || !Cleanup.CanClean(CleanupCategory.'+$category+')) return;')) "Direct call guarded too: $category"
}
Check ($main.Contains('!Operations.IsActive && Cleanup.CanCleanSelected') -and $vm.Contains('Categories.Where(c => c.IsSelected).All(c => c.CanClean)')) 'Every marked category must have valid data'
Check ($vm.Contains('Result.WasAnalyzed && Result.IsAvailable && !Result.WasCancelled') -and $vm.Contains('Result.PotentiallyCleanableBytes <= Result.TotalBytes')) 'Valid, noncancelled and coherent estimates required'
Check ($vm.Contains('(!IsWindowsTemporary || Result.AnalysisFinishedAt.HasValue)')) 'Windows estimate requires actual elevated analysis time'
Check ($vm.Contains('result.Category != CleanupCategory.RecycleBin')) 'Recycle bin stays unmarked by default'
Check ($vm.Contains('if (!hasRun) return selected;') -and $vm.Contains('Estimación parcial:') -and $vm.Contains('Estimación no disponible')) 'Simple initial, complete, partial and unknown selection summaries'
Check ($vm.Contains('FormatBytes(Result.TotalBytes) + " en la Papelera"')) 'Recycle amount only, no API details'
$style=$xml.SelectSingleNode('//p:Style[@x:Key="CleanupCategoryCheckBox"]',$ns)
Check ($style.GetAttribute('TargetType') -eq 'CheckBox' -and $page.SelectNodes('.//p:CheckBox[@Style="{StaticResource CleanupCategoryCheckBox}"]',$ns).Count -eq 1) 'Checkbox style is scoped to Cleanup template'
Check ($style.OuterXml.Contains('Width="20"') -and $style.OuterXml.Contains('Height="20"') -and $style.OuterXml.Contains('Value="30"')) 'Larger box and clickable area'
foreach($property in @('IsChecked','IsKeyboardFocused','IsEnabled')){Check ($style.OuterXml.Contains('Property="'+$property+'"')) "Native checkbox state supported: $property"}
Check (-not $style.OuterXml.Contains('Focusable') -and -not $style.OuterXml.Contains('FocusVisualStyle') -and -not $style.OuterXml.Contains('KeyDown')) 'No global keyboard/focus suppression'
Check ($style.OuterXml.Contains('DynamicResource AccentBrush') -and $style.OuterXml.Contains('DynamicResource SurfaceBrush') -and $style.OuterXml.Contains('Value="0.45"')) 'Both themes and clear disabled state'
Check ($analysis.Contains('public const int ProtectedRecentHours = 48;')) '48-hour protection unchanged'
Check ($analysis.Contains('result.TotalBytes = checked(result.TotalBytes + length)') -and $analysis.Contains('if (IsPotentiallyCleanable(modified, cutoff))')) 'Found bytes separate from conservative candidates'
Check ($analysis.Contains('FileMode.Open, FileAccess.Read') -and -not $analysis.Contains('File.Delete') -and -not $analysis.Contains('Directory.Delete')) 'Analysis remains read only'
Check ($analysis.Contains('Analyze(progress, token, false)') -and $analysis.Contains('includeWindows || p.Category != CleanupCategory.WindowsTemporary')) 'Normal scan skips Windows Temp; no partial privileged substitute'
Check ($vm.Contains('operations.Begin("Análisis de Limpieza", true)') -and $vm.Contains('operation.SetCancelable(false)')) 'Single global lease, native elevated phase noncancelable'
Check ($vm.Contains('await elevatedAnalysis(operation,') -and $vm.Contains('else Apply(previousWindows)')) 'One administrative invocation; post-clean refresh adds none'
Check ($vm.Contains('CurrentTask = new MaintenanceTask { Id = "cleanup.analyze", Name = "Análisis de Limpieza" }') -and $vm.Contains('outcome.SequenceSteps.Add')) 'Single user result with internal administrative details'
$method=$runner.Substring($runner.IndexOf('public async Task<MaintenanceTaskResult> RunCleanupAnalysisAsync'),$runner.IndexOf('internal async Task<MaintenanceTaskResult> RunDiagnosticAsync')-$runner.IndexOf('public async Task<MaintenanceTaskResult> RunCleanupAnalysisAsync'))
Check ($method.Contains('operations.Owns(lease)') -and $method.Contains('lease.Name != "Análisis de Limpieza"')) 'Administrative transport validates global lease'
Check ($method.Contains('RunElevatedCoreAsync(ElevatedTaskCatalog.Get(ElevatedTaskCatalog.WindowsTempAnalyzeId), false, null, lease, null, progress)')) 'Existing fixed worker transport reused; no second operation/publish stream'
Check (-not $method.Contains('Process.Start') -and -not $method.Contains('NamedPipe')) 'No duplicate elevation/IPC infrastructure'
Check ($vm.Contains('No se comprobaron los temporales de Windows porque se cancelaron los permisos de administrador.') -and $vm.Contains('FindingStatus.PartiallyCompleted')) 'UAC cancellation retains normal results, marks incomplete overall'
Check ($vm.Contains('recuperables estimados') -and -not $vm.Contains('Percentage =')) 'Brief structured estimate, no invented percentages'
Check (-not $xml.OuterXml.Contains('Portable · Acciones explícitas')) 'Fixed sidebar tagline removed'
Check ($main.Contains('ProductInformation.DisplayVersion')) 'UI consumes one public version source'
$assembly=Source 'Properties/AssemblyInfo.cs';Check ($assembly.Contains('AssemblyInformationalVersion("1.1.0")')) 'Current stable version'
foreach($name in @('AssemblyVersion','AssemblyFileVersion')){Check ($assembly.Contains($name+'("1.1.0.0")')) "Stable $name metadata"}
Check ((Source 'Infrastructure/ProductInformation.cs').Contains('GetCustomAttribute<AssemblyInformationalVersionAttribute>')) 'Standard informational version metadata'
Check ((Source 'app.manifest').Contains('level="asInvoker"')) 'Main app remains asInvoker'
Check (-not $style.OuterXml.Contains('x:Static') -and -not $xml.OuterXml.Contains('xmlns:local')) 'No new local XAML second-pass patterns'
foreach($theme in @('Light','Dark')){foreach($key in @('TextBrush','MutedBrush','SurfaceBrush','AccentBrush','AccentSoftBrush')){Check ((Source "Themes/$theme.xaml").Contains('x:Key="'+$key+'"')) "$theme checkbox resource $key"}}
Write-Output "$count comprobaciones estáticas de Limpieza/versión correctas; sin cargar WinSereno, UAC ni limpieza real."
