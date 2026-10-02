$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $project 'bin\Debug\WinSereno.exe'
[void][Reflection.Assembly]::LoadFrom($exe)
Add-Type -AssemblyName PresentationFramework, WindowsBase
$source = @'
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
public static class WindowsTempSnapshotChecks {
 static int count;
 static readonly Assembly Assembly=typeof(CleanupViewModel).Assembly;
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;}
 static void Set(object value,string property,object data){value.GetType().GetProperty(property).SetValue(value,data,null);}
 static CleanupCategoryResult Sample(CleanupCategory category,long bytes,DateTimeOffset? time=null,bool available=true,bool partial=false){
  var r=new CleanupCategoryResult();Set(r,"Category",category);Set(r,"Name",CleanupBatchExecutor.Name(category));
  Set(r,"Path",category==CleanupCategory.WindowsTemporary?CleanupAnalysisService.WindowsTemporaryPath:"controlled");
  Set(r,"WasAnalyzed",true);Set(r,"IsAvailable",available);Set(r,"TotalBytes",bytes);Set(r,"FileCount",10L);
  Set(r,"PotentiallyCleanableBytes",bytes/2);Set(r,"PotentiallyCleanableFileCount",5L);Set(r,"AnalysisFinishedAt",time);
  Set(r,"AccessDeniedCount",partial?1L:0L);return r;
 }
 static CleanupViewModel View(){
  Func<IProgress<CleanupCategoryResult>,CancellationToken,Task<CleanupAnalysisResult>> analyze=(progress,token)=>{
   var categories=Enum.GetValues(typeof(CleanupCategory)).Cast<CleanupCategory>().Select(c=>Sample(c,123,null,c!=CleanupCategory.WindowsTemporary)).ToArray();
   foreach(var item in categories)progress.Report(item);
   var r=new CleanupAnalysisResult();Set(r,"Categories",Array.AsReadOnly(categories));return Task.FromResult(r);
  };
  return (CleanupViewModel)typeof(CleanupViewModel).GetConstructors(BindingFlags.NonPublic|BindingFlags.Instance).Single()
   .Invoke(new object[]{analyze,new OperationCoordinator(),null});
 }
 static void Pump(Task task){
  var frame=new DispatcherFrame();
  task.ContinueWith(t=>Application.Current.Dispatcher.BeginInvoke(new Action(()=>frame.Continue=false)));
  Dispatcher.PushFrame(frame);task.GetAwaiter().GetResult();
  Application.Current.Dispatcher.Invoke(new Action(()=>{}),DispatcherPriority.Background);
 }
 static void Expect(CleanupViewModel vm,CleanupCategoryResult r){
  Check(object.ReferenceEquals(vm.ElevatedWindowsTempAnalysis,r),"Snapshot conservado");
  var row=vm.Categories.Single(c=>c.IsWindowsTemporary);
  Check(object.ReferenceEquals(row.Result,r)&&row.Result.TotalBytes==r.TotalBytes,"Cifras sin reemplazar");
  Check(row.Result.AnalysisFinishedAt==r.AnalysisFinishedAt && row.HasElevatedAnalysisTime,"Timestamp real intacto");
 }
 static CleanupCategoryResult RoundTrip(CleanupCategoryResult value){
  var type=Assembly.GetType("WinSereno.Services.WindowsTempAnalysisProtocol");
  using(var stream=new MemoryStream()){
   type.GetMethod("Write").Invoke(null,new object[]{new BinaryWriter(stream),value});stream.Position=0;
   return (CleanupCategoryResult)type.GetMethod("Read").Invoke(null,new object[]{new BinaryReader(stream)});
  }
 }
 static MaintenanceTaskResult Cleanup(Func<CleanupCategoryResult> analyze){
  var files=new UserTempCleanupResult();Set(files,"Status",UserTempCleanupStatus.Completed);
  return (MaintenanceTaskResult)Assembly.GetType("WinSereno.Services.WindowsTempCleanupService")
   .GetMethod("RunCore",BindingFlags.NonPublic|BindingFlags.Static)
   .Invoke(null,new object[]{new Func<UserTempCleanupResult>(()=>files),analyze,null});
 }
 sealed class Session:ICleanupWindowsSession {
  public int Runs,Analyses,Closes;public CleanupCategoryResult Snapshot;public bool FailAnalysis;
  public Task<MaintenanceTaskResult> RunWindowsAsync(Action<CleanupCounters> progress){
   Runs++;return Task.FromResult(new MaintenanceTaskResult{ExecutionStatus=ExecutionStatus.Success,FindingStatus=FindingStatus.Completed});
  }
  public Task<CleanupCategoryResult> ReanalyzeWindowsAsync(){
   Analyses++;if(FailAnalysis)throw new IOException("Mock reanalysis failed");return Task.FromResult(Snapshot);
  }
  public Task CloseAsync(){Closes++;return Task.FromResult(0);}
 }
 static MaintenanceTaskResult Batch(Session session,bool cancel,out int opens){
  int calls=0;
  Func<Task<ICleanupWindowsSession>> open=()=>{calls++;if(cancel)throw new System.ComponentModel.Win32Exception(1223);return Task.FromResult((ICleanupWindowsSession)session);};
  Func<CleanupCategory,Action<CleanupCounters>,Task<MaintenanceTaskResult>> execute=(c,p)=>{throw new Exception("Otra categoria prohibida");};
  Func<CleanupCategory,Task<CleanupCategoryResult>> analyze=c=>{throw new Exception("Sin analisis real");};
  var executor=(CleanupBatchExecutor)typeof(CleanupBatchExecutor).GetConstructors(BindingFlags.NonPublic|BindingFlags.Instance).Single()
   .Invoke(new object[]{execute,open,analyze,null});
  var task=CleanupBatchExecutor.Prepare(CleanupSelection.WindowsTemporary,new[]{session.Snapshot});
  var result=executor.RunAsync(task,null).GetAwaiter().GetResult();opens=calls;return result;
 }
 public static string Run(string project,string fixture){
  var app=new Application();SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
  var vm=View();Check(vm.ElevatedWindowsTempAnalysis==null,"Sesion inicial sin snapshot");
  Pump(vm.AnalyzeAsync());
  var initial=vm.Categories.Single(c=>c.IsWindowsTemporary);
  Check(!initial.Result.IsAvailable && initial.Summary.Contains("No disponible")&&!initial.Summary.Contains("0 B"),"Sin acceso no equivale a vacio");
  Check(initial.CanAnalyzeElevated&&!initial.HasElevatedAnalysisTime,"Accion explicita sin timestamp falso");
  var first=RoundTrip(Sample(CleanupCategory.WindowsTemporary,1000,DateTimeOffset.Now.AddHours(-2)));
  vm.SetWindowsTempAnalysis(first);Expect(vm,first);
  Check(first.PotentiallyCleanableBytes==500&&first.FileCount==10&&!first.IsPartial,"Contadores IPC");
  Check(vm.Categories[1].CanAnalyzeElevated,"Permite repetir analisis con permisos aunque completo");
  for(int i=0;i<3;i++){Pump(vm.AnalyzeAsync());Expect(vm,first);}
  Check(vm.Categories[0].Result.TotalBytes==123&&vm.Categories[2].Result.TotalBytes==123,"Otras categorias actualizadas");
  var second=RoundTrip(Sample(CleanupCategory.WindowsTemporary,2000,DateTimeOffset.Now.AddHours(-1),true,true));
  vm.SetWindowsTempAnalysis(second);Expect(vm,second);Check(second.IsPartial,"Snapshot parcial valido conservado");
  vm.SetWindowsTempAnalysis(null);Expect(vm,second);Check(true,"Cancelacion sin resultado");
  var unavailable=Sample(CleanupCategory.WindowsTemporary,0,DateTimeOffset.Now,false,true);
  vm.SetWindowsTempAnalysis(unavailable);Expect(vm,second);
  var cancelled=Sample(CleanupCategory.WindowsTemporary,3000,DateTimeOffset.Now);Set(cancelled,"WasCancelled",true);
  vm.SetWindowsTempAnalysis(cancelled);Expect(vm,second);
  vm.SetWindowsTempAnalysis(Sample(CleanupCategory.WindowsTemporary,3000));Expect(vm,second);
  vm.SetWindowsTempAnalysis(first);Expect(vm,second);
  var after=RoundTrip(Sample(CleanupCategory.WindowsTemporary,300,DateTimeOffset.Now.AddMinutes(-20)));
  var cleaned=Cleanup(()=>after);
  using(var stream=new MemoryStream()){
   var protocol=Assembly.GetType("WinSereno.Services.WindowsTempCleanupProtocol");
   protocol.GetMethod("Write").Invoke(null,new object[]{new BinaryWriter(stream),cleaned});stream.Position=0;
   var received=new MaintenanceTaskResult();protocol.GetMethod("ReadInto").Invoke(null,new object[]{new BinaryReader(stream),received});
   Check(received.WindowsTempAnalysis.AnalysisFinishedAt==after.AnalysisFinishedAt,"Timestamp de reanalisis atraviesa IPC de limpieza");
   Check(received.WindowsTempAnalysis.TotalBytes==after.TotalBytes,"Cifras posteriores atraviesan IPC");
  }
  vm.SetWindowsTempAnalysis(cleaned.WindowsTempAnalysis);Expect(vm,after);
  Check(cleaned.UserSummary.Contains("Rean"),"Resultado de reanalisis separado");
  var failed=Cleanup(()=>{throw new IOException("Mock");});
  vm.SetWindowsTempAnalysis(failed.WindowsTempAnalysis);Expect(vm,after);
  Check(!failed.WindowsTempAnalysis.IsAvailable&&failed.UserSummary.Contains("no disponible"),"Fallo reanalisis no inventa cifras");
  int opens;var session=new Session{Snapshot=RoundTrip(Sample(CleanupCategory.WindowsTemporary,150,DateTimeOffset.Now.AddMinutes(-10)))};
  var batch=Batch(session,false,out opens);
  foreach(var step in batch.CleanupBatch.Steps)if(step.Analysis!=null)vm.SetWindowsTempAnalysis(step.Analysis);
  Expect(vm,session.Snapshot);
  Check(opens==1&&session.Runs==1&&session.Analyses==1&&session.Closes==1,"Un worker simulado y reanalisis dentro de misma sesion");
  Check(batch.CleanupBatch.Status==CleanupBatchStatus.Completed,"Resultado seleccionado correcto");
  var failedSession=new Session{Snapshot=session.Snapshot,FailAnalysis=true};
  var partial=Batch(failedSession,false,out opens);vm.SetWindowsTempAnalysis(partial.CleanupBatch.Steps[0].Analysis);Expect(vm,session.Snapshot);
  Check(partial.CleanupBatch.Status==CleanupBatchStatus.Partial&&opens==1,"Fallo reanalisis seleccionado es parcial");
  var cancelledSession=new Session{Snapshot=session.Snapshot};var uac=Batch(cancelledSession,true,out opens);
  foreach(var step in uac.CleanupBatch.Steps)if(step.Analysis!=null)vm.SetWindowsTempAnalysis(step.Analysis);
  Expect(vm,session.Snapshot);Check(uac.ExecutionStatus==ExecutionStatus.Cancelled&&cancelledSession.Runs==0,"UAC simulado cancelado antes del borrado");
  var format=typeof(CleanupCategoryViewModel).GetMethod("FormatAnalysisTime",BindingFlags.NonPublic|BindingFlags.Static);
  var day=new DateTimeOffset(2026,10,1,12,34,56,DateTimeOffset.Now.Offset);
  string same=(string)format.Invoke(null,new object[]{day});
  string next=(string)format.Invoke(null,new object[]{day.AddDays(-1)});
  Check(same.EndsWith("12:34")&&!same.Contains("56"),"Hoy HH:mm sin segundos");
  Check(same.Contains("01/10/2026")&&next.Contains("30/09/2026")&&next.EndsWith("12:34"),"Otro dia incluye fecha sin renovar timestamp");
  var fresh=View();Check(fresh.ElevatedWindowsTempAnalysis==null&&!fresh.Categories[1].HasElevatedAnalysisTime,"Nueva sesion sin snapshot");
  string storage=File.ReadAllText(Path.Combine(project,"Services","PortableStorage.cs"));
  Check(!storage.Contains("AnalysisFinishedAt")&&!storage.Contains("elevatedWindowsTemp"),"Snapshot no persistido");
  // Read only this empty controlled directory to verify the producer's actual timestamp.
  var provider=new CleanupAnalysisService(null);var began=DateTimeOffset.Now;
  var produced=(CleanupCategoryResult)typeof(CleanupAnalysisService).GetMethod("AnalyzeWindowsTemporaryResolved",BindingFlags.NonPublic|BindingFlags.Instance)
   .Invoke(provider,new object[]{new Func<string>(()=>fixture)});
  Check(produced.IsAvailable&&produced.AnalysisFinishedAt>=began&&produced.AnalysisFinishedAt<=DateTimeOffset.Now,"Timestamp al finalizar proveedor controlado");
  return count+" comprobaciones correctas; sin Windows Temp real, limpieza real ni UAC.";
 }
}
'@
$provider=[Microsoft.CSharp.CSharpCodeProvider]::new()
$parameters=[CodeDom.Compiler.CompilerParameters]::new()
$parameters.GenerateInMemory=$true
foreach($reference in @($exe,'System.dll','System.Core.dll','System.Xaml.dll',
 [System.Windows.Application].Assembly.Location,[System.Windows.Threading.Dispatcher].Assembly.Location)) {
 [void]$parameters.ReferencedAssemblies.Add($reference)
}
$compiled=$provider.CompileAssemblyFromSource($parameters,$source)
if($compiled.Errors.HasErrors) { throw ($compiled.Errors | Out-String) }
$fixture=Join-Path $PSScriptRoot ('snapshot-fixture-'+[Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($fixture)
try { [WindowsTempSnapshotChecks]::Run($project,$fixture) }
finally {
 if([IO.Path]::GetFullPath($fixture).StartsWith([IO.Path]::GetFullPath($PSScriptRoot)+'\',[StringComparison]::OrdinalIgnoreCase)) {
  [IO.Directory]::Delete($fixture,$true)
 }
}
