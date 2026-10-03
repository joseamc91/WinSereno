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
using System.Reflection;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using WinSereno.Models;
using WinSereno.Services;
using WinSereno.ViewModels;
using WinSereno.Infrastructure;
public sealed class CleanupFixtureLog:ISessionLogger{
 public List<string> Lines=new List<string>();public void Write(string s){Lines.Add(s);}public void TaskStarted(MaintenanceTask t){}public void TaskFinished(MaintenanceTask t,MaintenanceTaskResult r){}public void Dispose(){}
}
public sealed class CleanupFixtureRunner:IMaintenanceTaskRunner{
 public TaskProgress Current{get{return null;}}public bool IsActive{get{return false;}}public event EventHandler<TaskProgress> ProgressChanged{add{}remove{}}
 public Task<MaintenanceTaskResult> RunAsync(MaintenanceTask t){throw new Exception("No actual tasks or UAC allowed");}public bool RequestCancellation(){return false;}public Task WaitForIdleAsync(){return Task.CompletedTask;}
}
public sealed class CleanupFixtureDialogs:IDialogService{
 public bool ConfirmTask(MaintenanceTask t){throw new Exception("No delete confirmation allowed during analysis");}public void ShowMessage(string s){throw new Exception(s);}public void ShowOutput(TaskProgress p){}public void ShowDiagnosticDetails(DiagnosticResult r){}
 public bool ConfirmCancelAndClose(){return false;}public RestartAdapter SelectRestartAdapter(IReadOnlyList<RestartAdapter> a){throw new Exception("No network actions allowed");}
}
public static class CleanupExperienceChecks{
 static int count;static void Check(bool ok,string label){if(!ok)throw new Exception(label);count++;}
 static void Set(object target,string property,object value){target.GetType().GetProperty(property).SetValue(target,value,null);}
 static void Pump(Task task){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(5)};var deadline=DateTime.UtcNow.AddSeconds(10);
  timer.Tick+=(s,e)=>{if(task.IsCompleted||DateTime.UtcNow>deadline)frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);timer.Stop();Check(task.IsCompleted,"Simulated analysis finishes");task.GetAwaiter().GetResult();Application.Current.Dispatcher.Invoke(new Action(()=>{}),DispatcherPriority.Background);}
 static CleanupCategoryResult Sample(CleanupCategory category,long bytes=4096,bool available=true,bool partial=false){
  var r=new CleanupCategoryResult();Set(r,"Category",category);Set(r,"Name",CleanupBatchExecutor.Name(category));Set(r,"Path",category==CleanupCategory.WindowsTemporary?CleanupAnalysisService.WindowsTemporaryPath:category==CleanupCategory.RecycleBin?"User recycle bin (SHQueryRecycleBinW)":"controlled fixture path");
  Set(r,"WasAnalyzed",true);Set(r,"IsAvailable",available);Set(r,"TotalBytes",bytes);Set(r,"FileCount",8L);Set(r,"PotentiallyCleanableBytes",bytes/2);Set(r,"PotentiallyCleanableFileCount",4L);Set(r,"AccessDeniedCount",partial?1L:0L);Set(r,"Information","Technical retained information, no UI heuristic.");
  if(category==CleanupCategory.WindowsTemporary)Set(r,"AnalysisFinishedAt",DateTimeOffset.Now);return r;
 }
 static CleanupViewModel View(OperationCoordinator ops,CleanupFixtureLog log,Func<OperationLease,Action<TaskProgress>,Task<MaintenanceTaskResult>> elevated){
  Func<IProgress<CleanupCategoryResult>,CancellationToken,Task<CleanupAnalysisResult>> normal=(progress,token)=>{
   Check(ops.IsActive&&ops.Name=="Análisis de Limpieza","Normal checks share global analysis lease");
   var values=new[]{Sample(CleanupCategory.UserTemporary),Sample(CleanupCategory.ThumbnailCache),Sample(CleanupCategory.RecycleBin)};
   foreach(var value in values)progress.Report(value);var result=new CleanupAnalysisResult();Set(result,"Categories",Array.AsReadOnly(values));return Task.FromResult(result);
  };
  return (CleanupViewModel)typeof(CleanupViewModel).GetConstructors(BindingFlags.Instance|BindingFlags.NonPublic).Single().Invoke(new object[]{normal,ops,log,elevated});
 }
 static MainViewModel CreateMain(string project,CleanupViewModel cleanup,OperationCoordinator ops,CleanupFixtureLog log){
  var state=new IntegritySessionState();var dialogs=new CleanupFixtureDialogs();return new MainViewModel(new PortableStorage(project),new AppSettings(),new ThemeService(),dialogs,new CleanupFixtureRunner(),new HomeViewModel(null,null),new DiagnosticViewModel(null,ops,log,dialogs,state),ops,state,log,null,null,cleanup);
 }
 static void Flow(string project,bool cancel,bool partial,bool fail){
  var ops=new OperationCoordinator();var log=new CleanupFixtureLog();int requests=0;TaskProgress ended=null;MainViewModel main=null;
  Func<OperationLease,Action<TaskProgress>,Task<MaintenanceTaskResult>> elevated=(lease,progress)=>{
   requests++;Check(ops.IsActive&&ops.Name=="Análisis de Limpieza"&&lease.Name==ops.Name,"Elevated phase shares the same operation");Check(!ops.CanBeCancelled,"Administrative phase never force-cancelable");
   Check(main.ShowTaskPanel&&main.TaskName=="Análisis de Limpieza"&&!main.CanDismissTaskPanel,"One active global toast, cannot dismiss");
   progress(new TaskProgress{State=RunnerState.Running,CurrentTask=ElevatedTaskCatalog.Get(ElevatedTaskCatalog.WindowsTempAnalyzeId)});
   var result=new MaintenanceTaskResult{ExecutionStatus=cancel?ExecutionStatus.Cancelled:fail?ExecutionStatus.Failed:ExecutionStatus.Success,UserSummary=cancel?"UAC cancelled":"Administrative fixture"};Set(result,"WindowsTempAnalysis",cancel||fail?null:Sample(CleanupCategory.WindowsTemporary,8192,true,partial));return Task.FromResult(result);
  };
  var vm=View(ops,log,elevated);main=CreateMain(project,vm,ops,log);vm.Completed+=(s,e)=>ended=e;
  Check(vm.Categories.Count==4&&vm.Categories.Take(3).All(c=>c.IsSelected)&&!vm.Categories[3].IsSelected,"Four categories, bin unmarked by default");
  Check(vm.SelectionSummary=="3 categorías marcadas","Simple initial selection");
  var commands=new[]{main.CleanUserTempCommand,main.CleanWindowsTempCommand,main.CleanThumbnailsCommand,main.EmptyRecycleBinCommand,main.CleanSelectedCommand};
  Check(commands.All(c=>!c.CanExecute(null)),"All delete actions disabled before analysis");
  Pump(vm.AnalyzeAsync());Check(requests==1,"One simulated UAC/administrative request maximum");
  Check(ended!=null&&ended.CurrentTask.Id=="cleanup.analyze"&&ended.Result.SequenceSteps.Count==1,"One completion, with Windows Temp internal details");
  Check(main.ActionHistory.Count==1&&main.ActionHistory[0].Name=="Análisis de Limpieza","One Activity action, no administrative sub-entry");
  Check(main.Progress.CurrentTask.Id=="cleanup.analyze"&&main.ShowTaskPanel&&main.CanDismissTaskPanel,"One final global toast");
  Check(vm.LastResult.Categories.Count==4&&vm.LastResult.Categories.All(c=>c.WasAnalyzed),"Four-category combined report");
  Check(main.CleanUserTempCommand.CanExecute(null)&&main.CleanThumbnailsCommand.CanExecute(null)&&main.EmptyRecycleBinCommand.CanExecute(null),"Normal valid categories remain usable");
  Check(main.CleanWindowsTempCommand.CanExecute(null)==(!cancel&&!fail),"Windows action enabled only with valid administrative result");
  Check(main.CleanSelectedCommand.CanExecute(null)==(!cancel&&!fail),"Marked invalid category prevents multiple cleanup");
  var win=vm.Categories.Single(c=>c.IsWindowsTemporary);
  if(cancel||fail){Check(!win.Result.IsAvailable&&!win.Result.PotentiallyCleanableBytes.HasValue&&win.Status=="No comprobado","Rejected/failed UAC never invents Windows estimate");Check(ended.Result.FindingStatus==FindingStatus.PartiallyCompleted,"Incomplete overall result");win.IsSelected=false;Check(main.CleanSelectedCommand.CanExecute(null),"Valid marked categories can be selected without unavailable Windows");}
  Check(vm.SelectionSummary.Contains((cancel||fail)?"Espacio recuperable estimado":"Espacio recuperable estimado")||vm.SelectionSummary.Contains("Estimación parcial"),"Selection contains readable estimate");
  Check(ended.StdOut.Contains("8 archivos accesibles")&&ended.StdOut.Contains("Technical retained information")&&ended.StdOut.Contains("controlled fixture path"),"Technical data and paths retained in details");
  Check(log.Lines.Any(l=>l.Contains("Análisis de Limpieza |")),"Full analysis result logged once");
  Check(vm.Categories.Single(c=>c.IsRecycleBin).Summary.Contains("en la Papelera")&&!vm.Categories.Single(c=>c.IsRecycleBin).DisplayPath.Contains("SHQuery"),"Recycle presentation avoids API details");
  main.DismissTaskPanelCommand.Execute(null);Check(!main.ShowTaskPanel&&main.ActionHistory.Count==1,"Dismiss only hides toast, retains action");
  Pump(vm.AnalyzeAsync());Check(requests==2&&main.ActionHistory.Count==2&&main.ShowTaskPanel,"Reanalysis restores toast and records one new action");
  Pump(vm.AnalyzeAsync(false));Check(requests==2&&main.ActionHistory.Count==2,"Automatic post-clean refresh adds no UAC or Activity entry");
  var fresh=View(new OperationCoordinator(),log,elevated);Check(fresh.ElevatedWindowsTempAnalysis==null&&!fresh.CanCleanSelected,"New session starts without snapshots");
 }
 public static string Run(string project){
  var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
  Flow(project,false,false,false);Flow(project,false,true,false);Flow(project,true,false,false);Flow(project,false,false,true);
  var ops=new OperationCoordinator();var log=new CleanupFixtureLog();var vm=View(ops,log,(lease,p)=>Task.FromResult(new MaintenanceTaskResult{ExecutionStatus=ExecutionStatus.Cancelled}));
  var old=Sample(CleanupCategory.WindowsTemporary);vm.SetWindowsTempAnalysis(old);Pump(vm.AnalyzeAsync());
  Check(ReferenceEquals(vm.ElevatedWindowsTempAnalysis,old)&&!vm.Categories[1].CanClean&&!vm.Categories[1].HasElevatedAnalysisTime,"Old snapshot retained internally but cannot masquerade as new cancelled analysis");
  var runner=new MaintenanceTaskRunner(log,ops);bool rejected=false;using(var lease=ops.Begin("Other operation",false))try{runner.RunCleanupAnalysisAsync(lease,p=>{}).GetAwaiter().GetResult();}catch(InvalidOperationException){rejected=true;}
  Check(rejected&&!ops.IsActive,"Runner rejects wrong lease before UAC");
  var info=typeof(MainViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();Check(info.InformationalVersion=="0.1.0-beta.5"&&ProductInformation.DisplayVersion=="v0.1.0-beta.5","Single public version source");
  Check(typeof(MainViewModel).Assembly.GetName().Version.ToString()=="0.1.0.0"&&typeof(MainViewModel).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>().Version=="0.1.0.0","Technical versions unchanged");
  Check(CleanupAnalysisService.ProtectedRecentHours==48,"48-hour policy unchanged");
  string root=Path.Combine(project,"bin","Debug","CleanupMetricsFixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try{
   string recent=Path.Combine(root,"recent.tmp"),oldFile=Path.Combine(root,"old.tmp"),locked=Path.Combine(root,"locked.tmp");File.WriteAllBytes(recent,new byte[100]);File.WriteAllBytes(oldFile,new byte[200]);File.WriteAllBytes(locked,new byte[300]);
   File.SetLastWriteTimeUtc(recent,DateTime.UtcNow.AddMinutes(-10));File.SetLastWriteTimeUtc(oldFile,DateTime.UtcNow.AddHours(-72));File.SetLastWriteTimeUtc(locked,DateTime.UtcNow.AddHours(-72));
   var provider=new CleanupAnalysisService(log);CleanupCategoryResult result;
   using(var handle=new FileStream(locked,FileMode.Open,FileAccess.ReadWrite,FileShare.None))result=(CleanupCategoryResult)typeof(CleanupAnalysisService).GetMethod("AnalyzeWindowsTemporaryResolved",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(provider,new object[]{new Func<string>(()=>root)});
   Check(result.TotalBytes==300&&result.PotentiallyCleanableBytes==200,"Recent contributes only to TotalBytes; old contributes to both");
   Check(result.FileCount==2&&result.PotentiallyCleanableFileCount==1&&result.LockedCount>0,"Inaccessible fixture invents no bytes");
   Check(Directory.GetFiles(root).Length==3&&new FileInfo(recent).Length==100&&new FileInfo(oldFile).Length==200&&new FileInfo(locked).Length==300,"Analysis leaves all controlled files intact");
  }finally{
   var fixtureRoot=Path.GetFullPath(root);var allowedRoot=Path.GetFullPath(Path.Combine(project,"bin","Debug"))+Path.DirectorySeparatorChar;
   if(!fixtureRoot.StartsWith(allowedRoot,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(fixtureRoot).StartsWith("CleanupMetricsFixture-",StringComparison.Ordinal))throw new Exception("Unsafe fixture cleanup target");
   Directory.Delete(fixtureRoot,true);
  }
  return count+" comprobaciones de experiencia Limpieza correctas; UAC/operaciones simulados y métricas en fixtures controlados, sin limpieza real.";
 }
}
'@
$provider=[Microsoft.CSharp.CSharpCodeProvider]::new();$parameters=[CodeDom.Compiler.CompilerParameters]::new();$parameters.GenerateInMemory=$true
foreach($reference in @($exe,'System.dll','System.Core.dll','System.Xaml.dll',[System.Windows.Application].Assembly.Location,[System.Windows.Media.Visual].Assembly.Location,[System.Windows.DependencyObject].Assembly.Location)){[void]$parameters.ReferencedAssemblies.Add($reference)}
$compiled=$provider.CompileAssemblyFromSource($parameters,$source)
if($compiled.Errors.HasErrors){throw ($compiled.Errors | Out-String)}
if($CompileOnly){'Harness Limpieza compilado sin cargar WinSereno ni solicitar UAC.';return}
[CleanupExperienceChecks]::Run($project)
