$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $project 'bin\Debug\WinSereno.exe'
[void][Reflection.Assembly]::LoadFrom($exe)
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Xaml
$source = @'
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
public sealed class LayoutDialogs : IDialogService {
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
public static class HomeSettingsLayoutChecks {
 static int count;
 static void Check(bool condition,string label){if(!condition)throw new Exception(label);count++;}
 static IEnumerable<T> Visual<T>(DependencyObject root) where T:DependencyObject {
  if(root is T)yield return (T)root;
  for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var value in Visual<T>(VisualTreeHelper.GetChild(root,i)))yield return value;
 }
 static void Pump(){Application.Current.Dispatcher.Invoke(new Action(()=>{}),DispatcherPriority.Background);}
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
  string[] values={"Windows 11 Pro","Intel Core i7-10700K","NVIDIA GeForce RTX 3070 Ti","32 GB","Ethernet","2 días"};
  string[] descriptions={"25H2 · x64","8 núcleos · 16 hilos · 3.8 GHz\nIntel UHD Graphics 630","Controlador 32.0.16.1692","DDR4 · 2 × 16 GB · 2933 MT/s","1 Gbps · IPv4 192.168.0.200","Desde el último arranque"};
  for(int i=0;i<6;i++){home.Cards[i].Value=values[i];home.Cards[i].Description=descriptions[i];}
  var ops=new OperationCoordinator();var integrity=new IntegritySessionState();
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
  var home=new HomeViewModel(null,null);var mixed=Disks(3);mixed.Volumes.Insert(0,Disk("D:",DriveType.Fixed));
  foreach(var excluded in new[]{DriveType.Network,DriveType.CDRom,DriveType.Ram,DriveType.NoRootDirectory,DriveType.Unknown})mixed.Volumes.Add(Disk("Z:",excluded));
  Apply(home,mixed);
  Check(home.LocalDisks.Select(d=>d.Name).SequenceEqual(new[]{"C: · Windows","D: · Windows"}),"Fixed volumes only, ordered by drive letter");
  Check(home.ExternalDisks.Select(d=>d.Name).SequenceEqual(new[]{"F: · USB","G: · USB","H: · USB"}),"Every removable volume ordered independently");
  Check(home.LocalDisks.Count+home.ExternalDisks.Count==5&&!home.LocalDisks.Intersect(home.ExternalDisks).Any(),"No excluded drive types or duplicated card objects");
  Check(home.HasExternalDisks&&!home.HasDisksMessage&&!home.HasExternalDisksMessage,"Ready mixed result, external section visible");
  Check(home.ExternalDisks.All(d=>d.CapacityText==home.LocalDisks[0].CapacityText&&d.FreeText==home.LocalDisks[0].FreeText&&d.StatusText==home.LocalDisks[0].StatusText),"Same capacity, free space and status formatting");
  mixed.HasErrors=true;Apply(home,mixed);
  Check(home.DisksMessage=="Algunas unidades no se pudieron consultar"&&home.ExternalDisksMessage==home.DisksMessage,"Partial query retains both groups with conservative warning");
  Apply(home,new DiskCollection());Check(home.LocalDisks.Count==0&&home.DisksMessage=="No hay volúmenes locales listos"&&!home.HasExternalDisks,"Empty result retains local message, hides external section");
  Apply(home,Disks(1));Apply(home,null);Check(home.LocalDisks.Count==0&&home.ExternalDisks.Count==0&&home.DisksMessage=="No se pudo consultar"&&!home.HasExternalDisks,"Failed query clears stale cards without external placeholder");
  Apply(home,new DiskCollection {HasErrors=true});Check(home.HasDisksMessage&&!home.HasExternalDisks&&!home.HasExternalDisksMessage,"Partial result without any external volume does not add an empty section");
  var service=new LayoutDiskService();var refreshed=new HomeViewModel(service,null);var notifications=new List<string>();refreshed.PropertyChanged+=(s,e)=>notifications.Add(e.PropertyName);
  var previous=SynchronizationContext.Current;SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
  try {
   foreach(int n in new[]{0,1,3,0}) {
    service.Data=Disks(n);refreshed.RefreshAsync().GetAwaiter().GetResult();Pump();
    Check(refreshed.LocalDisks.Count==1&&refreshed.ExternalDisks.Count==n&&refreshed.HasExternalDisks==(n>0),"Refresh simulates removable attach/detach: "+n);
    Check(!refreshed.IsRefreshing&&!refreshed.HasExternalDisksMessage,"Refresh completed with reliable state: "+n);
   }
  } finally {SynchronizationContext.SetSynchronizationContext(previous);refreshed.Stop();}
  Check(service.Calls==4,"Exactly one collection call per refresh, not one per disk group");
  Check(notifications.Count(n=>n=="HasExternalDisks")>=8,"External section visibility notified during reset and result");
  var output=Path.Combine(project,"bin","Debug","VisualChecks");Directory.CreateDirectory(output);var dialogs=new LayoutDialogs();
  foreach(string theme in new[]{"Light","Dark"}) {
   new ThemeService().Apply(theme);
   var settingsHome=new HomeViewModel(null,null);settingsHome.Stop();var vm=ViewModel(project,settingsHome,dialogs,theme);vm.Navigate(NavigationSection.Settings);
   var window=new MainWindow(vm,dialogs){Left=-30000,Top=-30000,ShowInTaskbar=false};
   try {
    window.Show();Pump();
    foreach(double width in new[]{1180.0,900.0}) {
     window.Width=width;window.UpdateLayout();Pump();
     var row=(Grid)window.FindName("SettingsDataPreferencesRow");var data=(Border)window.FindName("SettingsDataCard");var preferences=(Border)window.FindName("SettingsPreferencesCard");var page=(StackPanel)window.FindName("SettingsPage");
     Check(row.ColumnDefinitions.Count==2&&row.ColumnDefinitions[0].Width==new GridLength(55,GridUnitType.Star)&&row.ColumnDefinitions[1].Width==new GridLength(45,GridUnitType.Star),"Adaptive 55/45 settings columns: "+theme+width);
     Check(ReferenceEquals(data.Parent,row)&&ReferenceEquals(preferences.Parent,row)&&Grid.GetColumn(preferences)==1,"Two separate sibling cards in the same row");
     var a=data.TransformToAncestor(page).Transform(new Point());var b=preferences.TransformToAncestor(page).Transform(new Point());
     Check(Math.Abs(a.Y-b.Y)<0.1&&Math.Abs(data.ActualHeight-preferences.ActualHeight)<0.1,"Independent cards have equal top and height: "+theme+width);
     Check(Math.Abs(b.X-a.X-data.ActualWidth-12)<0.1,"Consistent 12 px gap without additional outer margin");
     Check(ReferenceEquals(data.Style,preferences.Style)&&data.CornerRadius==preferences.CornerRadius&&data.Padding==new Thickness(18),"Unchanged Card style and padding");
     Check(page.Children.Count==3&&ReferenceEquals(page.Children[1],row)&&page.Children[0] is Border&&page.Children[2] is Border,"Full-width Appearance, parallel cards, full-width About");
     CheckButtons(data,theme+width);CheckButtons(preferences,theme+width);
     var scroll=(ScrollViewer)window.FindName("PageScroll");
     if(width==1180)Check(scroll.ScrollableHeight==0&&scroll.ComputedVerticalScrollBarVisibility==Visibility.Collapsed,"Actual default window: Settings fully visible without vertical scrollbar: "+theme);
     Check(scroll.HorizontalScrollBarVisibility==ScrollBarVisibility.Disabled,"No horizontal overflow at reduced width");
     Console.WriteLine("Settings "+theme+" "+width+" px: content="+scroll.ExtentHeight.ToString("0.0")+", viewport="+scroll.ViewportHeight.ToString("0.0")+", scroll="+scroll.ScrollableHeight.ToString("0.0"));
     Save((FrameworkElement)window.Content,Path.Combine(output,"SettingsCompact-"+theme+(width==900?"-900":"")+".png"));
    }
   } finally {window.Close();}
   foreach(int n in new[]{0,1,3}) {
    var sceneHome=new HomeViewModel(null,null);Apply(sceneHome,Disks(n));var scene=ViewModel(project,sceneHome,dialogs,theme);sceneHome.Stop();
    var sceneWindow=new MainWindow(scene,dialogs){Left=-30000,Top=-30000,ShowInTaskbar=false};
    try {
     sceneWindow.Show();Pump();var local=(ItemsControl)sceneWindow.FindName("LocalDiskCards");var external=(ItemsControl)sceneWindow.FindName("ExternalDiskCards");var section=(StackPanel)sceneWindow.FindName("ExternalDisksSection");
     Check(local.Items.Count==1&&external.Items.Count==n,"Correct visual group sizes: "+theme+n);
     Check(section.Visibility==(n==0?Visibility.Collapsed:Visibility.Visible),"External section completely hidden with zero removables: "+theme+n);
     Check(ReferenceEquals(local.ItemTemplate,external.ItemTemplate)&&ReferenceEquals(local.ItemsPanel,external.ItemsPanel),"Both disk groups reuse exactly one template and panel");
     var cards=Visual<Border>(local).Concat(Visual<Border>(external)).Where(b=>b.DataContext is DiskViewModel&&b.Width==252).ToArray();
     Check(cards.Length==1+n,"Each ready volume rendered exactly once: "+theme+n);
     foreach(var card in cards) {
      Check(card.ActualWidth==252&&card.Padding==new Thickness(12)&&card.Margin==new Thickness(0,0,10,10),"Identical disk dimensions and spacing");
      Check(Equals(card.Background,app.Resources["SurfaceBrush"])&&Equals(card.BorderBrush,app.Resources["BorderBrush"]),"Same themed disk surface and border");
      Check(Visual<TextBlock>(card).Any(t=>t.Text==((DiskViewModel)card.DataContext).Name)&&Visual<TextBlock>(card).Any(t=>t.Text=="Correcto"),"Name, capacity/free space and status retained");
     }
     if(n>0)Check(section.TransformToAncestor((FrameworkElement)sceneWindow.Content).Transform(new Point()).Y>=local.TransformToAncestor((FrameworkElement)sceneWindow.Content).Transform(new Point()).Y+local.ActualHeight,"External section follows local disks without interleaving");
     Save((FrameworkElement)sceneWindow.Content,Path.Combine(output,"HomeDisks-"+(n==0?"LocalOnly":n==1?"OneExternal":"MultipleExternal")+"-"+theme+".png"));
     sceneWindow.Width=900;sceneWindow.UpdateLayout();Pump();
     Check(cards.All(c=>c.TransformToAncestor((FrameworkElement)sceneWindow.FindName("PageScroll")).Transform(new Point()).X+c.ActualWidth<=((FrameworkElement)sceneWindow.FindName("PageScroll")).ActualWidth),"Disk cards fit reduced window without horizontal clipping");
    } finally {sceneWindow.Close();}
   }
  }
  return count+" checks passed: grouped disks, refresh, compact Settings and ten Light/Dark captures; no real queries, shell, maintenance or UAC.";
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
[HomeSettingsLayoutChecks]::Run($project)
