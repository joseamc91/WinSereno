$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $project 'bin\Debug\WinSereno.exe'
[void][Reflection.Assembly]::LoadFrom($exe)
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.Xaml
$source = @"
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using WinSereno.Models;
using WinSereno.Services;
using WinSereno.ViewModels;
public sealed class IntegrityCheckProgress<T> : IProgress<T> {
 readonly Action<T> receive; public IntegrityCheckProgress(Action<T> value) {receive=value;} public void Report(T value) {receive(value);}
}
public sealed class IntegrityCheckLogger : ISessionLogger {
 public string Text=""; public void Write(string text) {Text+=text+"\n";}
 public void TaskStarted(MaintenanceTask task) {Write(task.Id+" start");}
 public void TaskFinished(MaintenanceTask task,MaintenanceTaskResult result) {Write(task.Id+" "+result.StdOut+result.StdErr);}
 public void Dispose() {}
}
public sealed class IntegrityCheckDialogs : IDialogService {
 public void ShowOutput(TaskProgress progress) {} public void ShowMessage(string text) {throw new Exception(text);}
 public void ShowDiagnosticDetails(DiagnosticResult result) {}
 public bool ConfirmTask(MaintenanceTask task) {throw new Exception("No second confirmation allowed");}
 public bool ConfirmCancelAndClose() {return false;}
 public RestartAdapter SelectRestartAdapter(IReadOnlyList<RestartAdapter> adapters) {throw new Exception("No network allowed");}
}
public sealed class IntegrityCheckRunner : IMaintenanceTaskRunner {
 public bool IsActive {get {return false;}} public TaskProgress Current {get {return new TaskProgress();}}
 public event EventHandler<TaskProgress> ProgressChanged {add {} remove {}}
 public Task<MaintenanceTaskResult> RunAsync(MaintenanceTask task) {throw new Exception("No real tasks allowed");}
 public bool RequestCancellation() {throw new Exception("No real tasks allowed");} public Task WaitForIdleAsync() {return Task.CompletedTask;}
}
public static class DiagnosticIntegrityChecks {
 static int count; static readonly DateTimeOffset Epoch=new DateTimeOffset(2026,10,2,10,0,0,TimeSpan.Zero);
 static void Check(bool value,string name) {if(!value) throw new Exception(name);count++;}
 static MaintenanceTaskResult Tool(FindingStatus status,bool sfc=false,int code=0) {
  var r=new MaintenanceTaskResult {StartedAt=Epoch,FinishedAt=Epoch.AddSeconds(2),Duration=TimeSpan.FromSeconds(2),ExecutionStatus=ExecutionStatus.Success,FindingStatus=status,ExitCode=code,UserSummary="Fixture "+status,StdOut="stdout fixture",StdErr="stderr fixture"};
  typeof(MaintenanceTaskResult).GetProperty("CommandStarted").SetValue(r,true,null);return r;
 }
 static MaintenanceTaskResult Pair(FindingStatus store,FindingStatus files) {
  var r=new MaintenanceTaskResult {Duration=TimeSpan.FromSeconds(4)};
  r.SequenceSteps.Add(new SequenceStepResult {TaskId=ElevatedTaskCatalog.CheckHealthId,Result=Tool(store)});
  r.SequenceSteps.Add(new SequenceStepResult {TaskId=ElevatedTaskCatalog.SfcVerifyOnlyId,Result=Tool(files,true)});return r;
 }
 static void WaitUntil(Func<bool> done) {
  var frame=new DispatcherFrame();var deadline=DateTime.UtcNow.AddSeconds(8);
  var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(10)};
  timer.Tick+=(s,e)=>{if(done()||DateTime.UtcNow>deadline) frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);timer.Stop();
  if(!done()) throw new Exception("Simulation timeout");
 }
 static void Wait(Task task) {WaitUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult();Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.Background);}
 static void SetCancelable(OperationLease lease,bool value) {typeof(OperationLease).GetMethod("SetCancelable",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(lease,new object[]{value});}
 public static string Run(string project) {
  var app=new Application();SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
  var pending=DiagnosticService.CreatePendingResults();
  var explanations=new[]{"Comprueba si las unidades tienen suficiente espacio libre.","Consulta el estado básico que Windows informa de los discos físicos.","Comprueba si el equipo tiene red activa y acceso funcional a Internet.","Comprueba que los servicios esenciales de Windows estén funcionando.","Busca errores o avisos recientes relevantes en los registros de Windows.","Comprueba el almacén de componentes y los archivos protegidos de Windows sin repararlos."};
  Check(pending.Count==6,"Six checks");
  for(int i=0;i<6;i++) {Check(pending[i].Summary==explanations[i],"Exact explanation "+i);Check(pending[i].Status==DiagnosticStatus.NotChecked&&!pending[i].HasNavigation&&pending[i].Recommendation==null,"Pending explanation without repeated actions "+i);}
  Check(ElevatedTaskCatalog.IsAllowed(ElevatedTaskCatalog.DiagnosticIntegrityId)&&ElevatedTaskCatalog.IsAllowed(ElevatedTaskCatalog.SfcVerifyOnlyId),"Explicit allowlist");
  Check(!ElevatedTaskCatalog.IsAllowed("diagnosis.any-command"),"Unknown id denied");
  Check(DiagnosticIntegritySequence.TaskIds.SequenceEqual(new[]{ElevatedTaskCatalog.CheckHealthId,ElevatedTaskCatalog.SfcVerifyOnlyId}),"Exactly two fixed integrity tasks");
  Check(ElevatedTaskCatalog.Get(ElevatedTaskCatalog.CheckHealthId).Arguments=="/Online /Cleanup-Image /CheckHealth /English","Exact CheckHealth");
  Check(ElevatedTaskCatalog.Get(ElevatedTaskCatalog.SfcVerifyOnlyId).Arguments=="/verifyonly","Exact verifyonly");
  Check(DiagnosticIntegritySequence.TaskIds.All(id=>ElevatedTaskCatalog.Get(id).ImpactLevel==ImpactLevel.Information),"Read-only catalog classifications");
  Check(!DiagnosticIntegritySequence.TaskIds.Any(id=>id==ElevatedTaskCatalog.SfcId||id==ElevatedTaskCatalog.ScanHealthId||id==ElevatedTaskCatalog.RestoreHealthId),"No repair/deep scan commands in diagnostics");
  Check(ElevatedTaskCatalog.Get(ElevatedTaskCatalog.SfcId).Arguments=="/scannow"&&RepairCompleteSequence.TaskIds.Count==3,"Existing Repair unchanged");
  foreach(var sample in new[]{new[]{"No component store corruption detected.","Healthy"},new[]{"The component store is repairable.","RepairRequired"},new[]{"The component store cannot be repaired.","Unrepairable"},new[]{"The component store is not repairable.","Unrepairable"},new[]{"The operation completed successfully.","Unknown"}}) {
   var tool=Tool(FindingStatus.Unknown);tool.StdOut=sample[0];RepairResultInterpreter.Apply(ElevatedTaskCatalog.CheckHealthId,tool);
   Check(tool.FindingStatus==(FindingStatus)Enum.Parse(typeof(FindingStatus),sample[1]),"Existing DISM parser retained "+sample[0]);
  }
  var samples=new[]{new[]{"Windows Resource Protection did not find any integrity violations.","Healthy"},new[]{"Protección de recursos de Windows no encontró ninguna infracción de integridad.","Healthy"},new[]{"Windows Resource Protection found integrity violations.","Attention"},new[]{"Protección de recursos de Windows encontró infracciones de integridad.","Attention"},new[]{"Windows Resource Protection could not perform the requested operation.","ScanFailed"},new[]{"Protección de recursos de Windows no pudo realizar la operación solicitada.","ScanFailed"}};
  foreach(var sample in samples) {
   var expected=(FindingStatus)Enum.Parse(typeof(FindingStatus),sample[1]);
   Check(SfcVerifyOnlyResultParser.Parse(sample[0])==expected,"ES/EN parse "+sample[1]);
   Check(SfcVerifyOnlyResultParser.Parse(sample[0].Replace("Windows ","Windows\r\n"))==expected,"Wrapped output");
   var tool=Tool(FindingStatus.Unknown,true);tool.StdOut=sample[0];SfcVerifyOnlyResultParser.Apply(tool);
   Check(tool.FindingStatus==expected,"Applied verify result");
   Check(!tool.RequiresRestart&&tool.FindingStatus!=FindingStatus.Repaired&&tool.FindingStatus!=FindingStatus.RepairFailed,"Verify never claims repair");
  }
  foreach(var text in new[]{"", "Unknown",samples[0][0]+"\n"+samples[2][0],"Windows Resource Protection found corrupt files and successfully repaired them."}) Check(SfcVerifyOnlyResultParser.Parse(text)==FindingStatus.Unknown,"Ambiguous/repair-only output remains Unknown");
  var violations=Tool(FindingStatus.Unknown,true,1);violations.StdOut=samples[2][0];SfcVerifyOnlyResultParser.Apply(violations);Check(violations.ExecutionStatus==ExecutionStatus.Success&&violations.FindingStatus==FindingStatus.Attention,"Nonzero violation result is not technical failure");
  var denied=Tool(FindingStatus.Unknown,true,5);denied.StdOut="Access denied";SfcVerifyOnlyResultParser.Apply(denied);Check(denied.ExecutionStatus==ExecutionStatus.Failed&&denied.FindingStatus==FindingStatus.Unknown,"Real execution failure");
  var ambiguous=Tool(FindingStatus.Unknown,true);ambiguous.StdOut="Unknown";SfcVerifyOnlyResultParser.Apply(ambiguous);Check(ambiguous.FindingStatus==FindingStatus.Unknown,"ExitCode 0 does not imply healthy");
  foreach(var store in new[]{FindingStatus.Healthy,FindingStatus.RepairRequired,FindingStatus.Unrepairable,FindingStatus.Unknown}) foreach(var files in new[]{FindingStatus.Healthy,FindingStatus.Attention,FindingStatus.ScanFailed,FindingStatus.Unknown}) {
   var r=Pair(store,files);var card=DiagnosticIntegritySequence.ToDiagnostic(r);
   var expected=store==FindingStatus.Unrepairable?DiagnosticStatus.Error:store==FindingStatus.RepairRequired||files==FindingStatus.Attention?DiagnosticStatus.Attention:store==FindingStatus.Healthy&&files==FindingStatus.Healthy?DiagnosticStatus.Healthy:DiagnosticStatus.NotChecked;
   Check(card.Status==expected,"Combined "+store+" / "+files);
   Check(card.HasNavigation==(expected==DiagnosticStatus.Error||expected==DiagnosticStatus.Attention),"Conditional Repair navigation");
   Check(card.Recommendation==(card.HasNavigation?"Revisar las opciones de Reparación; ninguna se ha ejecutado automáticamente.":null),"Recomendación solo con Atención/Error");
   Check(card.DetailedDescription.Contains("Almacén de componentes")&&card.DetailedDescription.Contains("Archivos protegidos")&&card.DetailedDescription.Contains("Duración:")&&card.DetailedDescription.Contains("stdout fixture")&&card.DetailedDescription.Contains("stderr fixture"),"Independent details/output/durations");
  }
  Check(DiagnosticIntegritySequence.ToDiagnostic(Pair(FindingStatus.RepairRequired,FindingStatus.Healthy)).Summary=="El almacén de componentes necesita reparación; los archivos protegidos no presentan infracciones. No se realizó ninguna reparación.","Resumen exacto del caso real; SFC no acusado");
  Check(DiagnosticIntegritySequence.ToDiagnostic(Pair(FindingStatus.Healthy,FindingStatus.Healthy)).Summary=="No se detectaron problemas de integridad en Windows.","Ambos correctos");
  Check(DiagnosticIntegritySequence.ToDiagnostic(Pair(FindingStatus.Healthy,FindingStatus.Attention)).Summary=="El almacén de componentes no presenta corrupción; se detectaron infracciones en archivos protegidos. No se realizó ninguna reparación.","Solo infracciones SFC; almacén no acusado");
  foreach(var store in new[]{FindingStatus.RepairRequired,FindingStatus.Unrepairable})
   Check(DiagnosticIntegritySequence.ToDiagnostic(Pair(store,FindingStatus.Attention)).Summary=="Se detectaron problemas tanto en el almacén de componentes como en los archivos protegidos. No se realizó ninguna reparación.","Problemas en ambos componentes");
  Check(DiagnosticIntegritySequence.ToDiagnostic(Pair(FindingStatus.Unknown,FindingStatus.Unknown)).Summary=="No se pudieron comprobar completamente el almacén de componentes ni los archivos protegidos. No se realizó ninguna reparación.","Ambos sin comprobar");
  Check(DiagnosticIntegritySequence.ToDiagnostic(new MaintenanceTaskResult()).Summary=="No se pudieron comprobar completamente el almacén de componentes ni los archivos protegidos. No se realizó ninguna reparación.","Ambos pasos ausentes sin inventar resultados");
  Check(DiagnosticIntegritySequence.ToDiagnostic(Pair(FindingStatus.RepairRequired,FindingStatus.Unknown)).Summary=="El almacén de componentes necesita reparación; no se pudieron comprobar completamente los archivos protegidos. No se realizó ninguna reparación.","Problema DISM con SFC incompleto");
  Check(DiagnosticIntegritySequence.ToDiagnostic(Pair(FindingStatus.Unrepairable,FindingStatus.Healthy)).Summary=="El almacén de componentes presenta corrupción no reparable; los archivos protegidos no presentan infracciones. No se realizó ninguna reparación.","Error DISM sin atribuir problemas a SFC");
  Check(DiagnosticIntegritySequence.ToDiagnostic(Pair(FindingStatus.Unknown,FindingStatus.Healthy)).Summary=="No se pudo comprobar completamente el almacén de componentes; los archivos protegidos no presentan infracciones. No se realizó ninguna reparación.","DISM desconocido y SFC correcto");
  foreach(var files in new[]{FindingStatus.Unknown,FindingStatus.ScanFailed})
   Check(DiagnosticIntegritySequence.ToDiagnostic(Pair(FindingStatus.Healthy,files)).Summary=="El almacén de componentes no presenta corrupción; no se pudieron comprobar completamente los archivos protegidos. No se realizó ninguna reparación.","SFC incompleto sin inventar infracciones");
  foreach(int incomplete in new[]{0,1,2}) {
   var partial=Pair(FindingStatus.Healthy,FindingStatus.Healthy);var step=partial.SequenceSteps[1];
   if(incomplete==0) step.WasSkipped=true;
   else if(incomplete==1) step.Result.ExecutionStatus=ExecutionStatus.Failed;
   else typeof(MaintenanceTaskResult).GetProperty("CommandStarted").SetValue(step.Result,false,null);
   var card=DiagnosticIntegritySequence.ToDiagnostic(partial);
   Check(card.Summary.Contains("no se pudieron comprobar completamente los archivos protegidos")&&!card.HasNavigation,"Paso omitido/fallido/no iniciado no inventa resultado ni acción");
  }
  var independent=new List<string>();Wait(DiagnosticIntegritySequence.RunAsync(id=>{independent.Add(id);return Task.FromResult(id==ElevatedTaskCatalog.CheckHealthId?new MaintenanceTaskResult {ExecutionStatus=ExecutionStatus.Failed}:Tool(FindingStatus.Healthy,true));}));
  Check(independent.SequenceEqual(DiagnosticIntegritySequence.TaskIds),"Failed check does not skip other fixed check");
  foreach(bool cancelled in new[]{false,true}) {
   var results=new List<DiagnosticResult>();var ops=new OperationCoordinator();int elevations=0,normals=0;var logger=new IntegrityCheckLogger();
   var service=new DiagnosticService(null,logger,new IntegritySessionState(),async (lease,normal,progress)=>{elevations++;await normal();return cancelled?new MaintenanceTaskResult {ExecutionStatus=ExecutionStatus.Cancelled}:Pair(FindingStatus.Healthy,FindingStatus.Healthy);},(id,token)=>{normals++;return Task.FromResult(new DiagnosticResult {Id=id,Name=id,Status=DiagnosticStatus.Healthy,Summary="Normal fixture"});});
   using(var lease=ops.Begin("Diagnóstico",true)) Wait(service.RunAsync(new IntegrityCheckProgress<DiagnosticResult>(results.Add),lease,new IntegrityCheckProgress<TaskProgress>(p=>{})));
   Check(elevations==1&&normals==5&&results.Count==6,"One elevation, five normal checks, six results");
   Check(results.Where(r=>r.Id!="integrity").All(r=>r.Status==DiagnosticStatus.Healthy),"Normal results retained");
   var card=results.Single(r=>r.Id=="integrity");Check(card.Status==(cancelled?DiagnosticStatus.NotChecked:DiagnosticStatus.Healthy),"UAC cancellation mapping");
   if(cancelled) Check(card.Summary=="No se comprobó la integridad porque se cancelaron los permisos de administrador.","Exact UAC cancellation explanation");
   Check(!ops.IsActive&&logger.Text.Contains("Integridad combinada"),"Coordinator released and combined log");
  }
  var operations=new OperationCoordinator();var dialogs=new IntegrityCheckDialogs();var log=new IntegrityCheckLogger();var session=new IntegritySessionState();
  var partialResults=new List<DiagnosticResult>();var partialLog=new IntegrityCheckLogger();int attempted=0;
  var partialService=new DiagnosticService(null,partialLog,session,async (lease,normal,progress)=>{await normal();return Pair(FindingStatus.Healthy,FindingStatus.Healthy);},(id,token)=>{
   attempted++;if(id=="events") throw new InvalidOperationException("Fixture access denied");return Task.FromResult(new DiagnosticResult {Id=id,Status=DiagnosticStatus.Healthy});
  });
  using(var lease=operations.Begin("Diagnóstico",true)) Wait(partialService.RunAsync(new IntegrityCheckProgress<DiagnosticResult>(partialResults.Add),lease,new IntegrityCheckProgress<TaskProgress>(p=>{})));
  Check(attempted==5&&partialResults.Count==6,"Normal failure does not suppress remaining checks or integrity");
  Check(partialResults.Single(r=>r.Id=="events").Status==DiagnosticStatus.NotChecked&&partialResults.Single(r=>r.Id=="integrity").Status==DiagnosticStatus.Healthy,"Independent failure results");
  Check(partialLog.Text.Contains("Fixture access denied")&&!operations.IsActive,"Normal exception logged and lease released");
  var gate=new TaskCompletionSource<bool>();int launches=0;OperationLease active=null;
  var orchestration=new DiagnosticService(null,log,session,async (lease,normal,progress)=>{
   launches++;active=lease;await normal();SetCancelable(lease,false);
   progress(new TaskProgress {State=RunnerState.Running,CurrentTask=ElevatedTaskCatalog.Get(ElevatedTaskCatalog.DiagnosticIntegrityId),StepLabel="Paso 2 de 2",Percentage=37});
   await gate.Task;return Pair(FindingStatus.Healthy,FindingStatus.Attention);
  },(id,token)=>Task.FromResult(new DiagnosticResult {Id=id,Name=id,Status=DiagnosticStatus.Healthy,Summary="Fixture"}));
  var vm=new DiagnosticViewModel(orchestration,operations,log,dialogs,session);
  var main=new MainViewModel(new PortableStorage(project),new AppSettings(),new ThemeService(),dialogs,new IntegrityCheckRunner(),new HomeViewModel(null,log),vm,operations,session,log);
  var analysis=vm.AnalyzeAsync();WaitUntil(()=>vm.IsIntegrityRunning);
  Check(operations.IsActive&&operations.Name=="Diagnóstico"&&!operations.CanBeCancelled,"One noncancelable integrity phase in same lease");
  Check(!vm.CancelCommand.CanExecute(null)&&!vm.AnalyzeCommand.CanExecute(null)&&!operations.RequestCancellation(),"Cancellation and concurrent operation disabled");
  bool exclusive=false;try {operations.Begin("Other",false);} catch(InvalidOperationException) {exclusive=true;}Check(exclusive,"Global exclusivity");
  Check(vm.Summary.Contains("Comprobando integridad de Windows")&&vm.Summary.Contains("37"),"Real fixture progress in existing summary");
  Check(main.ActionHistory.Count==0,"No internal history before completion");
  gate.SetResult(true);Wait(analysis);
  Check(!operations.IsActive&&!vm.IsRunning,"Operation fully released");
  Check(launches==1&&main.ActionHistory.Count==1&&main.ActionHistory.Single().Name=="Diagnóstico","Exactly one main activity entry");
  Check(main.ActionHistory.Single().CreateDetailsProgress().StdOut.Contains("Almacén de componentes")&&main.ActionHistory.Single().CreateDetailsProgress().StdOut.Contains("Archivos protegidos"),"Main history contains integrity details");
  Check(vm.Results.Single(r=>r.Id=="integrity").Status==DiagnosticStatus.Attention,"Integrated result replaces pending explanation");
  main.Navigate(NavigationSection.Activity);Check(main.ActionHistory.Count==1,"Navigation does not lose entry");
  session.Update(ElevatedTaskCatalog.CheckHealthId,Tool(FindingStatus.Unrepairable));Check(vm.Results.Single(r=>r.Id=="integrity").Status==DiagnosticStatus.Attention,"Repair session does not overwrite diagnostic result");
  string runner=File.ReadAllText(Path.Combine(project,"Services","MaintenanceTaskRunner.cs"));
  string worker=File.ReadAllText(Path.Combine(project,"Services","ElevatedWorker.cs"));
  Check(runner.Contains("externalLease == null ? operations.Begin")&&runner.Contains("internalProgress ?? Publish"),"Shared transport avoids second operation/public publication");
  Check(runner.Contains("NativeErrorCode == 1223")&&runner.Contains("if (!normalRan) await beforeIntegrity()"),"Existing UAC cancellation plus normal-check fallback");
  Check(worker.Contains("reader.ReadBoolean()")&&worker.Contains("DiagnosticIntegritySequence.RunAsync"),"Authenticated worker waits before fixed sequence");
  Check(System.Text.RegularExpressions.Regex.Matches(runner,"Process.Start\\(new ProcessStartInfo").Count==1,"Exactly one shared worker launch site");
  Check(!runner.Contains("worker.Kill(")&&!worker.Contains("process.Kill("),"No forced termination of integrity commands");
  Check(File.ReadAllText(Path.Combine(project,"Services","FixedTaskProcess.cs")).Contains("taskId == ElevatedTaskCatalog.SfcId || taskId == ElevatedTaskCatalog.SfcVerifyOnlyId ? Encoding.Unicode"),"VerifyOnly uses SFC Unicode decoding");
  foreach(string text in new[]{"GetNamedPipeClientProcessId","PipeSecurity","RandomNumberGenerator.Create()","Handshake IPC inválido"}) Check(runner.Contains(text),"Security retained "+text);
  Check(worker.Contains("GetNamedPipeServerProcessId")&&worker.Contains("parent.MainModule.FileName"),"Worker PID/executable validation");
  Check(!runner.Contains("logger.Write(nonce")&&!runner.Contains("logger.Write(args[3]"),"Nonce not logged");
  Check(File.ReadAllText(Path.Combine(project,"app.manifest")).Contains("level=\"asInvoker\""),"Main remains asInvoker");
  return count+" comprobaciones correctas; parsers ES/EN, diagnóstico simulado, UAC simulado, exclusión e historial único; sin comandos reales ni UAC.";
 }
}
"@
$provider = [Microsoft.CSharp.CSharpCodeProvider]::new()
$parameters = [CodeDom.Compiler.CompilerParameters]::new()
$parameters.GenerateInMemory = $true
foreach ($reference in @($exe,'System.dll','System.Core.dll','System.Xml.dll','System.Xaml.dll','System.Runtime.Serialization.dll',
    [System.Windows.Application].Assembly.Location,[System.Windows.Media.Visual].Assembly.Location,[System.Windows.DependencyObject].Assembly.Location)) { [void]$parameters.ReferencedAssemblies.Add($reference) }
$compiled = $provider.CompileAssemblyFromSource($parameters,$source)
if ($compiled.Errors.HasErrors) { throw ($compiled.Errors | Out-String) }
[DiagnosticIntegrityChecks]::Run($project)
