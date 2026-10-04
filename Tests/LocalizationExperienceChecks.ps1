param([switch]$CompileOnly)
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
$exe=Join-Path $project 'bin\Debug\WinSereno.exe'
if (!$CompileOnly) { [void][Reflection.Assembly]::LoadFrom($exe) }
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Xaml
$source=@'

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinSereno.Models;
using WinSereno.Services;
using WinSereno.ViewModels;
using WinSereno.Views;

public sealed class LayoutDiskService : ISystemInformationService {
 public DiskCollection Data; public int Calls;
 public Task CollectAsync(IProgress<InformationUpdate> progress,CancellationToken token) {
  Calls++; progress.Report(new InformationUpdate {Block=InformationBlock.Disks,Status=InformationStatus.Available,Data=Data});
  return Task.CompletedTask;
 }
}
public sealed class LayoutDialogs : IDialogService, IPreferencesDialogs {
 public bool AcceptReset; public bool ConfirmResetPreferences(){return AcceptReset;}
 public bool ConfirmTask(MaintenanceTask t){throw new Exception("No UAC or tasks permitted");}
 public void ShowMessage(string text){throw new Exception(text);}
 public void ShowOutput(TaskProgress p){throw new Exception("No real actions");}
 public void ShowDiagnosticDetails(DiagnosticResult r){throw new Exception("No real actions");}
 public bool ConfirmCancelAndClose(){return false;}
 public RestartAdapter SelectRestartAdapter(IReadOnlyList<RestartAdapter> list){throw new Exception("No network actions");}
}
public sealed class LayoutRunner : IMaintenanceTaskRunner {
 public bool IsActive {get{return false;}} public TaskProgress Current {get{return new TaskProgress();}}
 public event EventHandler<TaskProgress> ProgressChanged {add{}remove{}}
 public Task<MaintenanceTaskResult> RunAsync(MaintenanceTask task){throw new Exception("No maintenance permitted");}
 public bool RequestCancellation(){throw new Exception("No real actions");} public Task WaitForIdleAsync(){return Task.CompletedTask;}
}
public static class LocalizationExperienceChecks {
 static int count;
 static void Check(bool condition,string label){if(!condition)throw new Exception(label);count++;}
 static IEnumerable<T> Visual<T>(DependencyObject root) where T:DependencyObject {
  if(root is T)yield return (T)root;
  for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var value in Visual<T>(VisualTreeHelper.GetChild(root,i)))yield return value;
 }
 static void Pump(){var frame=new DispatcherFrame();Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);}
 static void Apply(HomeViewModel home,DiskCollection data) {
  typeof(HomeViewModel).GetMethod("ApplyUpdate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(home,new object[]{new InformationUpdate {Block=InformationBlock.Disks,Status=data==null?InformationStatus.Failed:InformationStatus.Available,Data=data}});
 }
 static DiskInformation Disk(string unit,DriveType type) {
  return new DiskInformation {Unit=unit,DriveType=type,Label=type==DriveType.Fixed?"Windows":"USB",TotalBytes=128L*1024*1024*1024,FreeBytes=64L*1024*1024*1024};
 }
 static DiskCollection Disks(int externalCount) {
  var data=new DiskCollection(); data.Volumes.Add(Disk("C:",DriveType.Fixed));
  for(int i=externalCount-1;i>=0;i--)data.Volumes.Add(Disk(((char)('F'+i))+":",DriveType.Removable));
  return data;
 }
 static MainViewModel ViewModel(string project,HomeViewModel home,LayoutDialogs dialogs,string theme) {
  string[] values={"Windows 11 Pro","Intel Core i7-10700K","NVIDIA GeForce RTX 3070 Ti","32 GB","Ethernet","2 días 4 h"};
  string[] descriptions={"25H2 · x64","8 núcleos · 16 hilos · 3.8 GHz\nIntel UHD Graphics 630","Controlador 32.0.16.1692","DDR4 · 2 × 16 GB · 2933 MT/s","1 Gbps · IPv4 192.168.0.200","Desde el último arranque"};
  for(int i=0;i<6;i++){home.Cards[i].Value=values[i];home.Cards[i].Description=descriptions[i];}
  var ops=new OperationCoordinator();var integrity=new IntegritySessionState();
  new PortableStorage(project).SaveSettings(new AppSettings {Theme=theme});
  return new MainViewModel(new PortableStorage(project),new AppSettings {Theme=theme},new ThemeService(),dialogs,new LayoutRunner(),home,
   new DiagnosticViewModel(null,ops,null,dialogs,integrity),ops,integrity,null);
 }
 static void Save(FrameworkElement root,string path) {
  var bitmap=new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth),(int)Math.Ceiling(root.ActualHeight),96,96,PixelFormats.Pbgra32);
  var background=new DrawingVisual();using(var drawing=background.RenderOpen())drawing.DrawRectangle((Brush)Application.Current.Resources["BackgroundBrush"],null,new Rect(0,0,root.ActualWidth,root.ActualHeight));bitmap.Render(background);bitmap.Render(root);
  var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(path))encoder.Save(stream);
 }
 static void CheckButtons(Border card,string label) {
  foreach(var button in Visual<Button>(card)) {
   var p=button.TransformToAncestor(card).Transform(new Point());
   Check(p.X>=card.Padding.Left&&p.X+button.ActualWidth<=card.ActualWidth-card.Padding.Right+1,"Button fits its independent card: "+label);
   Check(button.ActualWidth>=button.DesiredSize.Width-button.Margin.Left-button.Margin.Right-1,"Button retains its desired text width: "+label);
   Check(button.FontSize==13,"No font reduction: "+label);
  }
 }

 public static string Run(string project) {
  var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};Application.ResourceAssembly=typeof(WinSereno.App).Assembly;
  app.Resources.MergedDictionaries.Add(new ResourceDictionary {Source=new Uri("/WinSereno;component/Themes/Styles.xaml",UriKind.Relative)});
  var loc=WinSereno.Localization.LocalizationService.Current;
  string scratch=Path.Combine(project,"bin","LocalizationValidation","Preferences-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(scratch);
  var storage=new PortableStorage(scratch);
  Check(storage.LoadSettings().Language=="es","New config defaults to Spanish");
  foreach(string json in new[]{"{\"Theme\":\"Dark\"}","{\"Theme\":\"Dark\",\"Language\":\"fr\"}"}) {
   File.WriteAllText(storage.ConfigPath,json);var loaded=storage.LoadSettings();
   Check(loaded.Language=="es"&&loaded.Theme=="Dark","Legacy/invalid language falls back without losing theme");
   Check(File.ReadAllText(storage.ConfigPath)==json,"Loading does not rewrite original config");
  }
  foreach(string language in new[]{"es","en"})foreach(string theme in new[]{"Light","Dark","System"}) {
   storage.SaveSettings(new AppSettings {Language=language,Theme=theme});var loaded=new PortableStorage(scratch).LoadSettings();
   Check(loaded.Language==language&&loaded.Theme==theme,"Independent preference round trip: "+language+theme);
  }
  var output=Path.Combine(project,"bin","LocalizationValidation","Renders");Directory.CreateDirectory(output);
  var dialogs=new LayoutDialogs();var home=new HomeViewModel(null,null);Apply(home,Disks(1));home.Stop();
  var vm=ViewModel(scratch,home,dialogs,"Light");
  var result=new MaintenanceTaskResult {ExecutionStatus=ExecutionStatus.Success,FindingStatus=FindingStatus.PartiallyCompleted,UserSummary="Windows restableció parcialmente TCP/IP, pero una o más entradas no pudieron modificarse.",StdOut="Acceso denegado.\nGlobal se restableció correctamente.\nCorrecto",StdErr="Error",StartedAt=DateTimeOffset.Now,FinishedAt=DateTimeOffset.Now};
  var progress=new TaskProgress {CurrentTask=new MaintenanceTask {Id="fixture",Name="Restablecer TCP/IP"},State=RunnerState.Completed,Result=result,StdOut=result.StdOut,StdErr=result.StdErr};
  typeof(MainViewModel).GetMethod("OnProgressChanged",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(vm,new object[]{null,progress});
  var diagnostic=new DiagnosticResult {Id="fixture",Name="Red local e Internet",Summary="No se obtuvo información suficiente para valorar la conectividad.",Status=DiagnosticStatus.Attention};
  vm.Diagnosis.Results.Add(diagnostic);
  var window=new MainWindow(vm,dialogs) {Left=-30000,Top=-30000,ShowInTaskbar=false};
  try {
   window.Show();Pump();vm.DismissTaskPanelCommand.Execute(null);
   foreach(string theme in new[]{"Light","Dark"}) {
    vm.SelectedTheme=theme=="Dark"?"Oscuro":"Claro";new ThemeService().Apply(theme);
    foreach(string language in new[]{"es","en","es","en"}) {
     vm.Navigate(NavigationSection.Settings);Pump();
     var choice=(ComboBox)window.FindName("LanguageSelector");
     choice.SelectedItem=vm.LanguageChoices.Single(l=>l.Code==language);Pump();
     Check(vm.SelectedLanguage.Code==language&&storage.LoadSettings().Language==language,"Real ComboBox selection updates and persists "+language);
     Check(storage.LoadSettings().Theme==theme&&vm.SelectedTheme==(theme=="Dark"?"Oscuro":"Claro"),"Language switch preserves theme");
     Check(vm.CurrentSection==NavigationSection.Settings&&vm.SelectedNavigation.Section==NavigationSection.Settings,"Language switch preserves selected page");
     Check(vm.ActionHistory.Count==1&&ReferenceEquals(vm.Diagnosis.Results.Last(),diagnostic)&&ReferenceEquals(vm.Progress.Result,result),"Language switch retains results and history identities");
     Check(result.StdOut=="Acceso denegado.\nGlobal se restableció correctamente.\nCorrecto"&&result.StdErr=="Error","Native output untouched");
     Check(loc.Present("8 núcleos · 16 hilos · 3.8 GHz")== (language=="en"?"8 cores · 16 threads · 3.8 GHz":"8 núcleos · 16 hilos · 3.8 GHz"),"Numeric presentation fragments translated with valid boundaries");
     Check(loc.Present("suficientesXYZ")=="suficientesXYZ","Unrelated suffixes never translated");
     string originalHardware=home.Cards[1].Value;home.Cards[1].Value="Atención";
     vm.Navigate(NavigationSection.Home);Pump();
     Check(Visual<TextBlock>(window).Any(t=>t.Text=="Atención"),"Hardware names are opaque even if identical to a translated status");
     home.Cards[1].Value=originalHardware;vm.Navigate(NavigationSection.Settings);Pump();
     string[] labels=Visual<TextBlock>((ListBox)window.FindName("MainNavigationList")).Select(t=>t.Text).ToArray();
     Check(labels.Contains(language=="en"?"Home":"Inicio")&&labels.Contains(language=="en"?"Diagnostics":"Diagnóstico"),"Navigation translated in the existing shell");
     Check(Visual<TextBlock>((StackPanel)window.FindName("SettingsPage")).Any(t=>t.Text==(language=="en"?"Appearance":"Apariencia")),"Existing settings title refreshed");
     Check(Visual<TextBlock>((StackPanel)window.FindName("SettingsPage")).Any(t=>t.Text==(language=="en"?"Language":"Idioma")),"Language card refreshed");
     Check(Visual<TextBlock>((ComboBox)window.FindName("ThemeSelector")).Any(t=>t.Text==(language=="en"?(theme=="Dark"?"Dark":"Light"):(theme=="Dark"?"Oscuro":"Claro"))),"Displayed theme translated without changing stored selection");
     Check(choice.IsTabStop&&choice.IsEnabled&&choice.Items.Count==2,"Language ComboBox retains keyboard access and both choices");
     Check(Visual<TextBlock>((StackPanel)window.FindName("SettingsPage")).Any(t=>t.Text=="1.1.0"),"Displayed version is 1.1.0");
     foreach(double width in new[]{1180.0,900.0}) {
      window.Width=width;window.UpdateLayout();Pump();
      var row=(Grid)window.FindName("SettingsAppearanceLanguageRow");var appearance=(Border)window.FindName("SettingsAppearanceCard");var card=(Border)window.FindName("SettingsLanguageCard");
      Check(Grid.GetColumn(card)==1&&ReferenceEquals(card.Parent,row)&&ReferenceEquals(appearance.Parent,row),"Separate paired Appearance/Language cards");
      var page=(StackPanel)window.FindName("SettingsPage");var a=appearance.TransformToAncestor(page).Transform(new Point());var b=card.TransformToAncestor(page).Transform(new Point());
      Check(Math.Abs(a.Y-b.Y)<1&&Math.Abs(b.X-a.X-appearance.ActualWidth-12)<1,"Paired cards retain 12px gap at "+width);
      Check(choice.ActualWidth==160&&choice.ActualWidth<=card.ActualWidth-36,"Dropdown fits without font reduction");
      CheckButtons((Border)window.FindName("SettingsDataCard"),language+theme+width);CheckButtons((Border)window.FindName("SettingsPreferencesCard"),language+theme+width);
      var scroll=(ScrollViewer)window.FindName("PageScroll");Console.WriteLine("Localization Settings "+language+theme+width+": scroll="+scroll.ScrollableHeight);
      Check(scroll.HorizontalScrollBarVisibility==ScrollBarVisibility.Disabled,"No horizontal scroll");
      Save((FrameworkElement)window.Content,Path.Combine(output,"Settings-"+language+"-"+theme+"-"+width+".png"));
     }
     window.Width=1180;
     foreach(var section in new[]{NavigationSection.Home,NavigationSection.Diagnosis,NavigationSection.Cleanup,NavigationSection.Repair,NavigationSection.Network,NavigationSection.Activity}) {
      vm.Navigate(section);window.UpdateLayout();Pump();
      Save((FrameworkElement)window.Content,Path.Combine(output,section+"-"+language+"-"+theme+".png"));
      if(section==NavigationSection.Activity)Check(Visual<TextBlock>(window).Any(t=>t.Text==(language=="en"?"Reset TCP/IP":"Restablecer TCP/IP")),"Stored activity localized without re-running its task");
      if(section==NavigationSection.Home)Check(Visual<TextBlock>(window).Any(t=>t.Text==(language=="en"?"External drives":"Unidades externas")),"Home storage labels localized");
      if(section==NavigationSection.Repair)Check(Visual<TextBlock>(window).Any(t=>t.Inlines.OfType<System.Windows.Documents.Run>().Any(r=>r.Text==(language=="en"?"Check and repair":"Comprobar y reparar"))),"Inline Run labels localized");
      if(section==NavigationSection.Repair) {
       Check(Visual<Button>(window).Any(b=>Convert.ToString(b.Content)==(language=="en"?"Check":"Comprobar")),"Individual repair Check button localized");
       Check(Visual<Button>(window).Any(b=>Convert.ToString(b.Content)==(language=="en"?"Repair":"Reparar")),"Individual repair Repair button localized");
       var absent=Visual<TextBlock>(window).Where(t=>t.Style==window.Resources["RepairSessionResultText"]).ToArray();
       Check(absent.Length>=6&&absent.All(t=>t.Visibility==Visibility.Collapsed),"Absent individual repair results remain hidden in either language");
      }
     }
     vm.Navigate(NavigationSection.Network);Pump();
     var absentNetwork=Visual<TextBlock>(window).Where(t=>t.Style==window.Resources["NetworkToolResultText"]).ToArray();
     Check(absentNetwork.Length==5&&absentNetwork.All(t=>t.Visibility==Visibility.Collapsed),"Absent network results remain hidden in either language");
     Check(absentNetwork.All(t=>Convert.ToString(t.Tag)=="Sin resultados durante esta sesión."),"Network empty-state rule uses original value, independent of rendered language");
     // Parse fixture output under either UI language; no native process or network call.
     foreach(string nativeLanguage in new[]{"es","en"}) {
      string native=File.ReadAllText(Path.Combine(project,"Tests","Fixtures","TcpIpResetPartial."+nativeLanguage+".txt"));
      var parsed=new MaintenanceTaskResult {ExitCode=1,StdOut=native};TcpIpResetResultParser.Apply(parsed);
      Check(parsed.ExecutionStatus==ExecutionStatus.Success&&parsed.FindingStatus==FindingStatus.PartiallyCompleted&&parsed.RequiresRestart,"TCP/IP parsing independent of UI language "+language+nativeLanguage);
      Check(parsed.StdOut==native&&parsed.ExitCode==1,"Parser retains exact native text and code");
     }
     var chkdsk=new MaintenanceTaskResult {ExitCode=1,StdOut="Acceso denegado porque no tiene privilegios suficientes."};typeof(MaintenanceTaskResult).GetProperty("CommandStarted").SetValue(chkdsk,true,null);ChkdskResultParser.Apply(chkdsk);
     Check(chkdsk.ExecutionStatus==ExecutionStatus.Failed&&chkdsk.FindingStatus==FindingStatus.ScanFailed,"CHKDSK localized output parsed independently of UI language");
     var eventResult=new DiagnosticResult {Name="Eventos de Windows",Status=DiagnosticStatus.NotChecked,Summary="Correcto",DetailedDescription="Almacén de componentes\nCorrecto",NativeOutputSegments=new List<string>{"Correcto"}};
     eventResult.Events.Add(new DiagnosticEvent {WindowsDescription="Correcto · Atención · Red",Interpretation="Windows registró un arranque tras un cierre no limpio. Este evento no identifica por sí solo la causa."});
     using(var details=new DiagnosticDetailsViewModel(eventResult)) {
      Check(details.Text.Contains("Correcto · Atención · Red"),"Original event description remains byte-for-byte unlocalized");
      Check(details.Text.Contains("Almacén de componentes\nCorrecto")==(language=="es"),"Generated diagnostic context translated around opaque native block");
      Check(details.Text.StartsWith(language=="en"?"NOT CHECKED · Healthy":"NO COMPROBADO · Correcto")||details.Text.StartsWith(language=="en"?"Not checked · Healthy":"No comprobado · Correcto"),"Generated summary translated even when identical to a native word");
     }
     var sequence=new MaintenanceTaskResult();
     foreach(string id in DiagnosticIntegritySequence.TaskIds) {
      var nativeStep=new MaintenanceTaskResult {ExecutionStatus=ExecutionStatus.Success,FindingStatus=FindingStatus.Healthy,StdOut="Correcto",StdErr="Error",UserSummary="Correcto",ExitCode=0};
      typeof(MaintenanceTaskResult).GetProperty("CommandStarted").SetValue(nativeStep,true,null);
      sequence.SequenceSteps.Add(new SequenceStepResult {TaskId=id,Result=nativeStep});
     }
     var integrityCard=DiagnosticIntegritySequence.ToDiagnostic(sequence);
     Check(integrityCard.NativeOutputRanges.Count==4,"Native DISM/SFC offsets recorded without changing classification");
     using(var details=new DiagnosticDetailsViewModel(integrityCard)) {
      Check(details.Text.Contains(language=="en"?"Component store · Healthy":"Almacén de componentes · Correcto"),"Generated integrity heading translated separately from repeated native status words");
      Check(details.Text.Contains("Correcto\r\nSTDERR:\r\nError"),"Original stdout/stderr blocks preserved at their exact positions");
     }
     var confirmation=new TaskConfirmationWindow(ElevatedTaskCatalog.Get(ElevatedTaskCatalog.ResetTcpIpId)) {Owner=window,Left=-30000,Top=-30000,ShowInTaskbar=false};
     try {
      confirmation.Show();Pump();
      Check(Visual<Button>(confirmation).Any(b=>Convert.ToString(b.Content)==(language=="en"?"Cancel":"Cancelar")),"Confirmation buttons follow selected language without executing a command");
      string other=language=="es"?"en":"es";
      vm.SelectedLanguage=vm.LanguageChoices.Single(l=>l.Code==other);Pump();
      Check(Visual<Button>(confirmation).Any(b=>Convert.ToString(b.Content)==(other=="en"?"Cancel":"Cancelar")),"Already created confirmation updates in the other language");
      vm.SelectedLanguage=vm.LanguageChoices.Single(l=>l.Code==language);Pump();
     } finally {confirmation.Close();}
     var warningSnapshot=new TcpIpResetSnapshot(new[]{new TcpIpAdapterConfiguration("fixture","Atención","Example network adapter",Ipv4ConfigurationMode.Manual,"192.0.2.10","255.255.255.0","192.0.2.1","192.0.2.53")},true);
     var warning=new TcpIpResetWarningWindow(warningSnapshot) {Owner=window,Left=-30000,Top=-30000,ShowInTaskbar=false};
     try {
      warning.Show();Pump();
      Check(Visual<TextBlock>(warning).Any(t=>t.Text=="Atención · Example network adapter"),"Preflight adapter identity is not translated");
      Check(!((Button)warning.FindName("ResetButton")).IsEnabled,"Second confirmation safety remains enforced in both languages");
      Check(warning.Title==(language=="en"?"Warning before resetting TCP/IP":"Advertencia antes de restablecer TCP/IP"),"Warning title localized");
     } finally {warning.Close();}
     var raw=new TaskOutputWindow(new {TaskName="Restablecer TCP/IP",Progress=progress}) {Owner=window,Left=-30000,Top=-30000,ShowInTaskbar=false};
     try {raw.Show();Pump();Check(Visual<TextBox>(raw).Any(t=>t.Text==result.StdOut)&&Visual<TextBox>(raw).Any(t=>t.Text==result.StdErr),"WPF output panes retain original Windows text");}finally{raw.Close();}
    }
   }
   // Theme System remains independent of either language.
   foreach(string language in new[]{"es","en"}) {vm.SelectedLanguage=vm.LanguageChoices.Single(l=>l.Code==language);vm.SelectedTheme="Sistema";Check(storage.LoadSettings().Language==language&&storage.LoadSettings().Theme=="System","System theme keeps selected language");}
   Directory.CreateDirectory(storage.LogsDirectory);string log=Path.Combine(storage.LogsDirectory,"retained.txt");File.WriteAllText(log,"retained");
   vm.SelectedLanguage=vm.LanguageChoices.Single(l=>l.Code=="en");dialogs.AcceptReset=false;vm.ResetPreferencesCommand.Execute(null);Check(vm.SelectedLanguage.Code=="en","Reset cancellation preserves language");
   dialogs.AcceptReset=true;vm.ResetPreferencesCommand.Execute(null);Pump();Check(vm.SelectedLanguage.Code=="es"&&storage.LoadSettings().Language=="es"&&storage.LoadSettings().Theme=="Light","Accepted UI reset restores Spanish and Light");
   Check(File.ReadAllText(log)=="retained"&&vm.ActionHistory.Count==1,"Reset does not erase logs or session data");
  } finally {window.Close();loc.Apply("es");Directory.Delete(scratch,true);}
  return count+" checks passed: live ES/EN WPF UI, preference migration/persistence, themes, opaque Windows output, parsers and controlled captures; no real maintenance/UAC.";
 }
}

'@
$provider = [Microsoft.CSharp.CSharpCodeProvider]::new()
$parameters = [CodeDom.Compiler.CompilerParameters]::new()
$parameters.GenerateInMemory = $true
foreach ($reference in @($exe,'System.dll','System.Core.dll','System.Xaml.dll','System.Runtime.Serialization.dll',
    [System.Windows.Application].Assembly.Location,[System.Windows.Media.Visual].Assembly.Location,[System.Windows.DependencyObject].Assembly.Location)) {
    [void]$parameters.ReferencedAssemblies.Add($reference)
}
$compiled = $provider.CompileAssemblyFromSource($parameters,$source)
if ($compiled.Errors.HasErrors) { throw ($compiled.Errors | Out-String) }
if (!$CompileOnly) { [LocalizationExperienceChecks]::Run($project) } else { Write-Output "Localization harness compiled; runtime not executed." }
