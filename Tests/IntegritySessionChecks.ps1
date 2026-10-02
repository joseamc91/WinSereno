$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $project 'bin\Debug\WinSereno.exe'
[void][Reflection.Assembly]::LoadFrom($exe)
$source = @'
using System;
using System.Linq;
using System.Reflection;
using WinSereno.Models;
using WinSereno.Services;
public static class IntegritySessionChecks {
 static int count;
 static readonly DateTimeOffset Epoch=new DateTimeOffset(2026,10,1,8,0,0,TimeSpan.Zero);
 static readonly string CheckId=ElevatedTaskCatalog.CheckHealthId, ScanId=ElevatedTaskCatalog.ScanHealthId, RestoreId=ElevatedTaskCatalog.RestoreHealthId;
 static void Check(bool value,string name) { if(!value) throw new Exception(name); count++; }
 static MaintenanceTaskResult Result(string label,int end,FindingStatus finding=FindingStatus.Healthy,ExecutionStatus execution=ExecutionStatus.Success,bool ran=true) {
  var r=new MaintenanceTaskResult {StartedAt=Epoch.AddMinutes(end-1),FinishedAt=Epoch.AddMinutes(end),
   Duration=TimeSpan.FromMinutes(1),FindingStatus=finding,ExecutionStatus=execution,ExitCode=ran?(int?)0:null,UserSummary=label,StdOut=label};
  typeof(MaintenanceTaskResult).GetProperty("CommandStarted").SetValue(r,ran,null); return r;
 }
 static void Expect(IntegritySessionState state,MaintenanceTaskResult winner,string id,DiagnosticStatus status=DiagnosticStatus.Healthy) {
  var view=state.Read(); Check(view.Summary==winner.UserSummary,"Gana resultado por timestamp: "+winner.UserSummary);
  Check(view.DetailedDescription.Contains(id),"Detalles del comando elegido");
  Check(view.Status==status,"Semantica funcional");
  Check(view.Duration==winner.Duration,"Duracion del resultado seleccionado");
 }
 public static string Run() {
  foreach(string id in new[]{CheckId,ScanId,RestoreId}) {
   var state=new IntegritySessionState();var r=Result("Solo "+id,10);state.Update(id,r);Expect(state,r,id);
  }
  string[][] pairs={new[]{ScanId,RestoreId},new[]{RestoreId,ScanId},new[]{RestoreId,CheckId},new[]{CheckId,ScanId}};
  foreach(var pair in pairs) {
   var state=new IntegritySessionState();var old=Result("Old "+pair[0],10,FindingStatus.Repaired);
   var latest=Result("New "+pair[1],20,FindingStatus.RepairRequired);
   state.Update(pair[0],old);state.Update(pair[1],latest);Expect(state,latest,pair[1],DiagnosticStatus.Attention);
  }
  var ids=new[]{ScanId,RestoreId,CheckId};var values=new[]{Result("Scan 10:00",120,FindingStatus.RepairRequired),
   Result("Restore 10:30",150,FindingStatus.Repaired),Result("Check 10:40",160)};
  foreach(var order in new[]{new[]{0,1,2},new[]{0,2,1},new[]{1,0,2},new[]{1,2,0},new[]{2,0,1},new[]{2,1,0}}) {
   var state=new IntegritySessionState();foreach(int i in order)state.Update(ids[i],values[i]);Expect(state,values[2],CheckId);
  }
  // Same finishing instant: latest actual start; complete tie: smallest ordinal TaskId.
  foreach(bool reverse in new[]{false,true}) {
   var state=new IntegritySessionState();var a=Result("Same finish old start",30);a.StartedAt=Epoch;
   var b=Result("Same finish new start",30);
   if(reverse){state.Update(ScanId,b);state.Update(CheckId,a);}else{state.Update(CheckId,a);state.Update(ScanId,b);}
   Expect(state,b,ScanId);
   var tie=new IntegritySessionState();var check=Result("Ordinal Check",40);var restore=Result("Ordinal Restore",40);var scan=Result("Ordinal Scan",40);
   if(reverse){tie.Update(ScanId,scan);tie.Update(RestoreId,restore);tie.Update(CheckId,check);}
   else{tie.Update(CheckId,check);tie.Update(RestoreId,restore);tie.Update(ScanId,scan);}
   Expect(tie,check,CheckId);
  }
  var preserved=new IntegritySessionState();var previous=Result("Previous real",10);preserved.Update(CheckId,previous);
  int events=0;preserved.Changed+=(s,e)=>events++;
  preserved.Update(CheckId,Result("UAC cancelled",20,FindingStatus.Unknown,ExecutionStatus.Cancelled,false));
  Expect(preserved,previous,CheckId);Check(events==0,"Cancelacion no reemplaza ni notifica");
  preserved.Update(RestoreId,Result("Worker failed before process",30,FindingStatus.Unknown,ExecutionStatus.Failed,false));
  Expect(preserved,previous,CheckId);Check(preserved.Get(RestoreId)==null&&events==0,"Fallo sin inicio no registrado");
  preserved.Update(ScanId,Result("Cancelled before command",40,FindingStatus.Unknown,ExecutionStatus.Cancelled,false));
  Expect(preserved,previous,CheckId);
  var empty=new IntegritySessionState();Check(empty.Read().Status==DiagnosticStatus.NotChecked,"Ausencia total");
  empty.Update(ScanId,Result("Not executed",30,FindingStatus.Unknown,ExecutionStatus.Failed,false));
  Check(empty.Read().Status==DiagnosticStatus.NotChecked && empty.Get(ScanId)==null,"Sin resultado administrativo valido");
  var files=Result("SFC separate newer",100,FindingStatus.RepairFailed);
  preserved.Update(ElevatedTaskCatalog.SfcId,files);Expect(preserved,previous,CheckId);
  Check(object.ReferenceEquals(preserved.Get(ElevatedTaskCatalog.SfcId),files)&&preserved.Read().DetailedDescription.Contains(files.UserSummary),"SFC separado intacto");
  var onlySfc=new IntegritySessionState();onlySfc.Update(ElevatedTaskCatalog.SfcId,files);
  Check(onlySfc.Read().Status==DiagnosticStatus.NotChecked&&onlySfc.Read().DetailedDescription.Contains(files.UserSummary),"Solo SFC no sustituye DISM");
  var same=new IntegritySessionState();var newest=Result("Newest same task",80,FindingStatus.RepairRequired);
  same.Update(ScanId,newest);same.Update(ScanId,Result("Old arriving later",20));
  Expect(same,newest,ScanId,DiagnosticStatus.Attention);
  Check(object.ReferenceEquals(same.Get(ScanId),newest),"Cache misma tarea conserva mas reciente");
  foreach(var finding in new[]{FindingStatus.Unknown,FindingStatus.Failed,FindingStatus.Unrepairable,FindingStatus.RepairFailed,FindingStatus.SourceFilesNotFound}) {
   var state=new IntegritySessionState();state.Update(RestoreId,Result("Previous repaired",10,FindingStatus.Repaired));
   var r=Result("Latest "+finding,20,finding);state.Update(ScanId,r);
   Expect(state,r,ScanId,finding==FindingStatus.Unknown||finding==FindingStatus.Failed?DiagnosticStatus.NotChecked:DiagnosticStatus.Error);
  }
  var failed=Result("Executed failed",20,FindingStatus.Unknown,ExecutionStatus.Failed);
  var failState=new IntegritySessionState();failState.Update(RestoreId,Result("Old repaired",10,FindingStatus.Repaired));failState.Update(ScanId,failed);
  Expect(failState,failed,ScanId,DiagnosticStatus.NotChecked);
  var ipc=Result("Started but completion unknown",30,FindingStatus.Unknown,ExecutionStatus.Failed);ipc.ExitCode=null;
  failState.Update(CheckId,ipc);Expect(failState,ipc,CheckId,DiagnosticStatus.NotChecked);
  foreach(int invalid in new[]{0,1,2}) {
   var r=Result("Invalid timestamp",200);
   if(invalid==0)r.FinishedAt=default(DateTimeOffset);if(invalid==1)r.StartedAt=default(DateTimeOffset);if(invalid==2)r.FinishedAt=r.StartedAt.AddSeconds(-1);
   preserved.Update(ScanId,r);Check(preserved.Read().Summary==previous.UserSummary,"Sin timestamp fiable no desplaza");
  }
  var offsetState=new IntegritySessionState();var earlier=Result("Earlier UTC",40);var later=Result("Later UTC different offset",50);
  later.StartedAt=later.StartedAt.ToOffset(TimeSpan.FromHours(-5));later.FinishedAt=later.FinishedAt.ToOffset(TimeSpan.FromHours(-5));
  offsetState.Update(ScanId,later);offsetState.Update(RestoreId,earlier);Expect(offsetState,later,ScanId);
  preserved.Update("repair.complete",Result("Aggregate is not component store",400));
  Expect(preserved,previous,CheckId);Check(new IntegritySessionState().Read().Status==DiagnosticStatus.NotChecked,"Nueva sesion sin historial");
  return count+" comprobaciones nuevas correctas; sin DISM/SFC, UAC ni persistencia de resultados.";
 }
}
'@
$provider=[Microsoft.CSharp.CSharpCodeProvider]::new()
$parameters=[CodeDom.Compiler.CompilerParameters]::new()
$parameters.GenerateInMemory=$true
foreach($reference in @($exe,'System.dll','System.Core.dll')) { [void]$parameters.ReferencedAssemblies.Add($reference) }
$compiled=$provider.CompileAssemblyFromSource($parameters,$source)
if($compiled.Errors.HasErrors) { throw ($compiled.Errors | Out-String) }
[IntegritySessionChecks]::Run()
