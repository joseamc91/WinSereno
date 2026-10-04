param([string]$AssemblyPath, [string]$Case, [string]$PreferencesDirectory)
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
if (!$AssemblyPath) { $AssemblyPath = Join-Path $project 'bin\Debug\WinSereno.exe' }
$validation = Join-Path $project 'bin\StartupValidation'
New-Item -ItemType Directory -Path $validation -Force | Out-Null
if (!$Case) {
    # Every case is a fresh process: warmed catalogs/translated controls must not
    # hide a failure of the very first Window.Loaded with English already active.
    $scratch = Join-Path $validation ('Preferences-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $scratch | Out-Null
    $cases = @('Missing','Legacy','Invalid','es-Light','es-Dark','es-System','en-Light','en-Dark','en-System',
        'Cycle-WriteEnglish','Cycle-ReadEnglish','Cycle-WriteSpanish','Cycle-ReadSpanish')
    $total = 0
    try {
        foreach ($scenario in $cases) {
            $folder = if ($scenario.StartsWith('Cycle-')) { Join-Path $scratch 'Cycle' } else { Join-Path $scratch $scenario }
            New-Item -ItemType Directory -Path $folder -Force | Out-Null
            $previousPreference = $ErrorActionPreference
            $ErrorActionPreference = 'Continue' # Capture native stderr and exit code even in Windows PowerShell 5.1.
            try { $output = @(& powershell.exe -NoLogo -NoProfile -NonInteractive -STA -File $PSCommandPath -AssemblyPath $AssemblyPath -Case $scenario -PreferencesDirectory $folder 2>&1) }
            finally { $ErrorActionPreference = $previousPreference }
            $code = $LASTEXITCODE
            $output | Out-File (Join-Path $validation ($scenario + '.log')) -Encoding utf8
            $output | ForEach-Object { Write-Output "[$scenario] $_" }
            if ($code -ne 0) { throw "Cold startup case $scenario failed (exit $code). See bin/StartupValidation/$scenario.log." }
            $matches = [regex]::Matches(($output -join "`n"), '(?m)^(\d+) checks passed:')
            if ($matches.Count -ne 1) { throw "Missing check count for $scenario." }
            $total += [int]$matches[0].Groups[1].Value
        }
    } finally {
        $resolved = [IO.Path]::GetFullPath($scratch)
        if (!$resolved.StartsWith([IO.Path]::GetFullPath($validation) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected scratch path.' }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
    Write-Output "$total checks passed: 13 isolated cold bootstraps, persisted languages/themes, migration, live switching and direct-startup renders; no maintenance/UAC."
    exit 0
}
[void][Reflection.Assembly]::LoadFrom($AssemblyPath)
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Xaml
$source = @'
using System;
using System.IO;
using System.Xml;
using System.Windows.Markup;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinSereno.Models;
using WinSereno.Services;
using WinSereno.ViewModels;
using WinSereno.Views;
using WinSereno.Localization;

public sealed class StartupInformation : ISystemInformationService {
 public Task CollectAsync(IProgress<InformationUpdate> progress,CancellationToken token){return Task.CompletedTask;}
}
public sealed class StartupDialogs : IDialogService {
 public bool ConfirmTask(MaintenanceTask task){throw new Exception("No actions/UAC permitted");}
 public void ShowMessage(string message){throw new Exception(message);}
 public void ShowOutput(TaskProgress progress){throw new Exception("No actions permitted");}
 public void ShowDiagnosticDetails(DiagnosticResult result){throw new Exception("No actions permitted");}
 public bool ConfirmCancelAndClose(){return false;}
 public RestartAdapter SelectRestartAdapter(IReadOnlyList<RestartAdapter> adapters){throw new Exception("No network actions permitted");}
}
public sealed class StartupRunner : IMaintenanceTaskRunner {
 public bool IsActive {get{return false;}} public TaskProgress Current {get{return new TaskProgress();}}
 public event EventHandler<TaskProgress> ProgressChanged {add{}remove{}}
 public Task<MaintenanceTaskResult> RunAsync(MaintenanceTask task){throw new Exception("No maintenance permitted");}
 public bool RequestCancellation(){throw new Exception("No actions permitted");}
 public Task WaitForIdleAsync(){return Task.CompletedTask;}
}
public static class LocalizationStartupChecks {
 static int count;
 static readonly List<Exception> errors=new List<Exception>();
 static void Check(bool value,string label){if(!value)throw new Exception(label);count++;}
 static void Pump(){var frame=new DispatcherFrame();Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);}
 static IEnumerable<T> Visual<T>(DependencyObject root) where T:DependencyObject {
  if(root is T)yield return (T)root;
  for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var node in Visual<T>(VisualTreeHelper.GetChild(root,i)))yield return node;
 }
 static void Save(Window window,string name,string output){
  var root=(FrameworkElement)window.Content;var bitmap=new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth),(int)Math.Ceiling(root.ActualHeight),96,96,PixelFormats.Pbgra32);
  var background=new DrawingVisual();using(var drawing=background.RenderOpen())drawing.DrawRectangle((Brush)Application.Current.Resources["BackgroundBrush"],null,new Rect(0,0,root.ActualWidth,root.ActualHeight));
  bitmap.Render(background);bitmap.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
  using(var stream=File.Create(Path.Combine(output,name+".png")))encoder.Save(stream);
 }
 static void VerifyLanguage(MainWindow window,MainViewModel vm,string language){
  bool english=language=="en";
  Check(LocalizationService.Current.Language==language&&vm.SelectedLanguage.Code==language,"Effective persisted language");
  var labels=Visual<TextBlock>((ListBox)window.FindName("MainNavigationList")).Select(t=>t.Text).ToArray();
  Check(labels.Contains(english?"Home":"Inicio")&&labels.Contains(english?"Diagnostics":"Diagnóstico"),"Existing navigation uses effective language");
  vm.Navigate(NavigationSection.Settings);Pump();
  var page=(FrameworkElement)window.FindName("SettingsPage");
  Check(page.Visibility==Visibility.Visible,"Settings page constructed and visible");
  Check(Visual<TextBlock>(page).Any(t=>t.Text==(english?"Appearance":"Apariencia")),"Appearance is localized on first render");
  Check(Visual<TextBlock>(page).Any(t=>t.Text==(english?"Language":"Idioma")),"Language card is localized on first render");
  Check(Visual<TextBlock>(page).Any(t=>t.Text==(english?"About":"Acerca de")),"About is localized on first render");
 }
 public static string Run(string project,string scratch,string scenario){
  var storage=new PortableStorage(scratch);string expectedLanguage="es",expectedTheme="Light";
  if(scenario=="Legacy"||scenario=="Invalid"){
   expectedTheme="Dark";File.WriteAllText(storage.ConfigPath,scenario=="Legacy"?"{\"Theme\":\"Dark\"}":"{\"Theme\":\"Dark\",\"Language\":\"fr\"}");
  } else if(scenario.StartsWith("Cycle-")){
   if(scenario=="Cycle-WriteEnglish")storage.SaveSettings(new AppSettings{Theme="Light",Language="es"});
   else {expectedTheme="Dark";expectedLanguage=scenario=="Cycle-ReadSpanish"?"es":"en";}
  } else if(scenario!="Missing"){
   var pieces=scenario.Split('-');expectedLanguage=pieces[0];expectedTheme=pieces[1];storage.SaveSettings(new AppSettings{Language=expectedLanguage,Theme=expectedTheme});
  }
  string output=Path.Combine(project,"bin","StartupValidation","Renders");Directory.CreateDirectory(output);
  var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};Application.ResourceAssembly=typeof(WinSereno.App).Assembly;
  var appXaml=new XmlDocument();appXaml.Load(Path.Combine(project,"App.xaml"));
  var namespaces=new XmlNamespaceManager(appXaml.NameTable);namespaces.AddNamespace("p","http://schemas.microsoft.com/winfx/2006/xaml/presentation");
  var resources=appXaml.SelectSingleNode("/p:Application/p:Application.Resources/p:ResourceDictionary",namespaces);
  app.Resources=(ResourceDictionary)XamlReader.Parse(resources.OuterXml,new ParserContext{BaseUri=new Uri("pack://application:,,,/WinSereno;component/App.xaml")});
  app.DispatcherUnhandledException+=(s,e)=>{
   errors.Add(e.Exception);File.WriteAllText(Path.Combine(output,scenario+"-Exception.txt"),e.Exception.ToString()+"\nInner="+(e.Exception.InnerException==null?"<none>":e.Exception.InnerException.ToString()));
   // Harness only: record and fail the case rather than crashing PowerShell
   // before it can return the exception/stack. Production never marks it handled.
   e.Handled=true;
  };
  AppDomain.CurrentDomain.UnhandledException+=(s,e)=>File.WriteAllText(Path.Combine(output,scenario+"-Unhandled.txt"),e.ExceptionObject.ToString());
  var settings=storage.LoadSettings();
  Check(settings.Language==expectedLanguage&&settings.Theme==expectedTheme,"Config load/default/migration retains theme");
  LocalizationPresentation.Initialize();LocalizationService.Current.Apply(settings.Language);
  var themes=new ThemeService();themes.Apply(settings.Theme);
  if(!File.Exists(storage.ConfigPath))storage.SaveSettings(settings);
  Check(LocalizationService.Current.Language==expectedLanguage,"Correct language active BEFORE ViewModels/window");
  Check(themes.EffectiveTheme=="Light"||themes.EffectiveTheme=="Dark","Valid resolved theme before window");
  var dialogs=new StartupDialogs();var ops=new OperationCoordinator();var integrity=new IntegritySessionState();
  var home=new HomeViewModel(new StartupInformation(),null);home.Stop();
  home.Cards[0].Value="Windows 11 Pro";home.Cards[1].Value="Intel Core i7";home.Cards[2].Value="Display adapter";
  var vm=new MainViewModel(storage,settings,themes,dialogs,new StartupRunner(),home,new DiagnosticViewModel(null,ops,null,dialogs,integrity),ops,integrity,null);
  var window=new MainWindow(vm,dialogs){Left=-30000,Top=-30000,ShowInTaskbar=false};app.MainWindow=window;
  try {
   window.Show();Pump();
   if(errors.Count!=0)throw new Exception("Cold Window.Loaded failed: "+scenario,errors[0]);
   Console.WriteLine("First layout: Loaded="+window.IsLoaded+" Visible="+window.IsVisible+" Width="+((FrameworkElement)window.Content).ActualWidth);
   Check(window.IsLoaded&&window.IsVisible&&((FrameworkElement)window.Content).ActualWidth>0,"Window completed load/layout and remains alive");
   Check(vm.CurrentSection==NavigationSection.Home,"Startup preserves Home page");
   VerifyLanguage(window,vm,expectedLanguage);
   if(scenario=="en-Light"||scenario=="en-Dark"){
    vm.Navigate(NavigationSection.Home);Pump();Save(window,"Home-"+scenario,output);
    vm.Navigate(NavigationSection.Settings);Pump();Save(window,"Settings-"+scenario,output);
   }
   vm.Navigate(NavigationSection.Repair);Pump();
   var inlineLabels=Visual<TextBlock>(window).SelectMany(t=>t.Inlines.OfType<Run>()).Select(r=>r.Text).ToArray();
   Check(inlineLabels.Contains(expectedLanguage=="en"?"Check and repair":"Comprobar y reparar"),"Bound Run translated during first traversal");
   // Also exercise nested Span in a newly loaded window while language is active.
   var label=new TextBlock();var span=new Span();var run=new Run();
   run.SetBinding(System.Windows.Documents.Run.TextProperty,new System.Windows.Data.Binding("PageTitle"){Source=vm,Mode=System.Windows.Data.BindingMode.OneWay});span.Inlines.Add(run);label.Inlines.Add(span);
   var secondary=new Window{Content=label,Width=200,Height=100,Left=-30000,Top=-30000,ShowInTaskbar=false};
   secondary.Show();Pump();Check(run.Text==(expectedLanguage=="en"?"Repair":"Reparación"),"Nested inline enumeration tolerates live translation");secondary.Close();
   string other=expectedLanguage=="en"?"es":"en";
   var combo=(ComboBox)window.FindName("LanguageSelector");combo.SelectedItem=vm.LanguageChoices.Single(l=>l.Code==other);Pump();VerifyLanguage(window,vm,other);
   combo.SelectedItem=vm.LanguageChoices.Single(l=>l.Code==expectedLanguage);Pump();VerifyLanguage(window,vm,expectedLanguage);
   Check(ReferenceEquals(window.DataContext,vm)&&ReferenceEquals(vm.Home,home),"Hot switching retains existing window/ViewModels");
   Check(storage.LoadSettings().Language==expectedLanguage&&storage.LoadSettings().Theme==expectedTheme,"Hot switching preserves independent theme and persisted language");
   if(scenario=="Cycle-WriteEnglish"||scenario=="Cycle-WriteSpanish"){
    string next=scenario=="Cycle-WriteEnglish"?"en":"es";combo.SelectedItem=vm.LanguageChoices.Single(l=>l.Code==next);vm.SelectedTheme="Oscuro";Pump();
    var saved=storage.LoadSettings();Check(saved.Language==next&&saved.Theme=="Dark","Real selectors persisted next fresh-process startup");
   }
   Check(errors.Count==0,"No unhandled dispatcher errors during startup/live switching");
  } finally {window.Close();home.Stop();}
  return count+" checks passed: cold startup "+scenario+"; localized first layout and restart preferences.";
 }
}
'@
$provider = [Microsoft.CSharp.CSharpCodeProvider]::new()
$parameters = [CodeDom.Compiler.CompilerParameters]::new()
$parameters.GenerateInMemory = $true
$parameters.ReferencedAssemblies.AddRange(@('System.dll','System.Core.dll','System.Xml.dll',$AssemblyPath))
foreach ($assembly in @([System.Windows.Window],[System.Windows.Media.Brush],[System.Windows.Threading.Dispatcher],[System.Xaml.XamlReader])) { [void]$parameters.ReferencedAssemblies.Add($assembly.Assembly.Location) }
$compiled = $provider.CompileAssemblyFromSource($parameters,$source)
if ($compiled.Errors.HasErrors) { $compiled.Errors | ForEach-Object { Write-Error $_ }; exit 1 }
try { [LocalizationStartupChecks]::Run($project,$PreferencesDirectory,$Case) } catch { Write-Output $_.Exception.ToString(); throw }
