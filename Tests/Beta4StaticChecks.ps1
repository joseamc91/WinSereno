$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$script:count = 0
function Check([bool]$condition, [string]$label) {
    if (!$condition) { throw $label }
    $script:count++
}
function Read([string]$path) { [IO.File]::ReadAllText((Join-Path $project $path)) }
$main = Read 'Views/MainWindow.xaml'
$styles = Read 'Themes/Styles.xaml'
$vm = Read 'ViewModels/MainViewModel.cs'
$shell = Read 'Services/ApplicationShellService.cs'
$theme = Read 'Services/ThemeService.cs'
$dialogs = Read 'Views/DialogService.cs'
$assembly = Read 'Properties/AssemblyInfo.cs'
foreach ($path in @('Views/MainWindow.xaml', 'Themes/Styles.xaml', 'Themes/Light.xaml', 'Themes/Dark.xaml')) {
    [xml]$xml = Read $path
    Check ($null -ne $xml.DocumentElement) "XML valid: $path"
}
foreach ($name in @('Home','Diagnosis','Repair','Network','Cleanup','Activity','Settings')) {
    Check ($styles.Contains('x:Key="Navigation'+$name+'Icon"')) "Vector icon $name"
    Check ($main.Contains('Resource Navigation'+$name+'Icon')) "Icon binding $name"
}
Check (([regex]::Matches($styles, '<Geometry x:Key="Navigation')).Count -eq 7) 'Exactly seven vector resources'
$navigationTemplate = [regex]::Match($main, '(?s)<DataTemplate x:Key="SidebarNavigationTemplate">.*?</DataTemplate>').Value
Check ($navigationTemplate.Length -gt 0 -and !$navigationTemplate.Contains('<Image') -and !$styles.Contains('<Image')) 'Navigation icons remain vector-only; branding images are separate'
Check ($main.Contains('StrokeThickness="1.6"') -and $main.Contains('Width="18" Height="18"')) 'Consistent icon scale'
Check ($main.Contains('Value="{DynamicResource AccentBrush}"') -and $main.Contains('Property="Stroke" Value="{DynamicResource MutedBrush}"')) 'Adaptive icon colors'
Check ($main.Contains('ItemsSource="{Binding MainNavigation}"') -and $main.Contains('ItemsSource="{Binding SettingsNavigation}"')) 'Separate navigation groups'
Check ($main.Contains('SelectedItem="{Binding SelectedMainNavigation, Mode=TwoWay}"') -and $main.Contains('SelectedItem="{Binding SelectedSettingsNavigation, Mode=TwoWay}"')) 'Each selector binds only to its own group'
Check (!$main.Contains('SelectedItem="{Binding SelectedNavigation}"')) 'No shared SelectedItem binding between disjoint selectors'
Check ($vm.Contains('MainNavigation.Contains(SelectedNavigation) ? SelectedNavigation : null') -and $vm.Contains('SettingsNavigation.Contains(SelectedNavigation) ? SelectedNavigation : null')) 'Group selection derives from one active navigation item'
Check ($vm.Contains('Raise(nameof(SelectedMainNavigation)); Raise(nameof(SelectedSettingsNavigation));')) 'Both selection projections update when navigation changes'
Check (([regex]::Matches($main, 'KeyDown="OnNavigationKeyDown"')).Count -eq 2) 'Enter activation applies to both navigation groups'
Check ((Read 'Views/MainWindow.xaml.cs').Contains('viewModel.NavigateCommand.Execute(navigation.Section);')) 'Enter uses the existing navigation command'
Check ($main.Contains('Text="{Binding Label}"')) 'Labels retained with icons'
Check ($main.Contains('Content="GitHub ↗"') -and $main.Contains('Command="{Binding OpenGitHubCommand}"')) 'Sidebar fixed GitHub command'
Check ($vm.Contains('public string PageNotice => "";')) 'No redundant notice'
Check ($vm.Contains('Personaliza la apariencia y consulta la configuración de WinSereno.')) 'Settings description'
Check ($vm.Contains('Consulta las acciones realizadas durante esta sesión; los logs TXT conservan el registro persistente.')) 'Activity description'
Check ($styles.Contains('<Setter Property="FontSize" Value="13"/>')) 'Common button font'
Check (!$main.Contains('FontSize="12" Padding') -and !(Read 'Views/DiagnosticCard.xaml').Contains('FontSize="12"')) 'No obsolete compact button font override'
foreach ($key in @('PrimaryBackgroundBrush','PrimaryForegroundBrush','PrimaryBorderBrush','PrimaryHoverBrush','PrimaryPressedBrush','NavigationHoverBrush','NavigationSelectedBrush')) {
    foreach ($value in @('Light','Dark')) { Check ((Read ('Themes/'+$value+'.xaml')).Contains('x:Key="'+$key+'"')) "$key in $value" }
}
Check ($styles.Contains('Property="IsPressed"') -and $styles.Contains('Value="0.45"')) 'Pressed and disabled states'
Check ($styles.Contains('Property="IsKeyboardFocusWithin"') -and $styles.Contains('Property="IsKeyboardFocused"')) 'Keyboard focus retained'
Check ($shell.Contains('https://github.com/joseamc91/WinSereno') -and $shell.Contains('GitHubUrl + "/releases"')) 'Fixed public URLs'
Check (!$shell.Contains('Arguments =') -and !$shell.Contains('runas')) 'No arbitrary shell arguments or elevation'
Check ($shell.Contains('UseShellExecute = true') -and $shell.Contains('Verb = "open"')) 'Default shell opening'
Check ($theme.Contains('AppsUseLightTheme') -and $theme.Contains('Registry.GetValue')) 'Read-only Windows theme source'
Check (!$theme.Contains('SetValue') -and !$theme.Contains('CreateSubKey')) 'No registry writes'
Check ((Read 'Services/PortableStorage.cs').Contains('"System"')) 'System config supported'
Check ($vm.Contains('new AppSettings().Theme') -and $vm.Contains('ConfirmResetPreferences')) 'Confirmed reset to existing defaults'
Check ($dialogs.Contains('Los logs y los datos del sistema no se modificarán.') -and $dialogs.Contains('Content = "Restablecer"')) 'Explicit reset confirmation'
Check (!$shell.Contains('File.Delete') -and !$vm.Contains('Directory.Delete')) 'Preferences/folder links never delete data'
Check ($assembly.Contains('AssemblyVersion("0.1.0.0")') -and $assembly.Contains('AssemblyFileVersion("0.1.0.0")')) 'Technical versions unchanged'
Check ($assembly.Contains('AssemblyInformationalVersion("0.1.0-beta.5')) 'Central Beta4 public version'
Check (!$main.Contains('Portable · Acciones explícitas')) 'Obsolete sidebar tagline absent'
Check (!(Read 'WinSereno.csproj').Contains('PackageReference')) 'No NuGet icon dependencies'
Check (!$main.Contains('x:Static') -and !$main.Contains('xmlns:local')) 'No local XAML second-pass references'
Check ($main.Contains('x:Name="SettingsDataPreferencesRow"') -and $main.Contains('<ColumnDefinition Width="55*"/><ColumnDefinition Width="45*"/>')) 'Settings data/preferences share two adaptive columns'
Check ($main.Contains('x:Name="SettingsDataCard" Style="{StaticResource Card}"') -and $main.Contains('x:Name="SettingsPreferencesCard" Grid.Column="1" Style="{StaticResource Card}"')) 'Two independent Settings cards keep the existing style'
Check (([regex]::Matches($main,'ItemTemplate="\{StaticResource DiskCardTemplate\}"')).Count -eq 2) 'Local and external disks share exactly the same card template'
Check ($main.Contains('ItemsSource="{Binding Home.LocalDisks}"') -and $main.Contains('ItemsSource="{Binding Home.ExternalDisks}"')) 'Separate disk collections without duplicate reads'
Check ($main.Contains('x:Name="ExternalDisksSection" Visibility="{Binding Home.HasExternalDisks, Converter={StaticResource BoolVisibility}}"')) 'External section entirely collapses when empty'
$disks = Read 'Services/SystemInformationService.cs'
Check ($disks.Contains('DriveType = driveType') -and $disks.Contains('!drive.IsReady') -and $disks.Contains('if (total <= 0) continue;')) 'DriveInfo classification preserves ready/valid volume guards'
Check (!(Read 'ViewModels/HomeViewModel.cs').Contains('public ObservableCollection<DiskViewModel> Disks ')) 'No ambiguous mixed Home disk collection remains'
Write-Output "$script:count comprobaciones estáticas Beta4 correctas; sin ejecutar aplicación, enlaces ni operaciones."
