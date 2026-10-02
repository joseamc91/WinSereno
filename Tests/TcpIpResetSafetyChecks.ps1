param([switch]$CompileOnly)
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
$exe=Join-Path $project 'bin\Debug\WinSereno.exe'
if(-not $CompileOnly){[void][Reflection.Assembly]::LoadFrom($exe)}
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Xaml
$source=@'
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinSereno.Models;
using WinSereno.Services;
using WinSereno.ViewModels;
using WinSereno.Views;
public sealed class TcpFixtureLog:ISessionLogger {
 public readonly List<string> Lines=new List<string>();public void Write(string s){Lines.Add(s);}
 public void TaskStarted(MaintenanceTask t){}public void TaskFinished(MaintenanceTask t,MaintenanceTaskResult r){}public void Dispose(){}
}
public sealed class TcpFixturePreflight:ITcpIpResetPreflight {
 public TcpIpResetSnapshot Snapshot;public List<string> Flow;public TcpIpResetSnapshot Read(){Flow.Add("preflight");return Snapshot;}
}
public sealed class TcpFixtureDialogs:IDialogService,ITcpIpResetDialogs {
 public bool First,Second;public List<string> Flow;public TcpIpResetSnapshot Seen;
 public bool ConfirmTask(MaintenanceTask t){Flow.Add("first");if(t.Id!=ElevatedTaskCatalog.ResetTcpIpId||t.Arguments!="int ip reset")throw new Exception("Fixed task changed");return First;}
 public bool ConfirmTcpIpReset(TcpIpResetSnapshot s){Flow.Add("second");Seen=s;return Second;}
 public void ShowMessage(string s){throw new Exception(s);}public void ShowOutput(TaskProgress p){}public void ShowDiagnosticDetails(DiagnosticResult r){}
 public bool ConfirmCancelAndClose(){return false;}public RestartAdapter SelectRestartAdapter(IReadOnlyList<RestartAdapter> a){throw new Exception("Other action forbidden");}
}
public sealed class TcpFixtureRunner:IMaintenanceTaskRunner {
 public List<string> Flow;public int Calls;public TaskProgress Current{get;private set;}public bool IsActive{get{return false;}}
 public event EventHandler<TaskProgress> ProgressChanged;
 public Task<MaintenanceTaskResult> RunAsync(MaintenanceTask t){
  Calls++;Flow.Add("runner");var approval=typeof(MaintenanceTask).GetProperty("TcpIpApproval",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(t,null);
  if(approval==null)throw new Exception("Runner reached without approval");
  var r=new MaintenanceTaskResult{StartedAt=DateTimeOffset.Now,FinishedAt=DateTimeOffset.Now,Duration=TimeSpan.FromSeconds(2),ExecutionStatus=ExecutionStatus.Success,FindingStatus=FindingStatus.Completed,RequiresRestart=true,UserSummary="Simulated TCP/IP result"};
  Current=new TaskProgress{CurrentTask=t,State=RunnerState.Completed,Result=r};if(ProgressChanged!=null)ProgressChanged(this,Current);return Task.FromResult(r);
 }
 public bool RequestCancellation(){throw new Exception("No cancellation of real tools");}public Task WaitForIdleAsync(){return Task.CompletedTask;}
}
public static class TcpIpResetSafetyChecks {
 static int count;static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;}
 static TcpIpAdapterConfiguration Adapter(string id,Ipv4ConfigurationMode mode,string ip="192.168.0.200"){
  return new TcpIpAdapterConfiguration(id,"Ethernet","Realtek Gaming 2.5GbE Family Controller",mode,ip,"255.255.255.0","192.168.0.1","192.168.0.1, 1.1.1.1");
 }
 static TcpIpResetSnapshot Snapshot(params TcpIpAdapterConfiguration[] a){return new TcpIpResetSnapshot(a,true);}
 static void Rejected(Action action,string label){bool rejected=false;try{action();}catch(InvalidOperationException){rejected=true;}catch(TargetInvocationException e){if(e.InnerException is InvalidOperationException)rejected=true;else throw;}Check(rejected,label);}
 static void Pump(Task task){var frame=new DispatcherFrame();var deadline=DateTime.UtcNow.AddSeconds(10);var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(5)};
  timer.Tick+=(s,e)=>{if(task.IsCompleted||DateTime.UtcNow>deadline)frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);timer.Stop();Check(task.IsCompleted,"Controlled async flow completed");task.GetAwaiter().GetResult();}
 static IEnumerable<T> Visual<T>(DependencyObject root)where T:DependencyObject{if(root is T)yield return (T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var v in Visual<T>(VisualTreeHelper.GetChild(root,i)))yield return v;}
 static void Layout(FrameworkElement root,double width){root.Measure(new Size(width,570));root.Arrange(new Rect(0,0,width,570));root.UpdateLayout();}
 static void Flow(string project,TcpIpResetSnapshot snapshot,bool first,bool second,bool expectedRunner){
  var flow=new List<string>();var log=new TcpFixtureLog();var dialogs=new TcpFixtureDialogs{First=first,Second=second,Flow=flow};var runner=new TcpFixtureRunner{Flow=flow};
  var preflight=new TcpFixturePreflight{Snapshot=snapshot,Flow=flow};var ops=new OperationCoordinator();var state=new IntegritySessionState();
  var vm=new MainViewModel(new PortableStorage(project),new AppSettings(),new ThemeService(),dialogs,runner,new HomeViewModel(null,null),new DiagnosticViewModel(null,ops,log,dialogs,state),ops,state,log,null,preflight);
  Pump((Task)typeof(MainViewModel).GetMethod("ResetTcpIpAsync",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(vm,null));
  string expected=!first?"preflight,first":snapshot.RequiresWarning?(second?"preflight,first,second,runner":"preflight,first,second"):"preflight,first,runner";
  Check(string.Join(",",flow)==expected,"Exact confirmation ordering: "+expected);
  Check(runner.Calls==(expectedRunner?1:0),"No runner/UAC path after rejected confirmation");Check(!ops.IsActive,"Global preflight lease released");
  Check(vm.ActionHistory.Count==(expectedRunner?1:0),"One Activity action, no preflight or modal entries");
  if(expectedRunner){Check(vm.ActionHistory[0].CreateDetailsProgress().CurrentTask.Id==ElevatedTaskCatalog.ResetTcpIpId,"Activity retains only TCP/IP action");Check(log.Lines.Any(l=>l.Contains("se permite solicitar UAC")),"UAC permitted only after confirmation log");}
  else Check(log.Lines.Any(l=>l.Contains("cancelada")),"Rejected confirmation recorded");
  if(first&&snapshot.RequiresWarning)Check(ReferenceEquals(dialogs.Seen,snapshot),"Warning receives exact immutable snapshot");
 }
 public static string Run(string project){
  var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/WinSereno;component/Themes/Styles.xaml",UriKind.Relative)});
  var manual=Snapshot(Adapter("one",Ipv4ConfigurationMode.Manual));var dhcp=Snapshot(Adapter("one",Ipv4ConfigurationMode.Dhcp));
  var unknown=Snapshot(Adapter("one",Ipv4ConfigurationMode.Unknown));var unavailable=new TcpIpResetSnapshot(new TcpIpAdapterConfiguration[0],false);
  var mixed=Snapshot(Adapter("one",Ipv4ConfigurationMode.Manual),Adapter("two",Ipv4ConfigurationMode.Dhcp));var multi=Snapshot(Adapter("one",Ipv4ConfigurationMode.Manual),Adapter("two",Ipv4ConfigurationMode.Manual));
  foreach(bool? a in new bool?[]{true,false,null})foreach(bool? b in new bool?[]{true,false,null}){
   var expected=!a.HasValue||!b.HasValue||a!=b?Ipv4ConfigurationMode.Unknown:a.Value?Ipv4ConfigurationMode.Dhcp:Ipv4ConfigurationMode.Manual;
   Check(TcpIpResetPreflightService.Classify(a,b)==expected,"Structured DHCP consensus "+a+" / "+b);
  }
  Check(manual.RequiresWarning&&!manual.IsIndeterminate&&manual.WarningInterfaces.Count==1,"Static IPv4 detected");
  var record=manual.WarningInterfaces[0];Check(record.IPv4=="192.168.0.200"&&record.Mask=="255.255.255.0"&&record.Gateway=="192.168.0.1"&&record.Dns=="192.168.0.1, 1.1.1.1","Exact manual snapshot");
  Check(record.ConfigurationLabel.Contains("DHCP desactivado"),"Manual DHCP label");Check(!dhcp.RequiresWarning,"Pure DHCP needs no extra friction");
  Check(mixed.RequiresWarning&&mixed.WarningInterfaces.Count==1,"Mixed configuration displays manual interface");Check(multi.WarningInterfaces.Count==2&&multi.WarningMessage.Contains("interfaces"),"All manual interfaces, plural wording");
  Check(unknown.RequiresWarning&&unknown.IsIndeterminate&&unavailable.RequiresWarning&&unavailable.IsIndeterminate,"Unknown/missing data fail safe, still confirmable");
  Check(new TcpIpAdapterConfiguration("x","test",null,Ipv4ConfigurationMode.Unknown,null,null,null,null).IPv4=="No disponible","Missing fields not invented");
  foreach(string description in new[]{"VPN","Hyper-V virtual","Bluetooth PAN","WireGuard tunnel","VMware","VirtualBox","Loopback"})Check(!DhcpRenewalPolicy.IsEligible(true,true,true,true,"Ethernet","Ethernet",description),"Shared exclusions: "+description);
  Check(!DhcpRenewalPolicy.IsEligible(false,true,true,true,"Ethernet","Ethernet","Realtek"),"Nonphysical excluded");
  Check(DhcpRenewalPolicy.IsEligible(true,true,true,true,"Wi-Fi","Wi-Fi","Intel Wireless"),"Physical WiFi eligible");
  var fingerprint=TcpIpResetPreflightService.Fingerprint(manual);
  TcpIpResetPreflightService.ValidateContext(manual,fingerprint,true);count++;
  Rejected(()=>TcpIpResetPreflightService.ValidateContext(manual,fingerprint,false),"Manual cannot bypass second consent");
  Rejected(()=>TcpIpResetPreflightService.ValidateContext(manual,"arbitrary",true),"Invalid context rejected");
  Rejected(()=>TcpIpResetPreflightService.ValidateContext(unknown,TcpIpResetPreflightService.Fingerprint(unknown),false),"Unknown cannot bypass second consent");
  Rejected(()=>TcpIpResetPreflightService.ValidateContext(manual,TcpIpResetPreflightService.Fingerprint(dhcp),false),"DHCP changed to manual after consent rejected");
  Rejected(()=>TcpIpResetPreflightService.ValidateContext(Snapshot(Adapter("new",Ipv4ConfigurationMode.Manual)),fingerprint,true),"Different interface selection cannot reuse consent");
  Check(fingerprint==TcpIpResetPreflightService.Fingerprint(Snapshot(Adapter("one",Ipv4ConfigurationMode.Manual,"192.168.0.201"))),"Mutable IP address does not invalidate same safety context");
  Check(TcpIpResetPreflightService.Fingerprint(multi)==TcpIpResetPreflightService.Fingerprint(Snapshot(multi.Interfaces.Reverse().ToArray())),"Stable order-independent context");
  TcpIpResetPreflightService.ValidateContext(unavailable,TcpIpResetPreflightService.Fingerprint(unavailable),true);count++;
  var approvalType=typeof(MaintenanceTask).Assembly.GetType("WinSereno.Services.TcpIpResetApproval");
  var approval=Activator.CreateInstance(approvalType,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{manual,true},null);
  var consume=approvalType.GetMethod("Consume",BindingFlags.NonPublic|BindingFlags.Instance);consume.Invoke(approval,null);Rejected(()=>consume.Invoke(approval,null),"Approval is single use");
  var realRunner=new MaintenanceTaskRunner(new TcpFixtureLog(),new OperationCoordinator());
  Rejected(()=>realRunner.RunAsync(ElevatedTaskCatalog.Get(ElevatedTaskCatalog.ResetTcpIpId)).GetAwaiter().GetResult(),"Actual runner rejects missing preflight before UAC");
  foreach(var fixture in new[]{manual,unknown,unavailable,mixed,multi}){Flow(project,fixture,false,false,false);Flow(project,fixture,true,false,false);Flow(project,fixture,true,true,true);}
  Flow(project,dhcp,false,false,false);Flow(project,dhcp,true,false,true);
  string output=Path.Combine(project,"bin","Debug","VisualChecks");Directory.CreateDirectory(output);
  foreach(string theme in new[]{"Light","Dark"}){
   var colors=new ResourceDictionary{Source=new Uri("/WinSereno;component/Themes/"+theme+".xaml",UriKind.Relative)};app.Resources.MergedDictionaries.Add(colors);
   foreach(var fixture in new[]{manual,multi,unknown,unavailable}){
    var window=new TcpIpResetWarningWindow(fixture);var root=(FrameworkElement)window.Content;var vm=(TcpIpResetWarningViewModel)window.DataContext;
    var button=(Button)window.FindName("ResetButton");var box=(CheckBox)window.FindName("Acknowledgement");
    foreach(double width in new[]{672.0,492.0}){
     Layout(root,width);Check(!vm.CanContinue&&!button.IsEnabled&&!box.IsChecked.GetValueOrDefault(),"Dangerous button initially disabled");
     box.IsChecked=true;Layout(root,width);Check(vm.Acknowledged&&button.IsEnabled,"Explicit check enables continuation");box.IsChecked=false;Layout(root,width);Check(!vm.CanContinue&&!button.IsEnabled,"Unchecking revokes continuation");
     Check(Visual<Button>(root).Single(b=>Convert.ToString(b.Content)=="Cancelar").IsCancel,"Cancel escapes without accepting");
     Check(!button.IsDefault,"Enter does not implicitly accept warning");
     Check(Visual<TextBlock>(root).Any(t=>t.Text==fixture.WarningMessage),"Prominent risk text retained");
     Check(Visual<Border>(root).Any(b=>Equals(b.BorderBrush,app.Resources["WarningBrush"])&&b.BorderThickness.Left==2),"Themed warning accent");
     foreach(var text in Visual<TextBlock>(root)){var p=text.TransformToAncestor(root).Transform(new Point());Check(p.X>=-0.5&&p.X+text.ActualWidth<=root.ActualWidth+0.5,"No text escapes narrow dialog: "+text.Text);}
     var scroll=Visual<ScrollViewer>(root).First();Check(scroll.HorizontalScrollBarVisibility==ScrollBarVisibility.Disabled,"Long configuration wraps; vertical scroll available");
     Check(Visual<TextBlock>(root).Count(t=>t.Text=="IPv4")==fixture.WarningInterfaces.Count,"Every warned interface shown separately");
    }
    if(ReferenceEquals(fixture,manual)){
     Layout(root,672);var image=new RenderTargetBitmap(672,570,96,96,PixelFormats.Pbgra32);var bg=new DrawingVisual();using(var drawing=bg.RenderOpen())drawing.DrawRectangle((Brush)app.Resources["BackgroundBrush"],null,new Rect(0,0,672,570));image.Render(bg);image.Render(root);
     var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var stream=File.Create(Path.Combine(output,"TcpIpWarning-"+theme+".png")))encoder.Save(stream);
    }
    window.Close();Check(window.DialogResult!=true,"Title-bar close never grants approval");
   }
   app.Resources.MergedDictionaries.Remove(colors);
  }
  return count+" comprobaciones TCP/IP correctas; configuración, confirmaciones, worker y Light/Dark simulados, sin comandos ni UAC.";
 }
}
'@
$provider=[Microsoft.CSharp.CSharpCodeProvider]::new();$parameters=[CodeDom.Compiler.CompilerParameters]::new();$parameters.GenerateInMemory=$true
foreach($reference in @($exe,'System.dll','System.Core.dll','System.Xml.dll','System.Xaml.dll',[System.Windows.Application].Assembly.Location,[System.Windows.Media.Visual].Assembly.Location,[System.Windows.DependencyObject].Assembly.Location)){[void]$parameters.ReferencedAssemblies.Add($reference)}
$compiled=$provider.CompileAssemblyFromSource($parameters,$source)
if($compiled.Errors.HasErrors){throw ($compiled.Errors | Out-String)}
if($CompileOnly){'Harness TCP/IP compilado sin ejecución ni UAC.';return}
[TcpIpResetSafetyChecks]::Run($project)
