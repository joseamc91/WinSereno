$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$count = 0
function Check([bool]$ok, [string]$label) { if (!$ok) { throw $label }; $script:count++ }
function Read([string]$path) { [IO.File]::ReadAllText((Join-Path $project $path)) }
$catalogs = @{}
foreach ($language in @('es','en')) {
    [xml]$xml = Read "Localization/Strings.$language.xaml"
    $entries = @{}
    foreach ($node in $xml.DocumentElement.ChildNodes) {
        if ($node.NodeType -ne [Xml.XmlNodeType]::Element) { continue }
        $key = $node.GetAttribute('Key', 'http://schemas.microsoft.com/winfx/2006/xaml')
        Check ($key.StartsWith('Text.') -and !$entries.ContainsKey($key)) "Unique logical key: $language/$key"
        Check (![string]::IsNullOrWhiteSpace($node.InnerText)) "Nonempty translation: $language/$key"
        $entries[$key] = $node.InnerText
    }
    $catalogs[$language] = $entries
}
Check ($catalogs.es.Count -eq $catalogs.en.Count) 'Same key count'
foreach ($key in $catalogs.es.Keys) { Check ($catalogs.en.ContainsKey($key)) "English parity: $key" }
foreach ($key in $catalogs.en.Keys) { Check ($catalogs.es.ContainsKey($key)) "Spanish parity: $key" }
foreach ($file in @(Get-ChildItem (Join-Path $project 'Views') -File -Filter '*.xaml')) {
    $source = [IO.File]::ReadAllText($file.FullName)
    foreach ($match in [regex]::Matches($source, '\{DynamicResource (Text\.[^}]+)\}')) {
        Check ($catalogs.es.ContainsKey($match.Groups[1].Value)) "Valid UI resource: $($file.Name)/$($match.Groups[1].Value)"
    }
    Check ($source -notmatch '(?:Text|Content|Header|Title|ToolTip|AutomationProperties.Name)="[^"{]') "No untranslated literal UI labels: $($file.Name)"
}
foreach ($directory in @('Models','ViewModels','Views','Localization')) {
    foreach ($file in Get-ChildItem (Join-Path $project $directory) -File -Filter '*.cs') {
        foreach ($match in [regex]::Matches([IO.File]::ReadAllText($file.FullName), 'LocalizationService\.(?:Source|Current\.Get)\("(Text\.[^"]+)"\)')) {
            Check ($catalogs.es.ContainsKey($match.Groups[1].Value)) "Valid C# resource: $($file.Name)/$($match.Groups[1].Value)"
        }
    }
}
$service = Read 'Localization/LocalizationService.cs'
Check ($service.Contains('new LanguageChoice("es", "Español")') -and $service.Contains('new LanguageChoice("en", "English")')) 'Central language registry with readable names'
Check ($service.Contains('Normalize') -and $service.Contains('Revision')) 'Fallback and runtime invalidation'
$presentation = Read 'Localization/LocalizationPresentation.cs'
foreach ($opaque in @('StdOut','StdErr','LastRelevantLine','WindowsDescription','ExactCommand','Arguments','Provider','Path')) {
    Check ($presentation.Contains($opaque)) "Opaque native/technical field: $opaque"
}
Check ($presentation.Contains('Run.TextProperty') -and $presentation.Contains('MultiBinding')) 'Inline labels and live bound results localized'
$storage = Read 'Services/PortableStorage.cs'
Check ($storage.Contains('[DataMember] public string Language') -and $storage.Contains('Normalize(settings.Language)')) 'Same config file; old/invalid language normalized'
$main = Read 'ViewModels/MainViewModel.cs'
Check ($main.Contains('settings.Language = new AppSettings().Language')) 'UI preference reset also restores Spanish'
$startup = Read 'App.xaml.cs'
Check ($startup.IndexOf('Current.Apply(settings.Language)') -lt $startup.IndexOf('new MainWindow(')) 'Persisted language applied before first window'
Check ($startup -notmatch 'CurrentUICulture|InstalledUICulture') 'No automatic Windows language selection'
[xml]$window = Read 'Views/MainWindow.xaml'
$ns = [Xml.XmlNamespaceManager]::new($window.NameTable)
$ns.AddNamespace('p','http://schemas.microsoft.com/winfx/2006/xaml/presentation'); $ns.AddNamespace('x','http://schemas.microsoft.com/winfx/2006/xaml')
$row = $window.SelectSingleNode('//p:Grid[@x:Name="SettingsAppearanceLanguageRow"]',$ns)
$card = $window.SelectSingleNode('//p:Border[@x:Name="SettingsLanguageCard"]',$ns)
$combo = $window.SelectSingleNode('//p:ComboBox[@x:Name="LanguageSelector"]',$ns)
Check ($card.ParentNode -eq $row -and $card.GetAttribute('Grid.Column') -eq '1') 'Language to the right of Appearance'
Check ($combo.GetAttribute('ItemsSource') -eq '{Binding LanguageChoices}' -and $combo.GetAttribute('DisplayMemberPath') -eq 'Name') 'Expandable central ComboBox, no culture codes shown'
Check ($combo.GetAttribute('SelectedItem').Contains('Mode=TwoWay') -and !$combo.HasAttribute('IsTabStop')) 'Immediate selection with normal keyboard access'
[xml]$proj = Read 'WinSereno.csproj'
Check ((Read 'Properties/AssemblyInfo.cs').Contains('AssemblyInformationalVersion("1.1.0")')) 'Current stable public version'
Check ((Read 'WinSereno.csproj').Contains('<ApplicationIcon>Assets\Brand\WinSereno.ico</ApplicationIcon>')) 'Application icon unchanged'
Check ((Read 'WinSereno.csproj').Contains('Localization\Strings.*.xaml')) 'Both dictionaries included as native WPF resources'
Check ((Read 'WinSereno.csproj') -notmatch 'PackageReference') 'No dependencies added'
Write-Output "$count checks passed: catalog parity, nonempty translations, central resources, persistence and UI wiring."
