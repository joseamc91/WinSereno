$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'LocalizationSource.ps1')
$count=0
function Check([bool]$ok,[string]$label){if(-not $ok){throw $label};$script:count++}
function Source([string]$path){(Read-LocalizedSource $path)}
[xml]$xml=Source 'Views/MainWindow.xaml'
$ns=[Xml.XmlNamespaceManager]::new($xml.NameTable)
$ns.AddNamespace('p','http://schemas.microsoft.com/winfx/2006/xaml/presentation')
$ns.AddNamespace('x','http://schemas.microsoft.com/winfx/2006/xaml')
$cards=$xml.SelectSingleNode('//p:ItemsControl[@x:Name="NetworkAdapterCards"]',$ns)
Check ($null -ne $cards -and $cards.GetAttribute('ItemsSource') -eq '{Binding Network.Adapters}') 'Cards use existing physical adapter collection'
$homeCardsNode=$xml.SelectSingleNode('//p:ItemsControl[@x:Name="HomeInformationCards"]',$ns)
$panel=$xml.SelectSingleNode('//p:ItemsPanelTemplate[@x:Key="ThreeColumnCardPanel"]/p:UniformGrid',$ns)
$layout=$xml.SelectSingleNode('//p:Style[@x:Key="ThreeColumnCards"]',$ns)
$surface=$xml.SelectSingleNode('//p:Style[@x:Key="ThreeColumnCard"]',$ns)
Check ($null -ne $panel -and $panel.GetAttribute('Columns') -eq '3') 'Exactly three equal structural columns'
Check ($cards.GetAttribute('Style') -eq '{StaticResource ThreeColumnCards}' -and $homeCardsNode.GetAttribute('Style') -eq $cards.GetAttribute('Style')) 'Home and Network share one column layout'
Check ($layout.SelectSingleNode('p:Setter[@Property="ItemsPanel"]',$ns).GetAttribute('Value') -eq '{StaticResource ThreeColumnCardPanel}') 'One shared ItemsPanel template'
Check ($layout.SelectSingleNode('p:Setter[@Property="Margin"]',$ns).GetAttribute('Value') -eq '-5,0,-5,0' -and $surface.SelectSingleNode('p:Setter[@Property="Margin"]',$ns).GetAttribute('Value') -eq '5,0,5,10') 'Half-gaps cancelled at page edges'
$card=$cards.SelectSingleNode('p:ItemsControl.ItemTemplate/p:DataTemplate/p:Border',$ns)
Check ($card.GetAttribute('Style') -eq '{StaticResource ThreeColumnCard}' -and $surface.GetAttribute('BasedOn') -eq '{StaticResource Card}' -and $surface.SelectSingleNode('p:Setter[@Property="Padding"]',$ns).GetAttribute('Value') -eq '12') 'Existing themed card style and compact padding'
Check (-not $card.HasAttribute('Width') -and -not $card.HasAttribute('MaxWidth') -and $surface.SelectSingleNode('p:Setter[@Property="HorizontalAlignment"]',$ns).GetAttribute('Value') -eq 'Stretch') 'Cards fill their structural column without fixed width'
$homeCard=$homeCardsNode.SelectSingleNode('p:ItemsControl.ItemTemplate/p:DataTemplate/p:Border',$ns)
Check ($homeCard.GetAttribute('Style') -eq $card.GetAttribute('Style') -and -not $homeCard.HasAttribute('Width')) 'Home shares the same stretching card surface'
foreach($binding in @('Kind','CardName','Status','LinkSpeed','IPv4','Gateway','Dns','WifiDetails')){
 Check ($card.OuterXml.Contains('Binding '+$binding)) "Adapter field retained: $binding"
}
Check (-not $card.OuterXml.Contains('MaximumSpeed') -and -not $card.OuterXml.Contains('SpeedDetails')) 'Maximum capacity absent from adapter card'
Check (-not $card.OuterXml.Contains('Binding Heading')) 'Old duplicate Ethernet/Ethernet heading removed'
Check ($card.OuterXml.Contains('Binding HasInterfaceName') -and $card.OuterXml.Contains('Binding HasCardName')) 'Repeated category/name suppressed by presentation flags'
Check ($card.OuterXml.Contains('Value="Desconectado"') -and $card.OuterXml.Contains('DynamicResource ErrorBrush') -and $card.OuterXml.Contains('DynamicResource GoodBrush')) 'Green connected / red disconnected / neutral unknown'
Check ($card.OuterXml.Contains('Header="Detalles del adaptador"')) 'Gateway DNS WiFi are optional local details'
$vm=Source 'ViewModels/NetworkViewModel.cs'
Check ($vm.Contains('Connectivity = check.ProbeDetails') -and -not $vm.Contains('Connectivity = result.DetailedDescription')) 'Global tests do not copy adapter detail block'
Check ($vm.Contains('refreshedAt = DateTimeOffset.Now') -and $vm.Contains('Última actualización:')) 'Real refresh completion timestamp'
Check ($vm.Contains('ConnectivityStatusCode = result.Status.ToString()')) 'Connectivity uses structured diagnostic status'
Check ($vm.Contains('switch (ConnectivityStatusCode)') -and -not $vm.Contains('Summary.Split') -and -not $vm.Contains('Summary.Contains')) 'Short status comes from structured state without parsing phrases'
foreach($text in @('Internet disponible','Conexión con incidencias','Sin conexión a Internet','Conexión no comprobada')){Check ($vm.Contains('return "'+$text+'"')) "Short user-facing status: $text"}
$header=$xml.SelectSingleNode('//p:Grid[@x:Name="NetworkStatusHeader"]',$ns)
Check ($null -ne $header -and $header.OuterXml.Contains('Binding Network.ConnectivityStatusText') -and $header.OuterXml.Contains('Text="Estado de red"')) 'Title and status share one header line'
Check ($header.SelectSingleNode('p:Button',$ns).GetAttribute('Grid.Column') -eq '1' -and $header.SelectSingleNode('p:Grid.ColumnDefinitions/p:ColumnDefinition[2]',$ns).GetAttribute('Width') -eq 'Auto') 'Refresh occupies right-hand header column'
Check (-not $xml.OuterXml.Contains('Binding Network.Summary')) 'Technical connectivity summary removed only from main presentation'
Check ($xml.OuterXml.Contains('Binding Network.RefreshedText') -and $xml.OuterXml.Contains('Binding Network.HasConnectivityDetails')) 'Timestamp and nonempty global detail visibility'
$main=Source 'ViewModels/MainViewModel.cs'
Check ($main.Contains('public string PageNotice => "";')) 'Network notice fully collapsed through shared infrastructure'
Check ($main.Contains('Consulta y actualiza el estado de la red; las herramientas solicitan confirmación y permisos de administrador cuando corresponde.')) 'One clear page description'
foreach($command in @('FlushDnsCommand','RenewDhcpCommand','RestartAdapterCommand','ResetWinsockCommand','ResetTcpIpCommand')){
 Check ($xml.OuterXml.Contains('Binding '+$command)) "Existing action button: $command"
}
$capture=Source 'Services/FixedTaskProcess.cs'
Check ($capture.Contains('NetshOutputReader.PumpAsync(process.StandardOutput.BaseStream') -and $capture.Contains('NetshOutputReader.PumpAsync(process.StandardError.BaseStream')) 'Decode original netsh stdout/stderr bytes'
Check ($capture.Contains('taskId == ElevatedTaskCatalog.FlushDnsId ? Encoding.GetEncoding((int)GetOEMCP())')) 'Flush DNS encoding unchanged'
Check ($capture.Contains('RunCommandAsync(AdapterRestartService.Command(adapter, attempt), NetshLegacyEncoding()')) 'Restart adapter only shares netsh text capture'
Check ($capture.Contains('service.ValidateTarget(adapter, attempt)')) 'Restart worker revalidation retained'
$reader=Source 'Services/NetshOutputReader.cs'
Check ($reader.Contains('new UTF8Encoding(false, true)') -and $reader.Contains('catch (DecoderFallbackException)')) 'Strict UTF8 with native legacy fallback'
Check ($reader.Contains('if (line.Length > 0)') -and $reader.Contains('line.SetLength(0)')) 'Final tail and no repeated byte lines'
Check (-not $reader.Contains('.Replace(')) 'No mojibake text substitutions'
$diagnostic=Source 'Services/NetworkDiagnosticCheck.cs'
Check ($diagnostic.Contains('probes[3].TimedOut && probes.Take(3).All(p => p.Success)')) 'HTTPS timeout exception requires all three independent positive signals'
Check ($diagnostic.Contains('bool internetAvailable = https || supportedTimeout') -and $diagnostic.Contains('internetAvailable && !linkWarning')) 'General result is separate from HTTPS and retains link warnings'
Check ($diagnostic.Contains('if (ex.Status == WebExceptionStatus.Timeout) return HttpsTimeout') -and $diagnostic.Contains('Name = "HTTPS", TimedOut = true')) 'Timeout is structured, never inferred from localized detail text'
Check ($diagnostic.Contains('code != 407 && expectedEndpoint')) 'Proxy auth and unexpected endpoints never confirm HTTPS'
Check ($diagnostic.Contains('request.Method = "HEAD"; request.Timeout = 5000; request.ReadWriteTimeout = 5000') -and $diagnostic.Contains('Task.Delay(5000)') -and $diagnostic.Contains('request.Abort()')) 'Read-only HEAD remains bounded and aborted on timeout'
Check (-not $diagnostic.Contains('Process.Start') -and -not $diagnostic.Contains('SecurityProtocol =') -and -not $diagnostic.Contains('ServerCertificateValidationCallback')) 'No command fallback or weakened TLS validation'
foreach($parser in @('WinsockResetResultParser.cs','TcpIpResetResultParser.cs')){
 $text=Source "Services/$parser"
 Check (-not $text.Contains('├') -and -not $text.Contains('Normalize(')) "Parser uses real uncorrupted phrases: $parser"
}
foreach($theme in @('Light','Dark')){
 [xml]$valid=Source "Themes/$theme.xaml"
 foreach($key in @('SurfaceBrush','TextBrush','MutedBrush','GoodBrush','ErrorBrush','WarningBrush')){Check ((Source "Themes/$theme.xaml").Contains('x:Key="'+$key+'"')) "$theme shared resource: $key"}
}
Check (-not $card.OuterXml.Contains('Color="#') -and -not $xml.OuterXml.Contains('xmlns:local')) 'No hardcoded network colors or compiled local types'
Write-Output "$count comprobaciones estáticas de Red correctas; sin comandos reales ni UAC."
