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
Check ($csproj.SelectSingleNode('//p:ApplicationIcon', $ns).InnerText -eq 'Assets\Brand\WinSereno.ico') 'Official EXE icon unchanged'
Check (@($csproj.SelectNodes('//p:Resource', $ns) | Where-Object { $_.GetAttribute('Include') -match '\.(png|svg)$' }).Count -eq 0) 'No raster or SVG branding embedded for UI'
$references = @($csproj.SelectNodes('//p:Reference', $ns) | ForEach-Object { $_.GetAttribute('Include') })
Check (($references -join '|') -eq 'System|System.Core|System.Runtime.Serialization|System.Xml|System.ServiceProcess|System.Management|System.Xaml|WindowsBase|PresentationCore|PresentationFramework') 'Only existing framework references; no SVG library/dependency'
Check ($csproj.SelectNodes('//p:PackageReference', $ns).Count -eq 0) 'No new NuGet packages'
$branding = Read 'Themes/Branding.xaml'
[xml]$valid = $branding
Check ($branding -notmatch 'BitmapImage|ImageBrush|OpacityMask|\.png|\.svg|UriSource|x:Static|xmlns:local') 'Branding is pure native geometry without masks, bitmaps or SVG runtime'
Check ($branding -notmatch 'file:|[A-Za-z]:\\|(?:Source|UriSource)="(?:https?:|/)') 'No absolute resource paths'
foreach ($key in @('WinSerenoMarkPrimaryGeometry', 'WinSerenoMarkAccentGeometry', 'WinSerenoWinGeometry', 'WinSerenoSerenoGeometry', 'BrandLogoImage', 'BrandMarkImage')) {
    Check ($branding.Contains('x:Key="' + $key + '"')) "Central reusable resource: $key"
}
foreach ($brush in @('BrandPrimaryBrush', 'BrandAccentBrush', 'BrandWordmarkBrush')) {
    Check ($branding.Contains('{DynamicResource ' + $brush + '}')) "Geometry uses theme brush: $brush"
}
foreach ($theme in @('Light', 'Dark')) {
    $text = Read "Themes/$theme.xaml"
    [xml]$valid = $text
    Check ($text.Contains('Source="/WinSereno;component/Themes/Branding.xaml"')) "$theme merges shared vector resources"
    foreach ($key in @('BrandPrimaryBrush', 'BrandAccentBrush', 'BrandWordmarkBrush')) {
        Check ($text.Contains('x:Key="' + $key + '"')) "$theme defines $key"
    }
    Check ($text -notmatch 'BitmapImage|ImageBrush|OpacityMask|\.png|x:Static|xmlns:local') "$theme has no raster branding or local XAML dependency"
}
$main = Read 'Views/MainWindow.xaml'
[xml]$valid = $main
Check ($main.Contains('Source="{DynamicResource BrandLogoImage}"') -and $main.Contains('Source="{DynamicResource BrandMarkImage}"')) 'Logo and About follow the effective theme dynamically'
Check ($main.Contains('x:Name="SidebarBrandLogo"') -and $main.Contains('AutomationProperties.Name="WinSereno"')) 'Sidebar branding keeps an accessible product name'
Check ($main.Contains('x:Name="AboutBrandMark"') -and $main.Contains('GNU GPLv3') -and $main.Contains('Binding ProductVersion')) 'About retains centralized version and adds GPLv3 identity'
Check ($main.Contains('OpenGitHubCommand') -and $main.Contains('OpenReleasesCommand')) 'About retains existing fixed-link commands'
Check (!$main.Contains('x:Static') -and !$main.Contains('xmlns:local')) 'No local XAML dependency requiring MCPass2'
Check ($main -notmatch '\.png|\.svg' -and $main.Contains('Stretch="Uniform"')) 'UI retains proportional vector presentation without raster references'
# Parse native WPF resources only: no WinSereno assembly, window, command or system operation.
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Xaml
$vectors = [Windows.Markup.XamlReader]::Parse($branding)
Check ($vectors['BrandLogoImage'] -is [Windows.Media.DrawingImage] -and $vectors['BrandMarkImage'] -is [Windows.Media.DrawingImage]) 'Both image sources are vector drawings'
Check ($vectors['BrandLogoImage'].Width -eq 2172 -and $vectors['BrandLogoImage'].Height -eq 724) 'Horizontal canvas keeps 3:1 proportions and contains all paths'
Check ($vectors['BrandMarkImage'].Width -eq 724 -and $vectors['BrandMarkImage'].Height -eq 724) 'Mark has reusable square canvas without clipping'
Check ($vectors['WinSerenoMarkPrimaryGeometry'].GetArea() -gt 90000 -and $vectors['WinSerenoMarkAccentGeometry'].GetArea() -gt 35000) 'Both main masses and central band have meaningful geometry'
foreach ($point in @([Windows.Point]::new(1415,390), [Windows.Point]::new(1688,390), [Windows.Point]::new(2021,413))) {
    Check (!$vectors['WinSerenoSerenoGeometry'].FillContains($point)) "Letter counter remains open at $point"
}
foreach ($file in @('WinSereno-Mark-Color.png','WinSereno-Logo-Horizontal-Dark.png','WinSereno-Logo-Horizontal-Light.png')) {
    Check (Test-Path -LiteralPath (Join-Path $project "Assets/Brand/$file")) "Historical PNG retained: $file"
}
Write-Output "$count comprobaciones estaticas de branding correctas; geometria nativa, sin ejecutar acciones reales."
