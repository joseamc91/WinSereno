# Read canonical ES text for pre-localization structural assertions. The localization
# suite separately validates actual resource references and ES/EN presentation.
function Read-LocalizedSource([string]$path) {
    [xml]$catalog = [IO.File]::ReadAllText((Join-Path $project 'Localization/Strings.es.xaml'))
    $values = @{}
    foreach ($node in $catalog.DocumentElement.ChildNodes) {
        if ($node.NodeType -eq [Xml.XmlNodeType]::Element) { $values[$node.GetAttribute('Key','http://schemas.microsoft.com/winfx/2006/xaml')] = $node.InnerText }
    }
    $text = [IO.File]::ReadAllText((Join-Path $project $path))
    $text = [regex]::Replace($text, 'WinSereno\.Localization\.LocalizationService\.(?:Source|Current\.Get)\("(?<key>Text\.[^"]+)"\)', {
        param($m)
        if (!$values.ContainsKey($m.Groups['key'].Value)) { throw "Missing canonical key: $($m.Value)" }
        '"' + $values[$m.Groups['key'].Value].Replace('\','\\').Replace('"','\"').Replace("`r",'\r').Replace("`n",'\n') + '"'
    })
    [regex]::Replace($text, '\{DynamicResource (?<key>Text\.[^}]+)\}', {
        param($m)
        if (!$values.ContainsKey($m.Groups['key'].Value)) { throw "Missing XAML key: $($m.Value)" }
        [Security.SecurityElement]::Escape($values[$m.Groups['key'].Value])
    })
}
