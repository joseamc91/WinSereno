param([switch]$CompileOnly)
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
$exe=Join-Path $project 'bin\Debug\WinSereno.exe'
if(-not $CompileOnly) { [void][Reflection.Assembly]::LoadFrom($exe) }
$source=@'
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using WinSereno.Models;
using WinSereno.Services;
public sealed class NetworkByteChunks : MemoryStream {
 readonly int size;
 public NetworkByteChunks(byte[] bytes,int size):base(bytes){this.size=size;}
 public override Task<int> ReadAsync(byte[] b,int o,int n,CancellationToken token){return Task.FromResult(base.Read(b,o,Math.Min(n,size)));}
}
public static class NetworkOutputChecks {
 static int count; static void Check(bool ok,string label){if(!ok)throw new Exception(label);count++;}
 static readonly Type Reader=typeof(WinsockResetResultParser).Assembly.GetType("WinSereno.Services.NetshOutputReader",true);
 static string Pump(byte[] bytes,int size,Encoding fallback,out int chunks){
  var emitted=new List<string>();using(var input=new NetworkByteChunks(bytes,size)){
   var task=(Task)Reader.GetMethod("PumpAsync",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{input,fallback,new Action<string>(emitted.Add)});
   task.GetAwaiter().GetResult();
  }
  chunks=emitted.Count;return string.Concat(emitted);
 }
 static MaintenanceTaskResult Result(string text,int exit=0,string error="") {return new MaintenanceTaskResult {StdOut=text,StdErr=error,ExitCode=exit,StartedAt=DateTimeOffset.Now.AddSeconds(-2),FinishedAt=DateTimeOffset.Now,Duration=TimeSpan.FromSeconds(2)};}
 public static string Run(string project){
  var legacy=Encoding.GetEncoding(850);int chunks;
  string es=File.ReadAllText(Path.Combine(project,"Tests","Fixtures","WinsockResetSuccess.es.txt"),Encoding.UTF8);
  Check(legacy.GetString(Encoding.UTF8.GetBytes("catálogo")).Contains("cat├ílogo"),"Reproduce historical UTF8/OEM corruption");
  Check(legacy.GetString(Encoding.UTF8.GetBytes("restableció")).Contains("restableci├│"),"Reproduce observed second accent corruption");
  foreach(string tool in new[]{"Winsock","TcpIp"})foreach(string lang in new[]{"es","en"}){
   string file=Path.Combine(project,"Tests","Fixtures",tool+"ResetSuccess."+lang+".txt");byte[] bytes=File.ReadAllBytes(file);string expected=File.ReadAllText(file,Encoding.UTF8);
   foreach(int size in new[]{1,2,3,7,1024}){
    string decoded=Pump(bytes,size,legacy,out chunks);
    Check(decoded==expected,"Exact reconstructed "+tool+lang+" / "+size);
    Check(chunks==expected.Count(c=>c=='\n'),"No duplicated/missing lines");
    Check(!decoded.Contains("├")&&!decoded.Contains("\uFFFD"),"No mojibake or replacement characters");
    var result=Result(decoded);if(tool=="Winsock")WinsockResetResultParser.Apply(result);else TcpIpResetResultParser.Apply(result);
    Check(result.ExecutionStatus==ExecutionStatus.Success&&result.FindingStatus==FindingStatus.Completed&&result.RequiresRestart,"Success/Completed/restart "+tool+lang);
    Check(result.StdOut==expected,"Parser preserves original decoded output");
   }
  }
  foreach(int size in new[]{1,2,7}){
   string text="áéíóúñ · línea\r\nÚltima línea sin salto";
   Check(Pump(Encoding.UTF8.GetBytes(text),size,legacy,out chunks)==text&&chunks==2,"UTF8 split characters, CRLF and final tail");
   Check(Pump(legacy.GetBytes(text),size,legacy,out chunks)==text,"Legacy OEM fallback remains readable");
   Check(Pump(new byte[]{239,187,191}.Concat(Encoding.UTF8.GetBytes(es)).ToArray(),size,legacy,out chunks)==es,"UTF8 BOM supported");
   Check(Pump(Encoding.UTF8.GetBytes("Acceso denegado.\r\nOperación inválida."),size,legacy,out chunks)=="Acceso denegado.\r\nOperación inválida.","Same capture path for stderr");
  }
  Check(Pump(new byte[0],1,legacy,out chunks)==""&&chunks==0,"Empty stream");
  Check(Pump(Encoding.ASCII.GetBytes("OK\n\n"),1,legacy,out chunks)=="OK\n\n"&&chunks==2,"Blank lines retained");
  var winsock=Result(es);WinsockResetResultParser.Apply(winsock);
  Check(winsock.UserSummary.Contains("Esto no confirma que Internet funcione.")&&winsock.UserSummary.Contains("debes reiniciar")&&winsock.UserSummary.Contains("no se reinicia automáticamente"),"Conservative functional summary");
  foreach(string failure in new[]{"Access is denied.","Acceso denegado."}){
   var r=Result(failure,5);WinsockResetResultParser.Apply(r);Check(r.ExecutionStatus==ExecutionStatus.Failed&&r.FindingStatus==FindingStatus.Unknown,"Winsock real failure");
  }
  var ambiguous=Result("Operation finished.");WinsockResetResultParser.Apply(ambiguous);Check(ambiguous.FindingStatus==FindingStatus.Unknown,"ExitCode0 alone not confirmed");
  var cancelled=new MaintenanceTaskResult{ExecutionStatus=ExecutionStatus.Cancelled};WinsockResetResultParser.Apply(cancelled);Check(cancelled.ExecutionStatus==ExecutionStatus.Cancelled&&!cancelled.RequiresRestart,"UAC cancellation semantics unchanged");
  var partial=Result("Restablecimiento de Interfaz correcto.\nRestablecimiento de Reenvío erróneo.\nAcceso denegado.\nReinicie el equipo para completar esta acción.");TcpIpResetResultParser.Apply(partial);
  Check(partial.ExecutionStatus==ExecutionStatus.Failed&&partial.FindingStatus==FindingStatus.PartiallyCompleted&&partial.RequiresRestart,"TCPIP partial with restart");
  var tcpUnknown=Result("Texto ambiguo.");TcpIpResetResultParser.Apply(tcpUnknown);Check(tcpUnknown.FindingStatus==FindingStatus.Unknown,"TCPIP ambiguous not confirmed");
  foreach(string phrase in new[]{"Se vació correctamente la caché de resolución de DNS.","Successfully flushed the DNS Resolver Cache."}){
   string decoded;using(var reader=new StreamReader(new MemoryStream(legacy.GetBytes(phrase)),legacy))decoded=reader.ReadToEnd();
   var r=Result(decoded);FlushDnsResultParser.Apply(r);Check(r.ExecutionStatus==ExecutionStatus.Success&&r.FindingStatus==FindingStatus.Completed&&!r.RequiresRestart,"Flush DNS existing OEM path regression");
  }
  foreach(bool recovered in new[]{true,false}){
   var calls=new List<int>();var notes=new List<string>();
   var r=AdapterRestartPolicy.RunAsync("Fixture Ethernet",i=>{
    calls.Add(i);return Task.FromResult(new AdapterRestartAttempt{Result=Result(i==0?"Deshabilitado.":"Habilitación.",i==0||recovered?0:5),CommandStarted=true,State=i==0?AdapterRestartState.Disabled:recovered?AdapterRestartState.Active:AdapterRestartState.Disabled});
   },(i,a)=>{},notes.Add,()=>Task.CompletedTask).GetAwaiter().GetResult();
   Check(calls.SequenceEqual(recovered?new[]{0,1}:new[]{0,1,2,3}),"Restart disable/enable and retry policy unchanged");
   Check(r.FindingStatus==(recovered?FindingStatus.Completed:FindingStatus.RepairFailed),"Restart final verification unchanged");
  }
  var failedDisable=AdapterRestartPolicy.RunAsync("Fixture",i=>Task.FromResult(new AdapterRestartAttempt{Result=Result("Acceso denegado.",5),State=AdapterRestartState.Active}),(i,a)=>{},s=>{},()=>Task.CompletedTask).GetAwaiter().GetResult();
  Check(failedDisable.SequenceSteps.Last().WasSkipped,"Failed disable does not start enable blindly");
  string dir=Path.Combine(project,"bin","Debug","NetworkChecks",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);string log;
  using(var logger=new SessionLogger(new PortableStorage(dir))){log=logger.FilePath;logger.TaskFinished(ElevatedTaskCatalog.Get(ElevatedTaskCatalog.ResetWinsockId),winsock);}
  Check(File.ReadAllText(log,Encoding.UTF8).Contains(es),"Logs retain decoded full stdout");
  var history=new ActionHistoryService();history.Record(new TaskProgress{CurrentTask=ElevatedTaskCatalog.Get(ElevatedTaskCatalog.ResetWinsockId),Result=winsock,State=RunnerState.Completed});
  Check(history.Entries.Count==1&&history.Entries[0].CreateDetailsProgress().Result.StdOut==es,"Activity retains decoded snapshot");
  return count+" comprobaciones de red correctas; bytes, parsers ES/EN, logging y recuperación simulados. Ningún comando real ni UAC.";
 }
}
'@
$provider=[Microsoft.CSharp.CSharpCodeProvider]::new()
$parameters=[CodeDom.Compiler.CompilerParameters]::new()
$parameters.GenerateInMemory=$true
foreach($reference in @($exe,'System.dll','System.Core.dll')){[void]$parameters.ReferencedAssemblies.Add($reference)}
$compiled=$provider.CompileAssemblyFromSource($parameters,$source)
if($compiled.Errors.HasErrors){throw ($compiled.Errors | Out-String)}
if($CompileOnly){'Harness de red compilado sin ejecutar ni cargar WinSereno.exe.';return}
[NetworkOutputChecks]::Run($project)
