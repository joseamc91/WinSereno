$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$count = 0
function Check([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
    $script:count++
}
function Read([string]$path) { [IO.File]::ReadAllText((Join-Path $project $path)) }
[xml]$csproj = Read 'WinSereno.csproj'
$ns = [Xml.XmlNamespaceManager]::new($csproj.NameTable)
$ns.AddNamespace('p', 'http://schemas.microsoft.com/developer/msbuild/2003')
$resources = @($csproj.SelectNodes('//p:Resource', $ns) | ForEach-Object { $_.GetAttribute('Include') })
foreach ($asset in @('WinSereno-Logo-Horizontal-Dark.png', 'WinSereno-Mark-Color.png')) {
    $relative = 'Assets\Brand\' + $asset
    Check ($resources -contains $relative) "Embedded official PNG: $asset"
    Check (Test-Path -LiteralPath (Join-Path $project $relative) -PathType Leaf) "PNG exists: $asset"
}
foreach ($theme in @('Light', 'Dark')) {
    $text = Read "Themes/$theme.xaml"
    [xml]$valid = $text
    foreach ($key in @('BrandLogoImage', 'BrandMarkImage')) {
        Check ($text.Contains('x:Key="' + $key + '"')) "$theme defines $key"
    }
    Check (!$text.Contains('x:Static') -and !$text.Contains('xmlns:local')) "$theme uses native WPF branding resources"
}
$light = Read 'Themes/Light.xaml'
$dark = Read 'Themes/Dark.xaml'
Check ($light.Contains('<BitmapImage x:Key="BrandLogoImage"') -and $light.Contains('WinSereno-Logo-Horizontal-Dark.png')) 'Light uses the official dark logo'
Check ($dark.Contains('DrawingGroup.OpacityMask') -and $dark.Contains('Brush="#F7F9FA"')) 'Dark renders a clean soft-white official silhouette'
$main = Read 'Views/MainWindow.xaml'
[xml]$valid = $main
Check ($main.Contains('Source="{DynamicResource BrandLogoImage}"') -and $main.Contains('Source="{DynamicResource BrandMarkImage}"')) 'Logo and About follow the effective theme dynamically'
Check ($main.Contains('x:Name="SidebarBrandLogo"') -and $main.Contains('AutomationProperties.Name="WinSereno"')) 'Sidebar branding keeps an accessible product name'
Check ($main.Contains('x:Name="AboutBrandMark"') -and $main.Contains('GNU GPLv3') -and $main.Contains('Binding ProductVersion')) 'About retains centralized version and adds GPLv3 identity'
Check ($main.Contains('OpenGitHubCommand') -and $main.Contains('OpenReleasesCommand')) 'About retains existing fixed-link commands'
Check (!$main.Contains('x:Static') -and !$main.Contains('xmlns:local')) 'No local XAML dependency requiring MCPass2'
Write-Output "$count comprobaciones estaticas de branding correctas; sin ejecutar acciones reales."
