$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $project 'bin\Debug\WinSereno.exe'
[void][Reflection.Assembly]::LoadFrom($exe)
$source = @'
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Collections.Generic;
using System.Threading.Tasks;
using WinSereno.Models;
using WinSereno.Services;
public sealed class SfcChunkReader : StreamReader {
 readonly Queue<string> chunks;
 public SfcChunkReader(string[] chunks) : base(new MemoryStream()) { this.chunks=new Queue<string>(chunks); }
 public override Task<int> ReadAsync(char[] buffer,int index,int count) {
  if(chunks.Count==0) return Task.FromResult(0);
  string text=chunks.Dequeue();
  if(text.Length>count) throw new Exception("Chunk demasiado grande");
  text.CopyTo(0,buffer,index,text.Length);
  return Task.FromResult(text.Length);
 }
}
public static class SfcStreamingChecks {
 static int count;
 const string Spanish="Se complet\u00f3 la comprobaci\u00f3n de ";
 static readonly Type Buffer=typeof(ToolProgressParser).Assembly.GetType("WinSereno.Services.OutputLineBuffer",true);
 static void Check(bool ok,string name) { if(!ok) throw new Exception(name); count++; }
 static object New(List<string> lines) { return Activator.CreateInstance(Buffer,new object[]{new Action<string>(lines.Add)}); }
 static void Append(object buffer,string text) { Buffer.GetMethod("Append").Invoke(buffer,new object[]{text}); }
 static void Complete(object buffer) { Buffer.GetMethod("Complete").Invoke(buffer,null); }
 static List<string> Collect(params string[] chunks) {
  var lines=new List<string>(); var buffer=New(lines);
  foreach(string chunk in chunks) Append(buffer,chunk); Complete(buffer);
  Check(string.Concat(lines)==string.Concat(chunks),"Reconstruccion exacta");
  Complete(buffer); Check(string.Concat(lines)==string.Concat(chunks),"EOF idempotente sin duplicados");
  return lines;
 }
 static double? Progress(string text) { return ToolProgressParser.Parse(ElevatedTaskCatalog.SfcId,text); }
 public static string Run(string project) {
  string line=Spanish+"37%.";
  var lines=Collect(line+"\r\n"); Check(lines.Count==1 && Progress(lines[0])==37,"Linea ES completa");
  var split=new List<string>(); var buffer=New(split); Append(buffer,"Se com");
  Check(split.Count==0,"No publicar fragmentos");
  Append(buffer,"plet\u00f3 la comprobaci\u00f3n de 37%.\r\n");
  Check(split.Count==1 && split[0]==line+"\r\n" && Progress(split[0])==37,"Caso real en dos chunks");
  Complete(buffer); Check(split.Count==1,"No duplicar al finalizar");
  string original=line+"\r\n";
  lines=Collect(original.Select(c=>c.ToString()).ToArray());
  Check(lines.Count==1 && lines[0]==original && Progress(lines[0])==37,"Chunks de un caracter/CRLF dividido");
  for(int cut=1;cut<original.Length;cut++) {
   var emitted=new List<string>(); var b=New(emitted); Append(b,original.Substring(0,cut));
   Check(emitted.Count==0,"Sin linea parcial en cada limite");
   Append(b,original.Substring(cut)); Complete(b);
   Check(emitted.Count==1 && emitted[0]==original && Progress(emitted[0])==37,"Cada particion reconstruida");
  }
  lines=Collect(Spanish+"0%.\r\n"+line+"\n"+Spanish+"100%.\r\n");
  Check(lines.Count==3 && Progress(lines[0])==0 && Progress(lines[1])==37 && Progress(lines[2])==100,"Varias lineas y progreso 0/37/100");
  var partial=new List<string>(); buffer=New(partial); Append(buffer,"primera\r\nSe com");
  Check(partial.Count==1 && partial[0]=="primera\r\n","Cola parcial retenida");
  Append(buffer,"plet\u00f3 la comprobaci\u00f3n de 37%.");
  Check(partial.Count==1,"Sin salto final aun no emitido");
  Complete(buffer); Check(partial.Count==2 && partial[1]==line && Progress(partial[1])==37,"Ultima linea sin salto final");
  lines=Collect("a\rb\r","\nc\n\r","\nultimo");
  Check(lines.SequenceEqual(new[]{"a\r","b\r\n","c\n","\r\n","ultimo"}),"CR/LF/CRLF/lineas vacias");
  lines=Collect("",""); Check(lines.Count==0,"Salida vacia no inventa lineas");
  lines=Collect("a\r"); Check(lines.Count==1 && lines[0]=="a\r","CR al EOF preservado");
  lines=Collect("Unicode: \ud83d","\ude00\r","\n"); Check(lines.Count==1 && lines[0]=="Unicode: \ud83d\ude00\r\n","Caracteres Unicode completos");
  foreach(int value in new[]{0,1,37,99,100}) Check(Progress(Spanish+value+"%.")==value,"Porcentaje valido ES");
  foreach(string text in new[]{Spanish+"101%.",Spanish+"999%.",Spanish+"-1%.",Spanish+"37.5%.",
    "Ejemplo: "+line,line+" Extra","Se completo la comprobacion de 37%.","37%","Se com","Verification 101% complete."})
   Check(Progress(text)==null,"Texto/porcentaje invalido");
  foreach(int value in new[]{0,37,100}) Check(Progress("Verification "+value+"% complete.")==value,"EN conservado");
  foreach(string text in new[]{"Se complet\u00f3 un 24% de la verificaci\u00f3n.",
    "Se complet\u00f3 24% de verificaci\u00f3n.","Se complet\u00f3 la verificaci\u00f3n de 24%."})
   Check(Progress(text)==24,"ES anterior conservado");
  // Exercise the actual asynchronous pump with controlled reads, never a process.
  var received=new List<string>(); buffer=New(received); var raw=new StringBuilder(); object active=buffer;
  var pump=typeof(ToolProgressParser).Assembly.GetType("WinSereno.Services.FixedTaskProcess").GetMethod("PumpAsync",BindingFlags.NonPublic|BindingFlags.Static);
  using(var reader=new SfcChunkReader(new[]{"Se com","plet\u00f3 la comprobaci\u00f3n de 37%.\r","\n","final sin salto"})) {
   var pending=(Task)pump.Invoke(null,new object[]{reader,new Action<string>(s=>{raw.Append(s);Append(active,s);}),new Action(()=>Complete(active))});
   pending.GetAwaiter().GetResult();
  }
  Check(received.Count==2 && received[0]==original && received[1]=="final sin salto","Pump emite lineas completas/EOF");
  Check(raw.ToString()==string.Concat(received),"StdOut integro igual a mensajes IPC");
  Check(Progress(received[0])==37,"Progreso sobre salida reconstruida del pump");
  var findings=new[]{FindingStatus.Healthy,FindingStatus.Repaired,FindingStatus.RepairFailed,FindingStatus.ScanFailed,FindingStatus.Unknown};
  string[] summaries={"Windows Resource Protection did not find any integrity violations.",
   "Windows Resource Protection found corrupt files and successfully repaired them.",
   "Windows Resource Protection found corrupt files but was unable to fix some of them.",
   "Windows Resource Protection could not perform the requested operation.","Unrecognized output"};
  for(int i=0;i<summaries.Length;i++) {
   string text=original+summaries[i];
   lines=Collect(text.Select(c=>c.ToString()).ToArray());
   Check(SfcResultParser.Parse(string.Concat(lines))==findings[i],"Resultado final independiente");
  }
  var result=new MaintenanceTaskResult{ExitCode=0,StdOut="Unknown"};
  RepairResultInterpreter.Apply(ElevatedTaskCatalog.SfcId,result);
  Check(result.FindingStatus==FindingStatus.Unknown,"ExitCode 0 no implica Healthy");
  string implementation=File.ReadAllText(Path.Combine(project,"Services","FixedTaskProcess.cs"));
  Check(implementation.Contains("task.Id == ElevatedTaskCatalog.SfcId ? new OutputLineBuffer(output) : null") &&
   implementation.Contains("if (lines != null) lines.Append(text); else output(text);"),"Buffer solo para SFC");
  return count+" comprobaciones nuevas correctas; sin SFC, procesos de mantenimiento ni UAC.";
 }
}
'@
$provider = [Microsoft.CSharp.CSharpCodeProvider]::new()
$parameters = [CodeDom.Compiler.CompilerParameters]::new()
$parameters.GenerateInMemory = $true
foreach($reference in @($exe,'System.dll','System.Core.dll')) { [void]$parameters.ReferencedAssemblies.Add($reference) }
$compiled=$provider.CompileAssemblyFromSource($parameters,$source)
if($compiled.Errors.HasErrors) { throw ($compiled.Errors | Out-String) }
[SfcStreamingChecks]::Run($project)
