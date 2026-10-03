param([switch]$CompileOnly)
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $project 'bin\Debug\WinSereno.exe'
if (-not $CompileOnly) { [void][Reflection.Assembly]::LoadFrom($exe) }
$source = @'
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using WinSereno.Models;
using WinSereno.Services;
using WinSereno.ViewModels;
public sealed class ChkdskByteChunks : MemoryStream {
 readonly int size;
 public ChkdskByteChunks(byte[] bytes,int size) : base(bytes) { this.size=size; }
 public override int Read(byte[] buffer,int offset,int count) { return base.Read(buffer,offset,Math.Min(size,count)); }
 public override Task<int> ReadAsync(byte[] buffer,int offset,int count,CancellationToken token) {
  return Task.FromResult(Read(buffer,offset,count));
 }
}
public static class ChkdskRegressionChecks {
 static int count;
 [DllImport("kernel32.dll")] static extern uint GetACP();
 static readonly Type ProcessCapture=typeof(ChkdskResultParser).Assembly.GetType("WinSereno.Services.FixedTaskProcess",true);
 static readonly Type Protocol=typeof(ChkdskResultParser).Assembly.GetType("WinSereno.Services.WorkerProtocol",true);
 static void Check(bool ok,string name) { if(!ok) throw new Exception(name);count++; }
 static MaintenanceTaskResult Parse(string text,int code,FindingStatus finding,ExecutionStatus execution,string error="") {
  var result=new MaintenanceTaskResult {StdOut=text,StdErr=error,ExitCode=code};
  RepairResultInterpreter.Apply(ElevatedTaskCatalog.ChkdskId,result);
  Check(result.FindingStatus==finding,"Finding: "+text+" / "+code);
  Check(result.ExecutionStatus==execution,"Execution: "+text+" / "+code);
  Check(result.StdOut==text&&result.StdErr==error&&result.ExitCode==code,"Salida y código intactos");
  Check(!result.RequiresRestart,"No se programa ni requiere reinicio");return result;
 }
 static string Pump(string text,Encoding encoding,int size) {
  var chunks=new List<string>();
  using(var reader=new StreamReader(new ChkdskByteChunks(encoding.GetBytes(text),size),encoding,true,128)) {
   var pump=(Task)ProcessCapture.GetMethod("PumpAsync",BindingFlags.Static|BindingFlags.NonPublic)
    .Invoke(null,new object[]{reader,new Action<string>(chunks.Add),null});pump.GetAwaiter().GetResult();
  }
  Check(chunks.Count>0,"Salida recibida durante pumping");
  string collected=string.Concat(chunks);Check(collected==text,"Sin caracteres perdidos o duplicados");
  foreach(string chunk in chunks) {
   using(var stream=new MemoryStream()) {
    using(var writer=new BinaryWriter(stream,Encoding.UTF8,true)) {
     Protocol.GetMethod("WriteText").Invoke(null,new object[]{writer,chunk});writer.Flush();
    }
    stream.Position=0;
    using(var reader=new BinaryReader(stream,Encoding.UTF8,true))
     Check((string)Protocol.GetMethod("ReadText").Invoke(null,new object[]{reader})==chunk,"IPC conserva Unicode");
   }
  }
  return collected;
 }
 public static string Run(string project,string fixtureFolder) {
  string observed=File.ReadAllText(Path.Combine(project,"Tests","Fixtures","ChkdskReadOnlyProblems.es.txt"),Encoding.UTF8);
  foreach(string phrase in new[]{"Ejecutando CHKDSK en modo de solo lectura.","El mapa de bits del volumen es incorrecto.",
   "Windows comprobó el sistema de archivos y detectó problemas.",
   "Ejecute chkdsk /scan para encontrar los problemas y ponerlos en cola para su reparación."}) Check(observed.Contains(phrase),"Fixture observado exacto");
  var real=Parse(observed,3,FindingStatus.Attention,ExecutionStatus.Success);
  Check(real.UserSummary.Contains("chkdsk /scan")&&real.UserSummary.Contains("solo información"),"Recomendación informativa sin ejecución");
  Check(real.UserSummary.Contains("no se ha realizado ninguna reparación"),"Explicación de solo lectura");
  foreach(string text in new[]{"Windows comprobó el sistema de archivos y no encontró problemas.",
   "Windows ha examinado el sistema de archivos y no encontró ningún problema.",
   "Windows ha comprobado el sistema de archivos y no ha encontrado problemas.",
   "Windows has scanned the file system and found no problems.","Windows has checked the file system and found no problems."})
   Parse(text,0,FindingStatus.Healthy,ExecutionStatus.Success);
  foreach(string text in new[]{observed,"Windows comprobó el sistema de archivos y detectó problemas.",
   "Windows ha encontrado problemas en el sistema de archivos.","Windows has scanned the file system and found problems.",
   "Windows has checked the file system and found problems.","Windows found problems with the file system.",
   "CHKDSK cannot continue in read-only mode.","CHKDSK no puede continuar en modo de solo lectura.",
   "CHKDSK is running in read-only mode.\r\nThe Volume Bitmap is incorrect.",
   "Ejecutando CHKDSK en modo de solo lectura.\r\nEl mapa de bits del volumen es incorrecto."})
   foreach(int code in new[]{0,1,2,3}) Parse(text,code,FindingStatus.Attention,ExecutionStatus.Success);
  foreach(string error in new[]{"Cannot open volume for direct access.","No se puede abrir el volumen para acceso directo.",
   "CHKDSK is not available for RAW drives.","CHKDSK no está disponible para unidades RAW.",
   "Access Denied as you do not have sufficient privileges or the disk may be locked by another process.",
   "Acceso denegado porque no tiene privilegios suficientes.",
   "Unable to determine volume version and state. CHKDSK aborted.","No se puede determinar la versión y el estado del volumen. CHKDSK se anuló."}) {
   Parse(error,3,FindingStatus.ScanFailed,ExecutionStatus.Failed);
   Parse("",0,FindingStatus.ScanFailed,ExecutionStatus.Failed,error);
  }
  // Recognized phrases may end with punctuation, but never inside a longer token.
  string[] denied={"Acceso denegado porque no tiene privilegios suficientes",
   "Access Denied as you do not have sufficient privileges"};
  foreach(string phrase in denied) {
   string summary=Parse(phrase,3,FindingStatus.ScanFailed,ExecutionStatus.Failed).UserSummary;
   foreach(string ending in new[]{"",".","!","?",":",";",",","...","?!",", consulta los detalles."}) {
    foreach(int code in new[]{0,3,5}) {
     var punctuation=Parse(phrase+ending,code,FindingStatus.ScanFailed,ExecutionStatus.Failed);
     Check(punctuation.UserSummary==summary,"Puntuación no cambia el mensaje funcional de error");
     Parse("",code,FindingStatus.ScanFailed,ExecutionStatus.Failed,phrase+ending);
    }
   }
   foreach(string invalid in new[]{phrase+"XYZ",phrase+".XYZ","X"+phrase+"."}) {
    Parse(invalid,0,FindingStatus.Unknown,ExecutionStatus.Success);
    Parse(invalid,3,FindingStatus.Unknown,ExecutionStatus.Unknown);
    Parse("",0,FindingStatus.Unknown,ExecutionStatus.Success,invalid);
   }
  }
  foreach(string healthy in new[]{"Windows comprobó el sistema de archivos y no encontró problemas.",
   "Windows has checked the file system and found no problems."}) {
   string summary=Parse(healthy,0,FindingStatus.Healthy,ExecutionStatus.Success).UserSummary;
   foreach(string ending in new[]{"!","?",":",";",","})
    Check(Parse(healthy+ending,0,FindingStatus.Healthy,ExecutionStatus.Success).UserSummary==summary,
     "Puntuación no cambia Healthy ni su mensaje");
   Parse(healthy+"XYZ",0,FindingStatus.Unknown,ExecutionStatus.Success);
   Parse(healthy+"!",3,FindingStatus.Unknown,ExecutionStatus.Unknown);
   foreach(string deniedPhrase in denied)
    Parse(healthy+" "+deniedPhrase+"!",0,FindingStatus.ScanFailed,ExecutionStatus.Failed);
  }
  foreach(string attention in new[]{"Windows comprobó el sistema de archivos y detectó problemas.",
   "Windows has checked the file system and found problems."}) {
   string summary=Parse(attention,3,FindingStatus.Attention,ExecutionStatus.Success).UserSummary;
   foreach(string ending in new[]{"!","?",":",";",","})
    foreach(int code in new[]{0,1,2,3})
     Check(Parse(attention+ending,code,FindingStatus.Attention,ExecutionStatus.Success).UserSummary==summary,
      "Puntuación conserva Attention y las reglas de ExitCode");
   Parse(attention+"XYZ",3,FindingStatus.Unknown,ExecutionStatus.Unknown);
   Parse(attention+"!",5,FindingStatus.Unknown,ExecutionStatus.Failed);
   foreach(string deniedPhrase in denied)
    Parse(attention+" "+deniedPhrase+"?",3,FindingStatus.ScanFailed,ExecutionStatus.Failed);
  }
  string contradictory="Windows has checked the file system and found no problems.! "+
   "Windows has checked the file system and found problems.?";
  Parse(contradictory,0,FindingStatus.Unknown,ExecutionStatus.Success);
  Parse(contradictory,3,FindingStatus.Unknown,ExecutionStatus.Unknown);
  foreach(string ambiguous in new[]{"Comprobación iniciada.","", "El mapa de bits del volumen es incorrecto.",
   "Windows has checked the file system and found no problems.\r\nWindows has checked the file system and found problems."}) {
   Parse(ambiguous,0,FindingStatus.Unknown,ExecutionStatus.Success);
   Parse(ambiguous,3,FindingStatus.Unknown,ExecutionStatus.Unknown);
  }
  Parse("Error no clasificado.",5,FindingStatus.Unknown,ExecutionStatus.Failed);
  var cancelled=new MaintenanceTaskResult {ExecutionStatus=ExecutionStatus.Cancelled,StdOut=observed};
  RepairResultInterpreter.Apply(ElevatedTaskCatalog.ChkdskId,cancelled);
  Check(cancelled.ExecutionStatus==ExecutionStatus.Cancelled&&cancelled.FindingStatus==FindingStatus.Unknown,"Cancelación no interpretada como hallazgo");
  var noCode=new MaintenanceTaskResult {StdOut=observed};ChkdskResultParser.Apply(noCode);
  Check(noCode.FindingStatus==FindingStatus.Unknown,"Sin confirmación de finalización no se inventa resultado");
  var captureEncoding=(Encoding)ProcessCapture.GetMethod("ChkdskOutputEncoding",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
  Check(captureEncoding.CodePage==(int)GetACP(),"Captura usa la ACP real, sin code page española fija");
  // Reproduce the exact historical corruption from ANSI 1252 bytes decoded as OEM 850.
  string[] correct={"parámetro","Comprobación","índice","huérfano","está"};
  string[] corrupted={"parßmetro","Comprobaci¾n","Ýndice","huÚrfano","estß"};
  for(int i=0;i<correct.Length;i++) {
   byte[] bytes=Encoding.GetEncoding(1252).GetBytes(correct[i]);
   Check(Encoding.GetEncoding(850).GetString(bytes)==corrupted[i],"Reproducción del defecto histórico");
   Check(Encoding.GetEncoding(1252).GetString(bytes)==correct[i],"Bytes originales decodificados correctamente");
  }
  foreach(int size in new[]{1,2,7,1024}) {
   string decoded=Pump(observed,captureEncoding,size);
   foreach(string bad in corrupted) Check(!decoded.Contains(bad),"Sin mojibake observado");
   Parse(decoded,3,FindingStatus.Attention,ExecutionStatus.Success);
   Check(decoded.Contains("Etapa: 100%; Total: 79%; Tiempo estimado"),"Progreso nativo intacto");
  }
  string accents=string.Join(" ",correct)+"\r\n";
  Check(Pump(accents,captureEncoding,1)==accents,"Caracteres españoles completos");
  Check(Pump("No se puede abrir el volumen para acceso directo.\r\nComprobación.",captureEncoding,2).Contains("Comprobación"),"Misma decodificación para stderr");
  Check(Pump(observed,new UTF8Encoding(false),1)==observed,"StreamReader conserva caracteres multibyte fragmentados");
  var task=ElevatedTaskCatalog.Get(ElevatedTaskCatalog.ChkdskId);
  Check(task.Arguments==(string)typeof(ChkdskResultParser).Assembly.GetType("WinSereno.Services.WindowsVolumeResolver",true).GetMethod("Resolve").Invoke(null,null),"Volumen interno sin modificación");
  Check(!task.Arguments.Contains("/")&&Path.GetFileName(task.Command)=="chkdsk.exe","Comando sin opciones adicionales");
  Check(task.RequiresElevation&&!task.CanBeCancelled,"Elevación y bloqueo de cierre originales");
  Check(!RepairCompleteSequence.TaskIds.Contains(task.Id),"Fuera de reparación completa");
  var state=new IntegritySessionState();state.Update(task.Id,real);var view=new RepairTaskViewModel(task.Id,"CHKDSK · Solo lectura",state);
  Check(view.ResultText.Contains("Atención")&&!view.ResultText.Contains("No se pudo completar"),"Resultado Attention llega al binding existente");
  string log;
  using(var logger=new SessionLogger(new PortableStorage(fixtureFolder))) {
   log=logger.FilePath;logger.TaskStarted(task);logger.TaskFinished(task,real);
  }
  string logged=File.ReadAllText(log,Encoding.UTF8);
  Check(logged.Contains(observed),"Log UTF8 conserva stdout completo");
  Check(logged.Contains("Estado=Success | Resultado=Attention | ExitCode=3"),"Log refleja ejecución y hallazgo separados");
  Check(logged.Contains("Volumen de Windows comprobado="+task.Arguments),"Log conserva volumen");
  return count+" comprobaciones CHKDSK correctas; parser ES/EN, captura, IPC y log simulados. Sin CHKDSK ni UAC reales.";
 }
}
'@
$provider = [Microsoft.CSharp.CSharpCodeProvider]::new()
$parameters = [CodeDom.Compiler.CompilerParameters]::new()
$parameters.GenerateInMemory = $true
foreach ($reference in @($exe,'System.dll','System.Core.dll')) { [void]$parameters.ReferencedAssemblies.Add($reference) }
$compiled = $provider.CompileAssemblyFromSource($parameters,$source)
if ($compiled.Errors.HasErrors) { throw ($compiled.Errors | Out-String) }
if ($CompileOnly) { 'Harness compilado sin ejecutar ni cargar WinSereno.exe.'; return }
$fixture = Join-Path $PSScriptRoot ('chkdsk-log-fixture-'+[Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($fixture)
try { [ChkdskRegressionChecks]::Run($project,$fixture) }
finally {
    $resolvedFixture = [IO.Path]::GetFullPath($fixture)
    if ($resolvedFixture.StartsWith([IO.Path]::GetFullPath($PSScriptRoot)+'\',[StringComparison]::OrdinalIgnoreCase)) {
        [IO.Directory]::Delete($resolvedFixture,$true)
    }
}
