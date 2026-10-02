$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $project 'bin\Debug\WinSereno.exe'
[void][Reflection.Assembly]::LoadFrom($exe)
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.Xaml
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
using System.Windows.Markup;
using WinSereno.Models;
using WinSereno.Services;
using WinSereno.ViewModels;
using WinSereno.Views;

public sealed class HistoryDialogs : IDialogService {
 public TaskProgress Output;
 public int Messages;
 public void ShowOutput(TaskProgress p) { Output=p; }
 public void ShowMessage(string text) { Messages++; }
 public void ShowDiagnosticDetails(DiagnosticResult r) { }
 public bool ConfirmTask(MaintenanceTask t) { throw new Exception("No real tasks permitted"); }
 public bool ConfirmCancelAndClose() { return false; }
 public RestartAdapter SelectRestartAdapter(IReadOnlyList<RestartAdapter> values) { throw new Exception("No network actions permitted"); }
}
public sealed class HistoryRunner : IMaintenanceTaskRunner {
 public bool IsActive {get {return Current!=null&&Current.State==RunnerState.Running;}}
 public TaskProgress Current {get;private set;}
 public event EventHandler<TaskProgress> ProgressChanged;
 public void Publish(TaskProgress p) { Current=p; if(ProgressChanged!=null) ProgressChanged(this,p); }
 public Task<MaintenanceTaskResult> RunAsync(MaintenanceTask t) { throw new Exception("No processes, cleanup or UAC permitted"); }
 public bool RequestCancellation() { throw new Exception("Dismiss must never cancel"); }
 public Task WaitForIdleAsync() { return Task.CompletedTask; }
}
public sealed class HistoryLogger : ISessionLogger {
 public void Write(string text) { }
 public void TaskStarted(MaintenanceTask task) { }
 public void TaskFinished(MaintenanceTask task,MaintenanceTaskResult result) { }
 public void Dispose() { }
}
public static class ActivityHistoryChecks {
 static int count;
 static readonly DateTimeOffset Epoch=new DateTimeOffset(2026,10,2,10,0,0,TimeSpan.Zero);
 static void Check(bool value,string label) { if(!value) throw new Exception(label); count++; }
 static TaskProgress Sample(string id,int finish=1,ExecutionStatus execution=ExecutionStatus.Success,FindingStatus finding=FindingStatus.Healthy) {
  var r=new MaintenanceTaskResult {StartedAt=Epoch.AddMinutes(finish-1),FinishedAt=Epoch.AddMinutes(finish),
   Duration=TimeSpan.FromSeconds(4),ExecutionStatus=execution,FindingStatus=finding,UserSummary=id+" summary",StdOut=id+" stdout",StdErr=id+" stderr",ExitCode=0};
  return new TaskProgress {CurrentTask=new MaintenanceTask {Id=id,Name=id+" action"},State=RunnerState.Completed,
   Result=r,StdOut=r.StdOut,StdErr=r.StdErr,Elapsed=r.Duration,StartedAt=r.StartedAt};
 }
 static IEnumerable<T> Children<T>(DependencyObject node) where T:DependencyObject {
  if(node is T) yield return (T)node;
  foreach(var child in LogicalTreeHelper.GetChildren(node)) if(child is DependencyObject)
   foreach(var item in Children<T>((DependencyObject)child)) yield return item;
 }
 static void Layout(FrameworkElement element) {
  element.Measure(new Size(860,650)); element.Arrange(new Rect(0,0,860,650)); element.UpdateLayout();
 }
 static MainViewModel CreateMain(string project,HistoryRunner runner,OperationCoordinator operations,HistoryDialogs dialogs) {
  var integrity=new IntegritySessionState();
  var diagnostic=new DiagnosticViewModel(new DiagnosticService(null,null,integrity),operations,null,dialogs,integrity);
  return new MainViewModel(new PortableStorage(project),new AppSettings(),new ThemeService(),dialogs,runner,
   new HomeViewModel(null,null),diagnostic,operations,integrity,null);
 }
 static void Set(object obj,string property,object value) { obj.GetType().GetProperty(property).SetValue(obj,value,null); }
 static CleanupViewModel Analysis(bool fail=false,bool cancel=false) {
  Func<IProgress<CleanupCategoryResult>,CancellationToken,Task<CleanupAnalysisResult>> collect=(progress,token)=>{
   if(fail) throw new IOException("simulated provider error");
   var categories=new List<CleanupCategoryResult>();
   foreach(CleanupCategory c in Enum.GetValues(typeof(CleanupCategory))) {
    var r=new CleanupCategoryResult(); Set(r,"Category",c); Set(r,"Name",c.ToString()); Set(r,"WasAnalyzed",true);
    Set(r,"IsAvailable",true); Set(r,"TotalBytes",100L); categories.Add(r);
   }
   var result=new CleanupAnalysisResult(); Set(result,"Categories",categories.AsReadOnly());Set(result,"WasCancelled",cancel);
   Set(result,"Duration",TimeSpan.FromSeconds(1));return Task.FromResult(result);
  };
  return (CleanupViewModel)typeof(CleanupViewModel).GetConstructors(BindingFlags.NonPublic|BindingFlags.Instance)
   .Single().Invoke(new object[]{collect,new OperationCoordinator(),new HistoryLogger(),null});
 }
 public static string Run(string project) {
  var app=Application.Current??new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
  ResourceDictionary styles;
  using(var stream=File.OpenRead(Path.Combine(project,"Themes","Styles.xaml"))) styles=(ResourceDictionary)XamlReader.Load(stream);
  app.Resources.MergedDictionaries.Add(styles);
  var history=new ActionHistoryService();
  Check(history.Entries.Count==0,"New session empty");
  history.Record(null); history.Record(new TaskProgress());
  var active=Sample("running");active.State=RunnerState.Running;history.Record(active);
  Check(history.Entries.Count==0,"Active/absent results not recorded");
  var first=Sample("repair.dism.scanhealth",10);history.Record(first);history.Record(first);
  Check(history.Entries.Count==1,"One entry per final result");
  var snapshot=history.Entries[0]; first.Result.StdOut="changed";first.Result.StdErr="changed";
  first.Result.UserSummary="changed";first.CurrentTask.Name="changed";first.Result.FinishedAt=Epoch;
  Check(snapshot.StdOut.Contains("scanhealth stdout"),"Immutable stdout");
  Check(snapshot.StdErr.Contains("scanhealth stderr"),"Immutable stderr");
  Check(snapshot.Summary.Contains("scanhealth summary"),"Immutable summary");
  Check(snapshot.Name.Contains("scanhealth action"),"Immutable name");
  Check(snapshot.FinishedAt==Epoch.AddMinutes(10),"Immutable timestamp");
  history.Record(Sample("new",30));history.Record(Sample("old",2));
  Check(history.Entries.Select(e=>e.TaskId).SequenceEqual(new[]{"new","repair.dism.scanhealth","old"}),"Timestamp descending, not insertion order");
  history.Record(Sample("same-time",30));Check(history.Entries[0].TaskId=="same-time","Ties newest notification first");
  var complete=Sample("repair.complete",40,ExecutionStatus.Success,FindingStatus.Repaired);
  complete.Result.SequenceSteps.Add(new SequenceStepResult {TaskId="repair.dism.scanhealth",Result=Sample("scan").Result});
  complete.Result.SequenceSteps.Add(new SequenceStepResult {TaskId="repair.dism.restorehealth",WasSkipped=true,SkipReason="Healthy; no repair needed"});
  complete.Result.SequenceSteps.Add(new SequenceStepResult {TaskId="repair.sfc.scannow",Result=Sample("sfc").Result});
  int before=history.Entries.Count;history.Record(complete);Check(history.Entries.Count==before+1,"Complete repair single parent");
  var seqEntry=history.Entries[0];complete.Result.SequenceSteps[1].SkipReason="changed";
  var detail=seqEntry.CreateDetailsProgress();
  Check(detail.StdOut.Contains("Skipped")&&detail.StdOut.Contains("Healthy; no repair needed"),"Skipped reasons snapshotted");
  Check(detail.StdOut.Contains("repair.sfc.scannow"),"SFC step in details only");
  Check(detail.StdOut.EndsWith(seqEntry.StdOut),"Full stdout preserved in details");
  Check(detail.StdErr==seqEntry.StdErr,"Full stderr in details");
  detail.StdOut="edited";detail.Result.UserSummary="edited";
  Check(seqEntry.CreateDetailsProgress().StdOut.Contains("Skipped"),"Detail window cannot mutate entry");
  foreach(var pair in new[]{Tuple.Create(FindingStatus.Healthy,"Correcto"),Tuple.Create(FindingStatus.Repaired,"Reparado"),
   Tuple.Create(FindingStatus.RepairRequired,"Atenci\u00f3n"),Tuple.Create(FindingStatus.PartiallyCompleted,"Parcial"),
   Tuple.Create(FindingStatus.Unknown,"No comprobado"),Tuple.Create(FindingStatus.Failed,"Fallo")}) {
   var e=new ActionHistoryEntry(Sample("a").CurrentTask,Sample("a",1,ExecutionStatus.Success,pair.Item1).Result);
   Check(e.StatusText==pair.Item2,"Functional label "+pair.Item1);
  }
  history.Record(Sample("uac",45,ExecutionStatus.Cancelled));Check(history.Entries[0].StatusText=="Cancelada","UAC cancellation recorded");
  history.Record(Sample("ipc",46,ExecutionStatus.Failed));Check(history.Entries[0].StatusText=="Fallo","Worker failure recorded");
  var restart=Sample("restart",47);restart.Result.RequiresRestart=true;history.Record(restart);
  Check(history.Entries[0].StateText.Contains("Reinicio"),"Restart information preserved");
  var mock=Sample("mock",48);mock.CurrentTask.IsMock=true;before=history.Entries.Count;history.Record(mock);
  Check(history.Entries.Count==before,"Development mocks excluded");
  Check(new ActionHistoryService().Entries.Count==0,"New service does not load old session");

  var operations=new OperationCoordinator();var runner=new HistoryRunner();var dialogs=new HistoryDialogs();
  var vm=CreateMain(project,runner,operations,dialogs);
  Check(!vm.HasHistory&&!vm.ShowTaskPanel,"Initial presentation empty");
  var panel=new TaskExecutionPanel {DataContext=vm};
  var close=Children<Button>(panel).Single(b=>Convert.ToString(b.Content)=="X");
  var finished=Sample("repair.complete",50);
  using(var lease=operations.Begin("simulated repair",false)) {
   runner.Publish(new TaskProgress {CurrentTask=finished.CurrentTask,State=RunnerState.Running,Percentage=37});
   Layout(panel);Check(vm.ShowTaskPanel,"Active panel visible");
   Check(!vm.CanDismissTaskPanel&&close.Visibility==Visibility.Collapsed,"No X during active operation");
   vm.DismissTaskPanelCommand.Execute(null);Check(vm.ShowTaskPanel&&operations.IsActive,"Programmatic dismiss cannot hide/cancel active operation");
   runner.Publish(finished);Layout(panel);
   Check(vm.HasHistory&&vm.ActionHistory.Count==1,"Final parent captured even before lease ends");
   Check(!vm.CanDismissTaskPanel,"X waits for global idle");
  }
  Layout(panel);Check(vm.CanDismissTaskPanel&&close.Visibility==Visibility.Visible,"X visible when finished");
  vm.DismissTaskPanelCommand.Execute(null);Layout(panel);
  Check(!vm.ShowTaskPanel&&vm.HasHistory,"Dismiss hides panel, retains history");
  runner.Publish(finished);Check(!vm.ShowTaskPanel&&vm.ActionHistory.Count==1,"Duplicate final event neither reopens nor duplicates");
  vm.HistoryDetailsCommand.Execute(vm.ActionHistory[0]);Check(dialogs.Output.StdOut.Contains("summary"),"Historical details command");
  vm.Navigate(NavigationSection.Activity);Check(vm.PageTitle=="Registro de acciones","Activity page title");
  Check(vm.Navigation.Any(n=>n.Label=="Actividad"),"Sidebar entry");
  vm.Navigate(NavigationSection.Settings);vm.Navigate(NavigationSection.Repair);vm.Navigate(NavigationSection.Activity);
  Check(vm.ActionHistory.Count==1,"Navigation retains history and adds no entries");
  using(var lease=operations.Begin("second",false)) {
   runner.Publish(new TaskProgress {CurrentTask=Sample("second").CurrentTask,State=RunnerState.Running});
   Layout(panel);Check(vm.ShowTaskPanel&&!vm.CanDismissTaskPanel,"Next action reopens active banner");
  }
  runner.Publish(Sample("second",60));Check(vm.ActionHistory.Count==2&&vm.ActionHistory[0].TaskId=="second","Several actions newest first");
  var diagHandler=(EventHandler<TaskProgress>)typeof(DiagnosticViewModel).GetField("Completed",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(vm.Diagnosis);
  diagHandler(vm.Diagnosis,Sample("diagnosis.general",70));
  Check(vm.ActionHistory.Count==3&&vm.TaskName.Contains("diagnosis.general"),"Diagnostic completion integrated without executing diagnostic");
  var analysisHandler=(EventHandler<TaskProgress>)typeof(CleanupViewModel).GetField("Completed",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(vm.Cleanup);
  analysisHandler(vm.Cleanup,Sample("cleanup.analyze",80));Check(vm.ActionHistory.Count==4,"Normal cleanup completion integrated");
  Check(CreateMain(project,new HistoryRunner(),new OperationCoordinator(),new HistoryDialogs()).ActionHistory.Count==0,"New main view model has empty history");

  var analysis=Analysis();int notifications=0;TaskProgress analysisOutput=null;
  analysis.Completed+=(s,e)=>{notifications++;analysisOutput=e;};
  analysis.AnalyzeAsync(false).GetAwaiter().GetResult();Check(notifications==0,"Automatic reanalysis excluded");
  analysis.AnalyzeAsync().GetAwaiter().GetResult();Check(notifications==1,"Explicit normal analysis included");
  Check(analysisOutput.StdOut.Contains("UserTemporary")&&analysisOutput.StdOut.Contains("RecycleBin"),"Analysis details snapshot");
  Check(analysisOutput.Result.FinishedAt>=analysisOutput.Result.StartedAt,"Analysis timestamps");
  var failed=Analysis(true);failed.Completed+=(s,e)=>analysisOutput=e;
  failed.AnalyzeAsync().GetAwaiter().GetResult();Check(analysisOutput.Result.ExecutionStatus==ExecutionStatus.Failed,"Analysis error captured");
  var cancelled=Analysis(false,true);cancelled.Completed+=(s,e)=>analysisOutput=e;
  cancelled.AnalyzeAsync().GetAwaiter().GetResult();Check(analysisOutput.Result.ExecutionStatus==ExecutionStatus.Cancelled,"Analysis cancellation captured");

  foreach(string theme in new[]{"Light","Dark"}) {
   ResourceDictionary colors;
   using(var stream=File.OpenRead(Path.Combine(project,"Themes",theme+".xaml"))) colors=(ResourceDictionary)XamlReader.Load(stream);
   app.Resources.MergedDictionaries.Add(colors);
   var window=new MainWindow(vm,dialogs);vm.Navigate(NavigationSection.Activity);
   Layout((FrameworkElement)window.Content);
   var list=Children<ItemsControl>(window).Single(c=>ReferenceEquals(c.ItemsSource,vm.ActionHistory));
   Check(list.Items.Count==vm.ActionHistory.Count,"History list binding "+theme);
   Layout(panel);Check(close.Foreground is System.Windows.Media.SolidColorBrush,"X theme brush "+theme);
   Check(panel.Content is Border,"Existing panel intact "+theme);
   using(var lease=operations.Begin("noncancelable",false)) {
    var args=new System.ComponentModel.CancelEventArgs();
    typeof(MainWindow).GetMethod("OnClosing",BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly).Invoke(window,new object[]{window,args});
    Check(args.Cancel&&operations.IsActive,"Noncancelable close still blocked "+theme);
   }
   window.Close();app.Resources.MergedDictionaries.Remove(colors);
  }
  app.Resources.MergedDictionaries.Remove(styles);
  string main=File.ReadAllText(Path.Combine(project,"ViewModels","MainViewModel.cs"));
  Check(main.Contains("await Cleanup.AnalyzeAsync(false)"),"Post-cleanup reanalysis explicitly excluded");
  string ui=File.ReadAllText(Path.Combine(project,"Views","MainWindow.xaml"));
  Check(ui.Contains("ItemsSource=\"{Binding ActionHistory}\"")&&ui.Contains("Todav\u00eda no se han realizado acciones"),"History empty and list presentation");
  Check(!ui.Contains("local:")&&!ui.Contains("x:Static m:"),"No local XAML type references reintroduced");
  return count+" checks passed; simulated results only, no maintenance, cleanup, network, UAC or settings writes.";
 }
}
'@
$provider = [Microsoft.CSharp.CSharpCodeProvider]::new()
$parameters = [CodeDom.Compiler.CompilerParameters]::new()
$parameters.GenerateInMemory = $true
foreach($reference in @($exe,'System.dll','System.Core.dll','System.Xml.dll','System.Xaml.dll','System.Runtime.Serialization.dll',
 [System.Windows.Application].Assembly.Location,[System.Windows.Media.Visual].Assembly.Location,[System.Windows.DependencyObject].Assembly.Location)) {
 [void]$parameters.ReferencedAssemblies.Add($reference)
}
$compiled = $provider.CompileAssemblyFromSource($parameters,$source)
if($compiled.Errors.HasErrors) { throw ($compiled.Errors | Out-String) }
[ActivityHistoryChecks]::Run($project)
