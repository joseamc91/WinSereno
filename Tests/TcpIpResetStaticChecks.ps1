$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
$count=0
function Check([bool]$ok,[string]$label){if(-not $ok){throw $label};$script:count++}
function Source([string]$path){[IO.File]::ReadAllText((Join-Path $project $path))}
$preflight=Source 'Services/TcpIpResetPreflightService.cs'
Check ($preflight.Contains('DHCPEnabled') -and $preflight.Contains('GetIPv4Properties()?.IsDhcpEnabled')) 'Two structured Windows DHCP sources'
Check ($preflight.Contains('PhysicalAdapter') -and $preflight.Contains('DhcpRenewalPolicy.IsEligible(true, true, true, true')) 'Existing physical policy reused, not a third exclusion list'
Check ($preflight.Contains('mode = knownPhysical ? Classify') -and $preflight.Contains('Ipv4ConfigurationMode.Unknown')) 'Uncertain physical/DHCP configuration never assumed DHCP'
foreach($field in @('IPv4Mask','GatewayAddresses','DnsAddresses','UnicastAddresses')){Check ($preflight.Contains($field)) "Read-only snapshot data: $field"}
Check (-not $preflight.Contains('Process.Start') -and -not $preflight.Contains('netsh ') -and -not $preflight.Contains('SetIP')) 'Preflight has no write operations or commands'
Check ($preflight.Contains('Interlocked.Exchange(ref consumed, 1)') -and $preflight.Contains('ElevatedTaskCatalog.ResetTcpIpId + "|"')) 'Single-use task-scoped approval'
Check ($preflight.Contains('a.Id.ToUpperInvariant() + ":" + (int)a.Mode') -and -not $preflight.Contains('a.IPv4.To')) 'Revalidation binds identity/mode, not mutable address'
$main=Source 'ViewModels/MainViewModel.cs'
$flow=$main.Substring($main.IndexOf('private async Task ResetTcpIpAsync()'),$main.IndexOf('private async Task ResetWinsockAsync()')-$main.IndexOf('private async Task ResetTcpIpAsync()'))
Check ($flow.IndexOf('tcpIpPreflight.Read()') -lt $flow.IndexOf('dialogs.ConfirmTask(task)')) 'Read-only preflight precedes first confirmation'
Check ($flow.IndexOf('dialogs.ConfirmTask(task)') -lt $flow.IndexOf('warningDialogs.ConfirmTcpIpReset(snapshot)')) 'Normal confirmation precedes special warning'
Check ($flow.IndexOf('warningDialogs.ConfirmTcpIpReset(snapshot)') -lt $flow.IndexOf('Runner.RunAsync(task)')) 'Second decision precedes UAC/runner'
Check ($flow.Contains('if (!warningAccepted) return;') -and $flow.Contains('if (snapshot.RequiresWarning)')) 'Cancellation and pure-DHCP paths'
Check (-not $flow.Contains('history.') -and -not $flow.Contains('ProgressChanged')) 'No extra history action for preflight or warning'
foreach($message in @('Preflight TCP/IP iniciado','Interfaces revisadas=','Segunda advertencia requerida=','Segunda advertencia TCP/IP','aún sin UAC ni comandos','se permite solicitar UAC')){Check ($flow.Contains($message)) "Useful consent logging: $message"}
$runner=Source 'Services/MaintenanceTaskRunner.cs'
Check ($runner.Contains('requested.TcpIpApproval == null') -and $runner.Contains('requested.TcpIpApproval.Consume()')) 'Runner rejects missing/reused consent before elevation'
Check ($runner.IndexOf('if (!valid) throw new InvalidDataException("Handshake IPC inválido.")') -lt $runner.IndexOf('WorkerProtocol.WriteText(writer, task.TcpIpApproval.Fingerprint)')) 'Consent transported only after authenticated handshake'
Check ($runner.Contains('writer.Write(task.TcpIpApproval.WarningAccepted)')) 'Only scoped context and consent cross existing pipe'
$worker=Source 'Services/ElevatedWorker.cs'
$tcp=$worker.Substring($worker.IndexOf('if (args[1] == ElevatedTaskCatalog.ResetTcpIpId)'),$worker.IndexOf('if (args[1] == ElevatedTaskCatalog.DiagnosticIntegrityId')-$worker.IndexOf('if (args[1] == ElevatedTaskCatalog.ResetTcpIpId)'))
Check ($tcp.Contains('new TcpIpResetPreflightService(null).Read()') -and $tcp.Contains('ValidateContext(current, fingerprint, warningAccepted)')) 'Fresh elevated preflight before fixed execution'
Check ($tcp.Contains('WorkerProtocol.WithTimeout') -and $tcp.Contains('WorkerMessage.TaskFailed') -and $tcp.Contains('return 5')) 'Bounded context handshake, fail closed without command'
Check ($worker.IndexOf('ValidateContext(current, fingerprint, warningAccepted)') -lt $worker.IndexOf('return await ExecuteAsync(writer, args[1])')) 'Safety context checked before native process'
Check ($worker.Contains('GetNamedPipeServerProcessId') -and $runner.Contains('GetNamedPipeClientProcessId')) 'PID authentication retained'
[xml]$window=Source 'Views/TcpIpResetWarningWindow.xaml';$ns=[Xml.XmlNamespaceManager]::new($window.NameTable);$ns.AddNamespace('p','http://schemas.microsoft.com/winfx/2006/xaml/presentation');$ns.AddNamespace('x','http://schemas.microsoft.com/winfx/2006/xaml')
$button=$window.SelectSingleNode('//p:Button[@x:Name="ResetButton"]',$ns)
Check ($button.GetAttribute('Content') -eq 'Restablecer TCP/IP' -and $button.GetAttribute('IsEnabled') -eq '{Binding CanContinue}') 'Explicit dangerous button bound to conscious acknowledgement'
Check ($null -ne $window.SelectSingleNode('//p:Button[@Content="Cancelar" and @IsCancel="True"]',$ns)) 'Escape/Cancel does not grant consent'
Check ($window.OuterXml.Contains('He revisado mi configuración de red y entiendo que podría tener que restaurarla manualmente.')) 'Exact conscious acknowledgement text'
Check ($window.OuterXml.Contains('DynamicResource WarningBrush') -and -not $window.OuterXml.Contains('DynamicResource ErrorBrush')) 'Warning accent, no error-red window'
Check ($window.OuterXml.Contains('Binding Snapshot.WarningInterfaces') -and $window.OuterXml.Contains('Binding Mask') -and $window.OuterXml.Contains('Binding Dns')) 'Each manual/unknown interface snapshot visible'
Check (-not $window.OuterXml.Contains('x:Static') -and -not $window.OuterXml.Contains('xmlns:local')) 'New window avoids second-pass local XAML patterns'
Check ((Source 'Views/TcpIpResetWarningWindow.xaml.cs').Contains('if (((TcpIpResetWarningViewModel)DataContext).CanContinue) DialogResult = true;')) 'Click handler independently guards acceptance'
Check ((Source 'Views/DialogService.cs').Contains('new TcpIpResetWarningWindow(snapshot) { Owner = Application.Current.MainWindow }.ShowDialog() == true')) 'Only explicit true modal result grants consent, X cancels'
[xml]$layout=Source 'Views/MainWindow.xaml';$ns=[Xml.XmlNamespaceManager]::new($layout.NameTable);$ns.AddNamespace('p','http://schemas.microsoft.com/winfx/2006/xaml/presentation');$ns.AddNamespace('x','http://schemas.microsoft.com/winfx/2006/xaml')
$card=$layout.SelectSingleNode('//p:ItemsControl[@x:Name="NetworkAdapterCards"]/p:ItemsControl.ItemTemplate/p:DataTemplate/p:Border/p:Grid',$ns)
Check ($null -ne $card) 'Structural adapter card grid'
$rows=$card.SelectNodes('p:Grid.RowDefinitions/p:RowDefinition',$ns)
Check ($rows.Count -eq 3 -and $rows[0].GetAttribute('Height') -eq 'Auto' -and $rows[1].GetAttribute('Height') -eq '*' -and $rows[2].GetAttribute('Height') -eq 'Auto') 'Auto/star/Auto structural bottom anchor'
$expander=$card.SelectSingleNode('p:Expander',$ns)
Check ($expander.GetAttribute('Grid.Row') -eq '2' -and -not $expander.HasAttribute('Visibility')) 'All adapter cards have bottom details, including disconnected'
Check (-not $card.HasAttribute('Height') -and -not $expander.HasAttribute('Height')) 'Details can expand naturally, no adapter-specific magic height'
Check ($layout.OuterXml.Contains('Columns="3"')) 'Shared three-column layout intact'
foreach($theme in @('Light','Dark')){Check ((Source "Themes/$theme.xaml").Contains('x:Key="WarningBrush"')) "$theme existing warning resource"}
Write-Output "$count comprobaciones estáticas TCP/IP correctas; sin consultas reales, comandos o UAC."
