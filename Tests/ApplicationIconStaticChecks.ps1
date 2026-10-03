$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$count = 0
function Check([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
    $script:count++
}
[xml]$document = [IO.File]::ReadAllText((Join-Path $project 'WinSereno.csproj'))
$ns = [Xml.XmlNamespaceManager]::new($document.NameTable)
$ns.AddNamespace('p', 'http://schemas.microsoft.com/developer/msbuild/2003')
$icons = @($document.SelectNodes('/p:Project/p:PropertyGroup/p:ApplicationIcon', $ns))
Check ($icons.Count -eq 1) 'Exactly one application icon configuration'
$relative = $icons[0].InnerText
Check ($relative -ceq 'Assets\Brand\WinSereno.ico') 'Official icon configured for all build configurations'
Check (-not [IO.Path]::IsPathRooted($relative)) 'Icon path is relative to the project'
Check (-not $icons[0].HasAttribute('Condition') -and -not $icons[0].ParentNode.HasAttribute('Condition')) 'Debug and Release share the same icon'
$icon = Join-Path $project $relative
Check (Test-Path -LiteralPath $icon -PathType Leaf) 'Official icon exists'
Check ((Get-FileHash -LiteralPath $icon -Algorithm SHA256).Hash -ceq 'E960030926C69707F77EFF1457A74FC5E3858D2552BA1AA9627F06B3E785A09D') 'User-prepared ICO bytes preserved'
$iconFiles = @(Get-ChildItem -LiteralPath (Join-Path $project 'Assets') -Recurse -File -Filter '*.ico')
Check ($iconFiles.Count -eq 1) 'No duplicated ICO asset'
$sources = @(Get-ChildItem -LiteralPath (Join-Path $project 'Views') -File | Where-Object { $_.Extension -in @('.xaml', '.cs') })
$windowOverrides = @($sources | Select-String -Pattern '\bIcon\s*=|Property\s*=\s*"Icon"')
Check ($windowOverrides.Count -eq 0) 'Windows retain native WPF inheritance from the assembly icon'
Write-Output "$count comprobaciones estáticas de icono correctas; sin ejecutar WinSereno."
