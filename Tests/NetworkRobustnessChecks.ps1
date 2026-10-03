param([switch]$CompileOnly)
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $project 'bin\Debug\WinSereno.exe'
if(-not $CompileOnly) { [void][Reflection.Assembly]::LoadFrom($exe) }
$source = @'
using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Reflection;
using WinSereno.Models;
using WinSereno.Services;
public static class NetworkRobustnessChecks {
 static int count;
 static readonly Assembly Assembly=typeof(MaintenanceTaskResult).Assembly;
 static readonly Type ProbeType=Assembly.GetType("WinSereno.Services.ConnectivityProbe",true);
 static readonly Type CheckType=Assembly.GetType("WinSereno.Services.NetworkDiagnosticCheck",true);
 static void Check(bool ok,string label){if(!ok)throw new Exception(label);count++;}
 static object Probe(string name,bool success,string detail){var p=Activator.CreateInstance(ProbeType);ProbeType.GetProperty("Name").SetValue(p,name,null);ProbeType.GetProperty("Success").SetValue(p,success,null);ProbeType.GetProperty("Detail").SetValue(p,detail,null);return p;}
 static bool Success(object p){return (bool)ProbeType.GetProperty("Success").GetValue(p,null);}
 static bool Timeout(object p){return (bool)ProbeType.GetProperty("TimedOut").GetValue(p,null);}
 static string Detail(object p){return (string)ProbeType.GetProperty("Detail").GetValue(p,null);}
 static object Https(int code,string endpoint="https://www.microsoft.com/"){return CheckType.GetMethod("HttpsResponse",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{code,new Uri(endpoint),TimeSpan.FromSeconds(1)});}
 static object HttpsTimeout(){return CheckType.GetMethod("HttpsTimeout",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{TimeSpan.FromSeconds(5)});}
 static DiagnosticResult Evaluate(string label,bool gateway,bool ip,bool dns,object https,DiagnosticStatus expected,bool unknownAdapter=false,bool linkWarning=false){
  object[] values={Probe("Gateway",gateway,gateway?"respuesta ICMP recibida":"sin respuesta ICMP válida"),Probe("IP pública",ip,ip?"respuesta ICMP recibida":"sin respuesta ICMP válida"),Probe("DNS",dns,dns?"www.microsoft.com resuelto":"no se pudo resolver"),https};
  var probes=Array.CreateInstance(ProbeType,4);var details=new StringBuilder();for(int i=0;i<4;i++){probes.SetValue(values[i],i);details.AppendLine((Success(values[i])?"✓ ":"— ")+ProbeType.GetProperty("Name").GetValue(values[i],null)+": "+Detail(values[i]));}
  string before=string.Join("|",values.Select(p=>Success(p)+"/"+Timeout(p)+"/"+Detail(p)));
  var result=(DiagnosticResult)CheckType.GetMethod("Summarize",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{unknownAdapter,linkWarning,probes,details.ToString()});
  Check(result.Status==expected,label+": overall "+result.Status+" expected "+expected);
  Check(result.DetailedDescription.Contains(Detail(https)),label+": individual HTTPS result retained in details");
  Check(before==string.Join("|",values.Select(p=>Success(p)+"/"+Timeout(p)+"/"+Detail(p))),label+": aggregate never fabricates successful probes");
  Check(result.NavigationTarget==NavigationSection.Network&&result.NavigationLabel=="Ir a Red",label+": contextual navigation unchanged");
  return result;
 }
 static MaintenanceTaskResult Result(string text,int? exit,string error=""){
  var result=new MaintenanceTaskResult {StdOut=text,StdErr=error,ExitCode=exit,StartedAt=DateTimeOffset.Now.AddSeconds(-2),FinishedAt=DateTimeOffset.Now,Duration=TimeSpan.FromSeconds(2)};
  typeof(MaintenanceTaskResult).GetProperty("CommandStarted").SetValue(result,exit.HasValue,null);return result;
 }
 static MaintenanceTaskResult Tcp(string label,string text,int? exit,ExecutionStatus execution,FindingStatus finding,bool restart=false,string error=""){
  var r=Result(text,exit,error);RepairResultInterpreter.Apply(ElevatedTaskCatalog.ResetTcpIpId,r);
  Check(r.ExecutionStatus==execution&&r.FindingStatus==finding,label+": "+r.ExecutionStatus+" / "+r.FindingStatus);
  Check(r.RequiresRestart==restart,label+": restart comes only from output");
  Check(r.ExitCode==exit&&r.StdOut==text&&r.StdErr==error,label+": original exit/stdout/stderr unchanged");
  return r;
 }
 public static string Run(string project){
  // Test production response/timeout mapping and the production aggregate without invoking any probe.
  var ok=Https(200);var rejected=Https(403);var timeout=HttpsTimeout();
  Check(Success(ok)&&!Timeout(ok)&&Detail(ok).Contains("HTTP 200"),"200 confirms HTTPS transport");
  Check(Success(rejected)&&!Timeout(rejected)&&Detail(rejected).Contains("HTTP 403"),"403 after TLS confirms transport, preserves endpoint rejection");
  Check(!Success(timeout)&&Timeout(timeout)&&Detail(timeout).Contains("5 s"),"Timeout remains explicitly unconfirmed and bounded");
  Check((TimeSpan)ProbeType.GetProperty("Duration").GetValue(timeout,null)==TimeSpan.FromSeconds(5),"Real probe duration retained");
  Evaluate("A: HTTPS 200",true,true,true,ok,DiagnosticStatus.Healthy);
  Evaluate("B: HTTPS 403",true,true,true,rejected,DiagnosticStatus.Healthy);
  var c=Evaluate("C: HTTPS timeout with three positive signals",true,true,true,timeout,DiagnosticStatus.Healthy);
  Check(c.Summary=="Internet disponible según gateway, IP pública y DNS. La comprobación HTTPS específica no respondió dentro del tiempo esperado.","C: transparent summary, no invented HTTPS success");
  Check(c.DetailedDescription.Contains("— HTTPS: timeout")&&!c.DetailedDescription.Contains("✓ HTTPS:"),"C: timeout remains visible as a failed individual check");
  Evaluate("D: DNS and HTTPS fail",true,true,false,timeout,DiagnosticStatus.Attention);
  Evaluate("E: all signals fail",false,false,false,timeout,DiagnosticStatus.Attention);
  Evaluate("F: DNS alone does not confirm Internet",false,false,true,timeout,DiagnosticStatus.Attention);
  // Removing any part of the supporting evidence must prevent the timeout exception.
  foreach(var signals in new[]{new[]{false,true,true},new[]{true,false,true},new[]{true,true,false},new[]{false,false,true},new[]{false,true,false},new[]{true,false,false},new[]{false,false,false}})
   Evaluate("Incomplete timeout evidence "+string.Join("/",signals),signals[0],signals[1],signals[2],HttpsTimeout(),DiagnosticStatus.Attention);
  var unknown=Evaluate("No adapter and no evidence",false,false,false,HttpsTimeout(),DiagnosticStatus.NotChecked,true);
  Check(unknown.Summary.Contains("información suficiente"),"Unavailable connectivity is not asserted to be a Windows failure");
  Evaluate("ICMP blocked but HTTPS works",false,false,true,Https(200),DiagnosticStatus.Healthy);
  Evaluate("Direct HTTPS confirmation despite separate DNS probe failure",false,false,false,Https(403),DiagnosticStatus.Healthy);
  var proxy=Https(407);Check(!Success(proxy)&&!Timeout(proxy),"407 is a proxy warning, not a timeout");
  Evaluate("407 with otherwise positive evidence",true,true,true,proxy,DiagnosticStatus.Attention);
  foreach(string failure in new[]{"TrustFailure","ConnectFailure","NameResolutionFailure"})
   Evaluate("Explicit HTTPS transport failure "+failure,true,true,true,Probe("HTTPS",false,"no confirmado ("+failure+")"),DiagnosticStatus.Attention);
  foreach(string endpoint in new[]{"http://www.microsoft.com/","https://example.invalid/"}){
   var other=Https(200,endpoint);Check(!Success(other),"Foreign/plain HTTP endpoint cannot confirm HTTPS");
   Evaluate("Unexpected response endpoint",true,true,true,other,DiagnosticStatus.Attention);
  }
  var link=Evaluate("Ethernet negotiation warning survives timeout fallback",true,true,true,HttpsTimeout(),DiagnosticStatus.Attention,false,true);
  Check(link.Summary.Contains("100 Mbps")&&link.Summary.Contains("HTTPS específica"),"Link warning and endpoint timeout both remain visible");
  Evaluate("Ethernet negotiation warning survives HTTPS 200",true,true,true,Https(200),DiagnosticStatus.Attention,false,true);

  foreach(string lang in new[]{"es","en"}){
   string text=File.ReadAllText(Path.Combine(project,"Tests","Fixtures","TcpIpResetPartial."+lang+".txt"),Encoding.UTF8);
   var partial=Tcp("Windows 10 partial "+lang,text,1,ExecutionStatus.Success,FindingStatus.PartiallyCompleted,true);
   Check(partial.UserSummary.Contains("parcialmente TCP/IP")&&partial.UserSummary.Contains("no pudieron modificarse")&&partial.UserSummary.Contains("3 pasos"),"Partial summary acknowledges applied steps and failures "+lang);
   Check(partial.UserSummary.Contains("debes reiniciar")&&partial.UserSummary.Contains("nunca reinicia automáticamente"),"Partial restart instruction explicit "+lang);
   var history=new ActionHistoryService();var progress=new TaskProgress {CurrentTask=ElevatedTaskCatalog.Get(ElevatedTaskCatalog.ResetTcpIpId),State=RunnerState.Completed,Result=partial};history.Record(progress);history.Record(progress);
   Check(history.Entries.Count==1&&history.Entries[0].StateText=="Parcial · Reinicio requerido","One partial Activity entry, not a total failure "+lang);
   var details=history.Entries[0].CreateDetailsProgress().Result;
   Check(details.ExitCode==1&&details.StdOut==text&&details.FindingStatus==FindingStatus.PartiallyCompleted,"Activity retains original Windows failure evidence "+lang);
   string dir=Path.Combine(project,"bin","Debug","NetworkRobustness",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);string file;
   using(var logger=new SessionLogger(new PortableStorage(dir))){file=logger.FilePath;logger.TaskFinished(progress.CurrentTask,partial);}
   string log=File.ReadAllText(file,Encoding.UTF8);Check(log.Contains(text)&&log.Contains("PartiallyCompleted"),"Logs retain partial result and full original output "+lang);
  }
  string esSuccess="Global se restableció correctamente.\nInterfaz se restableció correctamente.\nRuta se restableció correctamente.";
  string enSuccess="Resetting Global, OK!\nResetting Interface, OK!\nResetting Route, OK!";
  Tcp("Spanish total",esSuccess,0,ExecutionStatus.Success,FindingStatus.Completed);
  Tcp("English total",enSuccess,0,ExecutionStatus.Success,FindingStatus.Completed);
  Tcp("Nonzero exit with successful steps remains partial",enSuccess,1,ExecutionStatus.Success,FindingStatus.PartiallyCompleted);
  Tcp("Exit 0 cannot hide a reported failed step",enSuccess+"\nResetting , failed.",0,ExecutionStatus.Success,FindingStatus.PartiallyCompleted);
  Tcp("Exit 0 with unknown output is not total success",esSuccess+"\nTexto no reconocido.",0,ExecutionStatus.Success,FindingStatus.PartiallyCompleted);
  Tcp("Spanish stderr failures",esSuccess,1,ExecutionStatus.Success,FindingStatus.PartiallyCompleted,true,"Acceso denegado.\nReinicie el equipo para completar esta acción.");
  Tcp("English stderr failures",enSuccess,1,ExecutionStatus.Success,FindingStatus.PartiallyCompleted,true,"Access is denied.\nRestart the computer to complete this action.");
  foreach(string punctuation in new[]{"",".","!","?",":",";",",",".!?"}){
   Tcp("Spanish punctuation "+punctuation,"Global se restableció correctamente"+punctuation+"\nAcceso denegado"+punctuation+"\nReinicie el equipo para completar esta acción"+punctuation,1,ExecutionStatus.Success,FindingStatus.PartiallyCompleted,true);
   Tcp("English punctuation "+punctuation,"Resetting Global, OK"+punctuation+"\nAccess is denied"+punctuation+"\nRestart the computer to complete this action"+punctuation,1,ExecutionStatus.Success,FindingStatus.PartiallyCompleted,true);
  }
  Tcp("Spanish whitespace variation","  Interfaz   se restableció correctamente.  ",0,ExecutionStatus.Success,FindingStatus.Completed);
  Tcp("Spanish without accent variant","Global se restablecio correctamente.",0,ExecutionStatus.Success,FindingStatus.Completed);
  foreach(string text in new[]{"Acceso denegado.","Access is denied.","Error al restablecer .","La operación solicitada requiere elevación.","The requested operation requires elevation (Run as administrator).",""})
   Tcp("No successful reset evidence "+text,text,1,ExecutionStatus.Failed,FindingStatus.Unknown);
  foreach(string text in new[]{"correctamente","successful","Global se restableció correctamenteXYZ","Global se restableció correctamente.XYZ","Global NO se restableció correctamente.","No se restableció correctamente.","Nota: Global se restableció correctamente.","Nota: Resetting Global, OK!","Resetting Global, OKXYZ","Resetting Global, OK!.XYZ","Texto ajeno."})
   Tcp("False positive rejected: "+text,text,0,ExecutionStatus.Failed,FindingStatus.Unknown);
  Tcp("Worker exception/missing final frame with partial stdout",esSuccess,null,ExecutionStatus.Failed,FindingStatus.Unknown);
  Tcp("Process never started","",null,ExecutionStatus.Failed,FindingStatus.Unknown);
  var cancelled=new MaintenanceTaskResult {ExecutionStatus=ExecutionStatus.Cancelled};TcpIpResetResultParser.Apply(cancelled);
  Check(cancelled.ExecutionStatus==ExecutionStatus.Cancelled&&cancelled.FindingStatus==FindingStatus.Unknown&&!cancelled.RequiresRestart,"Cancelled UAC stays cancelled; no reset result invented");
  return count+" comprobaciones de robustez Red/TCP-IP correctas; sondas, HTTP y resultados Windows 10 simulados, sin red real, procesos ni UAC.";
 }
}
'@
$provider=[Microsoft.CSharp.CSharpCodeProvider]::new()
$parameters=[CodeDom.Compiler.CompilerParameters]::new()
$parameters.GenerateInMemory=$true
foreach($reference in @($exe,'System.dll','System.Core.dll')){[void]$parameters.ReferencedAssemblies.Add($reference)}
$compiled=$provider.CompileAssemblyFromSource($parameters,$source)
if($compiled.Errors.HasErrors){throw ($compiled.Errors | Out-String)}
if($CompileOnly){'Harness de robustez compilado sin ejecutar ni cargar WinSereno.exe.';return}
[NetworkRobustnessChecks]::Run($project)
