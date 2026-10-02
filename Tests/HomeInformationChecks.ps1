$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $project 'bin\Debug\WinSereno.exe'
[void][Reflection.Assembly]::LoadFrom($exe)
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.Xaml
$source = @'
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinSereno.Models;
using WinSereno.Services;
using WinSereno.ViewModels;
using WinSereno.Views;

public sealed class HomeCheckLogger : ISessionLogger {
 public string Text="";
 public void Write(string text) {Text+=text;}
 public void TaskStarted(MaintenanceTask t) {}
 public void TaskFinished(MaintenanceTask t,MaintenanceTaskResult r) {}
 public void Dispose() {}
}
public sealed class HomeGpuProgress : IProgress<InformationUpdate> {
 public InformationUpdate Update;
 public void Report(InformationUpdate update) {Update=update;}
}
public sealed class HomeCheckRunner : IMaintenanceTaskRunner {
 public bool IsActive {get {return false;}}
 public TaskProgress Current {get {return new TaskProgress();}}
 public event EventHandler<TaskProgress> ProgressChanged {add {} remove {}}
 public System.Threading.Tasks.Task<MaintenanceTaskResult> RunAsync(MaintenanceTask t) {throw new Exception("No real actions permitted");}
 public bool RequestCancellation() {throw new Exception("No real actions permitted");}
 public System.Threading.Tasks.Task WaitForIdleAsync() {return System.Threading.Tasks.Task.CompletedTask;}
}
public sealed class HomeCheckDialogs : IDialogService {
 public void ShowOutput(TaskProgress p) {throw new Exception("No actions permitted");}
 public void ShowMessage(string t) {throw new Exception("Unexpected dialog");}
 public void ShowDiagnosticDetails(DiagnosticResult r) {throw new Exception("No actions permitted");}
 public bool ConfirmTask(MaintenanceTask t) {throw new Exception("No UAC permitted");}
 public bool ConfirmCancelAndClose() {return false;}
 public RestartAdapter SelectRestartAdapter(IReadOnlyList<RestartAdapter> list) {throw new Exception("No network actions permitted");}
}
public static class HomeInformationChecks {
 static int count;
 static readonly ulong GB=1024UL*1024*1024;
 static void Check(bool value,string label) {if(!value) throw new Exception(label); count++;}
 static void Apply(HomeViewModel home,InformationBlock block,object data) {
  typeof(HomeViewModel).GetMethod("ApplyUpdate",BindingFlags.Instance|BindingFlags.NonPublic)
   .Invoke(home,new object[]{new InformationUpdate {Block=block,Status=InformationStatus.Available,Data=data}});
 }
 static GpuInformation Gpu(params GpuInformation[] adapters) {
  return (GpuInformation)typeof(SystemInformationService).GetMethod("SummarizeGpu",BindingFlags.Static|BindingFlags.NonPublic)
   .Invoke(null,new object[]{adapters});
 }
 static int? ReliableSpeed(params object[] speeds) {
  var rows=speeds.Select(speed=>new Dictionary<string,object> {{"MaxClockSpeed",speed}}).ToList();
  return (int?)typeof(SystemInformationService).GetMethod("ReliableCpuClockSpeed",BindingFlags.Static|BindingFlags.NonPublic)
   .Invoke(null,new object[]{rows});
 }
 static string GraphicsName(string name) {
  return (string)typeof(HomeViewModel).GetMethod("CleanGraphicsName",BindingFlags.Static|BindingFlags.NonPublic)
   .Invoke(null,new object[]{name});
 }
 static MemoryModuleInformation Module(ulong gb,uint type,uint speed) {
  return new MemoryModuleInformation {CapacityBytes=gb*GB,SmbiosMemoryType=type,ConfiguredSpeed=speed};
 }
 static void Memory(HomeViewModel home,string expected,params MemoryModuleInformation[] modules) {
  Apply(home,InformationBlock.Memory,new MemoryInformation {InstalledBytes=32*GB,Modules=modules});
  var card=home.Cards.Single(c=>c.Block==InformationBlock.Memory);
  Check(card.Value=="32 GB","Native installed total unchanged");
  Check(card.Description==expected,"RAM description: "+card.Description+" expected "+expected);
  Check(!card.Description.Contains("desconocido"),"Unknown metadata omitted");
 }
 static IEnumerable<T> Logical<T>(DependencyObject node) where T:DependencyObject {
  if(node is T) yield return (T)node;
  foreach(var child in LogicalTreeHelper.GetChildren(node)) if(child is DependencyObject)
   foreach(var value in Logical<T>((DependencyObject)child)) yield return value;
 }
 static IEnumerable<T> Visual<T>(DependencyObject node) where T:DependencyObject {
  if(node is T) yield return (T)node;
  for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)
   foreach(var value in Visual<T>(VisualTreeHelper.GetChild(node,i))) yield return value;
 }
 static void Layout(FrameworkElement content,double width=1150,double height=760) {
  content.Measure(new Size(width,height));content.Arrange(new Rect(0,0,width,height));content.UpdateLayout();
 }
 static void Save(FrameworkElement root,string file) {
  var bitmap=new RenderTargetBitmap((int)root.ActualWidth,(int)root.ActualHeight,96,96,PixelFormats.Pbgra32);
  var backdrop=new DrawingVisual();using(var drawing=backdrop.RenderOpen())drawing.DrawRectangle((Brush)Application.Current.Resources["BackgroundBrush"],null,new Rect(0,0,root.ActualWidth,root.ActualHeight));bitmap.Render(backdrop);bitmap.Render(root);
  var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(file))encoder.Save(stream);
 }
 static void CheckGrid(MainWindow window,FrameworkElement root,ItemsControl cards,HomeViewModel home,string label) {
  var panel=Visual<UniformGrid>(cards).Single();var style=window.FindResource("ThreeColumnCard");
  var borders=Visual<Border>(cards).Where(b=>ReferenceEquals(b.Style,style)).ToList();
  Check(panel.Columns==3&&borders.Count==6,"Exactly three columns and six Home cards: "+label);
  Check(borders.Select(b=>b.DataContext).SequenceEqual(home.Cards.Cast<object>()),"Windows/CPU/GPU then RAM/Network/Uptime unchanged: "+label);
  Check(borders.All(b=>double.IsNaN(b.Width)&&b.HorizontalAlignment==HorizontalAlignment.Stretch),"No fixed width; all Home cards stretch: "+label);
  Check(borders.All(b=>Math.Abs(b.ActualWidth-borders[0].ActualWidth)<0.5),"Equal Home card widths: "+label);
  var points=borders.Select(b=>b.TransformToAncestor(root).Transform(new Point())).ToList();
  Check(points.Take(3).All(p=>Math.Abs(p.Y-points[0].Y)<0.5)&&points.Skip(3).All(p=>Math.Abs(p.Y-points[3].Y)<0.5)&&points[3].Y>points[0].Y,"Two rows of three: "+label);
  Check(Enumerable.Range(0,3).All(i=>Math.Abs(points[i].X-points[i+3].X)<0.5),"Aligned column positions: "+label);
  var refresh=Logical<Button>(root).Single(b=>ReferenceEquals(b.Command,home.RefreshCommand));var buttonPoint=refresh.TransformToAncestor(root).Transform(new Point());
  Check(Math.Abs(points[2].X+borders[2].ActualWidth-buttonPoint.X-refresh.ActualWidth)<0.5,"Third column aligns with Refresh right edge: "+label);
  var parent=(FrameworkElement)cards.Parent;var parentPoint=parent.TransformToAncestor(root).Transform(new Point());
  Check(Math.Abs(points[0].X-parentPoint.X)<0.5&&Math.Abs(cards.ActualWidth+cards.Margin.Left+cards.Margin.Right-parent.ActualWidth)<0.5,"Home fills useful width without exterior gaps: "+label);
  Check(Math.Abs(points[1].X-points[0].X-borders[0].ActualWidth-10)<0.5&&Math.Abs(points[2].X-points[1].X-borders[1].ActualWidth-10)<0.5,"Consistent horizontal card gaps: "+label);
  foreach(var border in borders)foreach(var text in Visual<TextBlock>(border))Check(text.ActualWidth<=border.ActualWidth-border.Padding.Left-border.Padding.Right,"Home text contained at "+label);
 }
 static string BindingPath(DependencyObject obj,DependencyProperty property) {
  var b=BindingOperations.GetBinding(obj,property);return b==null?null:b.Path.Path;
 }
 public static string Run(string project) {
  CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("es-ES");
  var logger=new HomeCheckLogger();var home=new HomeViewModel(null,logger);
  Check(home.Cards.Count==6,"Six main cards");
  Check(home.Cards.Select(c=>c.Title).SequenceEqual(new[]{"Windows","CPU","GPU","RAM instalada","Red","Uptime"}),"Card titles and order");
  Check(home.Cards[2].Block==InformationBlock.Gpu,"GPU is the third card");
  Check(!home.Cards.Any(c=>c.Title.Contains("Reinicio")),"No global restart card");
  Check(!Enum.GetNames(typeof(InformationBlock)).Contains("Restart"),"No global restart provider enum");
  var assembly=typeof(HomeViewModel).Assembly;
  Check(assembly.GetType("WinSereno.Services.RestartPendingService")==null,"Global restart service removed");
  Check(assembly.GetType("WinSereno.Models.RestartPendingInformation")==null,"Global restart data removed");
  Check(assembly.GetType("WinSereno.Models.RestartPendingStatus")==null,"Global restart enum removed");
  var pending=DiagnosticService.CreatePendingResults();
  Check(pending.Count==6&&!pending.Any(d=>d.Id=="restart"||d.Id=="power"),"Six diagnostics, no restart or power check");
  Check(assembly.GetType("WinSereno.Services.BasicDiagnosticChecks").GetMethod("Restart")==null,"Global diagnostic implementation removed");
  Check(File.ReadAllText(Path.Combine(project,"Services","SystemInformationService.cs")).Contains("GetPhysicallyInstalledSystemMemory"),"Reliable native installed total retained");

  var reader=assembly.GetType("WinSereno.Services.MemoryModuleReader");
  Func<IList<MemoryModuleInformation>> failing=()=>{throw new IOException("simulated WMI failure");};
  var fallback=(MemoryInformation)reader.GetMethod("Enrich").Invoke(null,new object[]{32*GB,failing,logger});
  Check(fallback.InstalledBytes==32*GB&&fallback.Modules.Count==0,"WMI failure retains total");
  Check(logger.Text.Contains("simulated WMI failure"),"Optional WMI error logged");
  Apply(home,InformationBlock.Memory,fallback);
  Check(home.Cards.Single(c=>c.Block==InformationBlock.Memory).Value=="32 GB","Total visible after WMI failure");
  Check(home.Cards.Single(c=>c.Block==InformationBlock.Memory).Description=="","No ugly WMI error in metadata");
  Func<IList<MemoryModuleInformation>> missing=()=>null;
  var empty=(MemoryInformation)reader.GetMethod("Enrich").Invoke(null,new object[]{16*GB,missing,logger});
  Check(empty.InstalledBytes==16*GB&&empty.Modules.Count==0,"Absent WMI details retain total");
  Memory(home,"DDR4 · 2 × 16 GB · 2933 MT/s",Module(16,26,2933),Module(16,26,2933));
  Memory(home,"DDR4 · 1 × 16 GB · 3200 MT/s",Module(16,26,3200));
  Memory(home,"DDR3 · 2 × 8 GB · 1600 MT/s",Module(8,24,1600),Module(8,24,1600));
  Memory(home,"DDR5 · 2 × 16 GB · 5600 MT/s",Module(16,34,5600),Module(16,34,5600));
  Memory(home,"DDR4 · 2 módulos · 3200 MT/s",Module(8,26,3200),Module(16,26,3200));
  Memory(home,"DDR4 · 2 × 16 GB",Module(16,26,2933),Module(16,26,3200));
  Memory(home,"DDR4 · 2 × 16 GB",Module(16,26,0),Module(16,26,3200));
  Memory(home,"DDR4 · 2 × 16 GB",Module(16,26,65535),Module(16,26,65535));
  Memory(home,"2 × 16 GB · 3200 MT/s",Module(16,0,3200),Module(16,26,3200));
  Memory(home,"2 × 16 GB · 3200 MT/s",Module(16,24,3200),Module(16,26,3200));
  Memory(home,"DDR4 · 2 módulos",Module(0,26,0),Module(16,26,0));
  Memory(home,"1 módulo",Module(0,0,0));
  Memory(home,"");
  Memory(home,"",new MemoryModuleInformation[]{null});
  for(uint type=0;type<=40;type++) {
   Apply(home,InformationBlock.Memory,new MemoryInformation {InstalledBytes=GB,Modules=new[]{Module(1,type,0)}});
   string text=home.Cards.Single(c=>c.Block==InformationBlock.Memory).Description;
   string known=type==24?"DDR3":type==26?"DDR4":type==34?"DDR5":null;
   Check(known==null?!text.Contains("DDR"):text.StartsWith(known+" · "),"Explicit SMBIOS map only: "+type);
  }
  Apply(home,InformationBlock.Memory,new MemoryInformation {InstalledBytes=16*GB,Modules=new[]{Module(16,26,3200)}});
  Check(home.Cards.Single(c=>c.Block==InformationBlock.Memory).Value=="16 GB","Single-module total");
  Apply(home,InformationBlock.Network,new NetworkInformation {Kind="Ethernet",Description="Adaptador físico",SpeedBitsPerSecond=1000000000,IPv4="192.168.0.200"});
  var network=home.Cards.Single(c=>c.Block==InformationBlock.Network);
  Check(network.Description=="Adaptador físico\n1 Gbps · IPv4 192.168.0.200","Speed and IPv4 same line");
  Apply(home,InformationBlock.Network,new NetworkInformation {NeutralMessage="Sin adaptador principal"});
  Check(network.Value=="Sin adaptador principal","Neutral selection unchanged");
  Apply(home,InformationBlock.Network,new NetworkInformation {Kind="Ethernet",Description="Adaptador físico",IPv4="192.168.0.200"});
  Check(network.Description=="Adaptador físico\nIPv4 192.168.0.200","Missing speed omitted");
  Apply(home,InformationBlock.Network,new NetworkInformation {Kind="Ethernet",Description="Adaptador físico",SpeedBitsPerSecond=1000000000,IPv4="192.168.0.200"});
  Apply(home,InformationBlock.Windows,new WindowsInformation {ProductName="Windows 11 Pro",Build="26200",Revision=1,DisplayVersion="25H2",Architecture="x64"});
  Apply(home,InformationBlock.Cpu,new CpuInformation {Model="Procesador del equipo",PhysicalCores=8,LogicalProcessors=16});
  Apply(home,InformationBlock.Uptime,new UptimeInformation {Uptime=TimeSpan.FromHours(27)});
  var disks=new DiskCollection();
  disks.Volumes.Add(new DiskInformation {Unit="C:",Label="Windows",TotalBytes=1000,FreeBytes=99});
  disks.Volumes.Add(new DiskInformation {Unit="D:",TotalBytes=1000,FreeBytes=100});
  disks.Volumes.Add(new DiskInformation {Unit="E:",TotalBytes=1000,FreeBytes=511});
  Apply(home,InformationBlock.Disks,disks);
  Check(SystemInformationPolicy.LowDiskSpacePercentage==10.0,"Disk threshold unchanged");
  Check(home.Disks[0].NeedsAttention&&home.Disks[0].StatusText=="Atención","Below threshold warning retained");
  Check(!home.Disks[1].NeedsAttention&&home.Disks[1].StatusText=="Correcto","Threshold boundary healthy");
  Check(home.Disks[2].FreeText=="51,1 % libre","Readable percentage retained");
  Check(home.Disks[0].Name=="C: · Windows"&&home.Disks[0].CapacityText.Contains("libres de"),"All disk fields retained");
  var gpu=Gpu(new GpuInformation {Name=" NVIDIA GeForce RTX 3070 Ti ",DriverVersion=" 32.0.15.6094 "});
  Check(gpu.Name=="NVIDIA GeForce RTX 3070 Ti"&&gpu.DriverVersion=="32.0.15.6094","GPU model and driver trimmed");
  Apply(home,InformationBlock.Gpu,gpu);
  Check(home.Cards[2].Value=="NVIDIA GeForce RTX 3070 Ti","GPU model shown");
  Check(home.Cards[2].Description=="Controlador 32.0.15.6094","GPU driver shown");
  foreach(string driver in new string[]{null,""," "}) {
   var noDriver=Gpu(new GpuInformation {Name="NVIDIA GeForce RTX 3070 Ti",DriverVersion=driver});
   Apply(home,InformationBlock.Gpu,noDriver);
   Check(home.Cards[2].Value=="NVIDIA GeForce RTX 3070 Ti"&&home.Cards[2].Description=="","No unknown driver text");
  }
  Check(Gpu()==null&&Gpu(new GpuInformation {Name=" "})==null,"No named adapters becomes unavailable");
  var physical=Gpu(new GpuInformation {Name="Microsoft Remote Display Adapter",DriverVersion="10.0"},
   new GpuInformation {Name="NVIDIA GeForce RTX 3070 Ti",DriverVersion="32.0.15.6094"},
   new GpuInformation {Name="Microsoft Basic Render Driver",DriverVersion="10.0"});
  Check(physical.Name=="NVIDIA GeForce RTX 3070 Ti"&&physical.DriverVersion=="32.0.15.6094","Known remote/software adapters do not displace physical GPU");
  var unknown=Gpu(new GpuInformation {Name="Intel Graphics",DriverVersion="31.0"},
   new GpuInformation {Name="NVIDIA GeForce RTX 3070 Ti",DriverVersion="32.0"});
  Check(unknown.Name=="Intel Graphics / NVIDIA GeForce RTX 3070 Ti","Several relevant GPU names shown without inventing a primary");
  Check(unknown.DriverVersion==null,"No single driver incorrectly attributed to multiple GPUs");
  var equalDriver=Gpu(new GpuInformation {Name="GPU B",DriverVersion="32.0"},new GpuInformation {Name="GPU A",DriverVersion="32.0"});
  Check(equalDriver.Name=="GPU A / GPU B"&&equalDriver.DriverVersion=="32.0","Common multi-adapter driver is reliable");
  var duplicate=Gpu(new GpuInformation {Name="GPU A",DriverVersion="32.0"},new GpuInformation {Name="gpu a",DriverVersion="32.0"});
  Check(duplicate.Name=="GPU A","Distinct GPU names, case insensitive");
  var incomplete=Gpu(new GpuInformation {Name="GPU A",DriverVersion="32.0"},new GpuInformation {Name="GPU B"});
  Check(incomplete.DriverVersion==null,"Incomplete drivers not misrepresented as shared");
  Check(Gpu(new GpuInformation {Name="Microsoft Basic Display Adapter"}).Name=="Microsoft Basic Display Adapter","Basic display adapter remains eligible");
  Check(Gpu(new GpuInformation {Name="Microsoft Remote Display Adapter"}).Name=="Microsoft Remote Display Adapter","Only remote adapter remains informative");
  Check(Gpu(null,new GpuInformation {Name="GPU A"}).Name=="GPU A","Nameless/null records omitted");
  string systemSource=File.ReadAllText(Path.Combine(project,"Services","SystemInformationService.cs"));
  Check(systemSource.Contains("SELECT Name, DriverVersion FROM Win32_VideoController"),"GPU query limited to requested fields");
  Check(!systemSource.Contains("AdapterRAM")&&!typeof(GpuInformation).GetProperties().Any(p=>p.Name.Contains("RAM")),"No GPU RAM query or model");
  var service=new SystemInformationService(logger);
  var readAsync=typeof(SystemInformationService).GetMethod("ReadAsync",BindingFlags.Instance|BindingFlags.NonPublic);
  var before=home.Cards.Where(c=>c.Block!=InformationBlock.Gpu).Select(c=>c.Value+"|"+c.Description).ToArray();
  var gpuFailure=new HomeGpuProgress();
  Func<object> failedGpu=()=>{throw new IOException("simulated GPU WMI failure");};
  ((System.Threading.Tasks.Task)readAsync.Invoke(service,new object[]{InformationBlock.Gpu,failedGpu,gpuFailure,System.Threading.CancellationToken.None})).GetAwaiter().GetResult();
  Check(gpuFailure.Update.Block==InformationBlock.Gpu&&gpuFailure.Update.Status==InformationStatus.Failed,"GPU provider failure controlled independently");
  typeof(HomeViewModel).GetMethod("ApplyUpdate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(home,new object[]{gpuFailure.Update});
  Check(home.Cards[2].Value=="No se pudo consultar"&&home.Cards[2].Description=="","GPU failure uses existing Home unavailable behavior");
  Check(home.Cards.Where(c=>c.Block!=InformationBlock.Gpu).Select(c=>c.Value+"|"+c.Description).SequenceEqual(before)&&home.Disks.Count==3,"Other Home cards/disks survive GPU failure");
  Check(logger.Text.Contains("simulated GPU WMI failure"),"GPU failure logged");
  var gpuEmpty=new HomeGpuProgress();Func<object> noGpu=()=>null;
  ((System.Threading.Tasks.Task)readAsync.Invoke(service,new object[]{InformationBlock.Gpu,noGpu,gpuEmpty,System.Threading.CancellationToken.None})).GetAwaiter().GetResult();
  typeof(HomeViewModel).GetMethod("ApplyUpdate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(home,new object[]{gpuEmpty.Update});
  Check(home.Cards[2].Value=="No disponible"&&home.Cards[2].Description=="","Missing GPU unavailable without garbage");
  Apply(home,InformationBlock.Gpu,gpu);
  string cpuName="Intel(R) Core(TM) i7-10700K CPU @ 3.80GHz";
  string[][] names={
   new[]{"Intel(R) Core(TM) i7-10700K CPU @ 3.80GHz","Intel Core i7-10700K"},
   new[]{"Intel(R) Core(TM) i7-8700T CPU @ 2.40GHz","Intel Core i7-8700T"},
   new[]{"Intel(R) Core(TM) i9-14900KF","Intel Core i9-14900KF"},
   new[]{"Intel(R) Core(TM) Ultra 7 265K","Intel Core Ultra 7 265K"},
   new[]{"AMD Ryzen 5 7530U with Radeon Graphics","AMD Ryzen 5 7530U"},
   new[]{"AMD EPYC 7763 64-Core Processor","AMD EPYC 7763"}};
  foreach(var pair in names) {
   Apply(home,InformationBlock.Cpu,new CpuInformation {Model=pair[0],PhysicalCores=8,LogicalProcessors=16,MaxClockSpeedMHz=3800});
   Check(home.Cards[1].Value==pair[1],"Required CPU name normalization: "+pair[0]+" -> "+pair[1]);
   Check(home.Cards[1].Description=="8 núcleos · 16 hilos · 3.8 GHz","Structured frequency independent of CPU name");
  }
  foreach(string original in new[]{"AMD Ryzen 7 7800X3D","Custom CPU @ fast","Custom @ 3.80GHz","Acme Processor","Intel Processor","Unknown 12-Core Device","CPU Model (ES)"}) {
   Apply(home,InformationBlock.Cpu,new CpuInformation {Model=original,PhysicalCores=8,LogicalProcessors=16});
   Check(home.Cards[1].Value==original,"Unrecognized CPU names not mutilated: "+original);
   Check(home.Cards[1].Description=="8 núcleos · 16 hilos","Unreliable frequency omitted: "+original);
  }
  foreach(string input in new[]{"Custom CPU @ 3.8 GHz","Custom cpu @ 3,80ghz","Custom CPU @ 3.80GHz"}) {
   Apply(home,InformationBlock.Cpu,new CpuInformation {Model=input});
   Check(home.Cards[1].Value=="Custom"&&home.Cards[1].Description=="","Suffix removed but never used as frequency fallback: "+input);
  }
  foreach(var pair in new[]{new[]{"3800","3.8 GHz"},new[]{"2400","2.4 GHz"},new[]{"3900","3.9 GHz"},new[]{"2000","2.0 GHz"},new[]{"3792","3.8 GHz"}}) {
   int mhz=int.Parse(pair[0]);Check(ReliableSpeed(mhz)==mhz,"Valid structured frequency: "+mhz);
   Apply(home,InformationBlock.Cpu,new CpuInformation {Model="Modern CPU",PhysicalCores=8,LogicalProcessors=16,MaxClockSpeedMHz=mhz});
   Check(home.Cards[1].Description=="8 núcleos · 16 hilos · "+pair[1],"MHz to GHz formatting: "+mhz);
  }
  Apply(home,InformationBlock.Cpu,new CpuInformation {Model=cpuName,PhysicalCores=8,LogicalProcessors=16,MaxClockSpeedMHz=2400});
  Check(home.Cards[1].Description=="8 núcleos · 16 hilos · 2.4 GHz","MaxClockSpeed overrides a contradictory name frequency");
  Apply(home,InformationBlock.Cpu,new CpuInformation {Model=cpuName,PhysicalCores=8,LogicalProcessors=16});
  Check(home.Cards[1].Description=="8 núcleos · 16 hilos","No MaxClockSpeed means no frequency fallback from name");
  Check(ReliableSpeed(3800,3800)==3800,"Equal valid multi-CPU frequencies");
  Check(ReliableSpeed(3800,2400)==null&&ReliableSpeed(2400,3800)==null,"Different multi-CPU frequencies omitted regardless of order");
  Check(ReliableSpeed(3800,null)==null&&ReliableSpeed(null,3800)==null,"Incomplete multi-CPU frequency omitted");
  Check(ReliableSpeed()==null,"No physical CPU frequency data");
  foreach(object invalid in new object[]{null,-1,0,1,99,20001,int.MaxValue,uint.MaxValue,"bad","3800.5"}) {
   Check(ReliableSpeed(invalid)==null&&ReliableSpeed(3800,invalid)==null,"Invalid structured frequency rejected: "+invalid);
  }
  foreach(int invalid in new[]{-1,0,99,20001}) {
   Apply(home,InformationBlock.Cpu,new CpuInformation {Model=cpuName,PhysicalCores=8,LogicalProcessors=16,MaxClockSpeedMHz=invalid});
   Check(home.Cards[1].Description=="8 núcleos · 16 hilos","Invalid model frequency cannot appear in UI: "+invalid);
  }
  Apply(home,InformationBlock.Cpu,new CpuInformation {Model="First CPU @ 3.00GHz / Second CPU @ 4.00GHz",PhysicalCores=16,LogicalProcessors=32,MaxClockSpeedMHz=ReliableSpeed(3000,4000)});
  Check(home.Cards[1].Value=="First / Second"&&home.Cards[1].Description=="16 núcleos · 32 hilos","Multiple CPU names cleaned independently, unequal frequency omitted");
  Apply(home,InformationBlock.Cpu,new CpuInformation {Model=" Intel(R)   Xeon(TM)   Processor ",MaxClockSpeedMHz=2000});
  Check(home.Cards[1].Value=="Intel Xeon","Generic known-vendor Processor suffix and duplicate spaces normalized");
  Check(systemSource.Contains("NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor"),"MaxClockSpeed queried as structured CPU data");
  var intel=new GpuInformation {Name="Intel(R) UHD Graphics 630",DriverVersion="31.0.101.2140"};
  var nvidia=new GpuInformation {Name="NVIDIA GeForce RTX 3070 Ti",DriverVersion="32.0.16.1692"};
  var mixed=Gpu(intel,nvidia,new GpuInformation {Name="Microsoft Remote Display Adapter",DriverVersion="10.0"},
   new GpuInformation {Name="Microsoft Basic Render Driver",DriverVersion="10.0"});
  Check(mixed.IsDedicated&&mixed.Name==nvidia.Name&&mixed.IntegratedName==intel.Name,"Intel integrated and NVIDIA separated; software ignored");
  Check(mixed.DriverVersion==nvidia.DriverVersion,"Only dedicated driver retained");
  Apply(home,InformationBlock.Cpu,new CpuInformation {Model=cpuName,PhysicalCores=8,LogicalProcessors=16,MaxClockSpeedMHz=3800});
  Apply(home,InformationBlock.Gpu,mixed);
  Check(home.Cards[1].Description=="8 núcleos · 16 hilos · 3.8 GHz\nIntel UHD Graphics 630","CPU secondary lines exactly as requested");
  Check(home.Cards[2].Title=="GPU dedicada"&&home.Cards[2].Value==nvidia.Name&&home.Cards[2].Description=="Controlador 32.0.16.1692","Dedicated GPU presentation exactly as requested");
  var amd=new GpuInformation {Name="AMD Radeon RX 6800 XT",DriverVersion="31.0.1"};
  var intelAmd=Gpu(intel,amd);
  Check(intelAmd.IsDedicated&&intelAmd.Name==amd.Name&&intelAmd.IntegratedName==intel.Name,"Intel integrated and AMD dedicated recognized");
  Apply(home,InformationBlock.Gpu,intelAmd);
  Check(home.Cards[1].Description.EndsWith("\nIntel UHD Graphics 630")&&home.Cards[2].Title=="GPU dedicada"&&home.Cards[2].Value==amd.Name,"AMD/Intel split presentation");
  var onlyIntel=Gpu(intel);
  Check(!onlyIntel.IsDedicated&&onlyIntel.IntegratedName==null&&onlyIntel.Name==intel.Name,"Only Intel remains generic GPU, not invented dedicated");
  Apply(home,InformationBlock.Gpu,onlyIntel);
  Check(home.Cards[2].Title=="GPU"&&home.Cards[2].Value=="Intel UHD Graphics 630"&&!home.Cards[1].Description.Contains("Intel UHD Graphics 630"),"Only Intel shown with clean name, without duplicated GPU line");
  foreach(var dedicated in new[]{nvidia,amd}) {
   var single=Gpu(dedicated);
   Apply(home,InformationBlock.Gpu,single);
   Check(single.IsDedicated&&single.IntegratedName==null&&home.Cards[2].Title=="GPU dedicada","Single clearly dedicated model: "+dedicated.Name);
   Check(!home.Cards[1].Description.Contains("\n"),"No fabricated integrated GPU: "+dedicated.Name);
   var noDriver=Gpu(intel,new GpuInformation {Name=dedicated.Name});
   Apply(home,InformationBlock.Gpu,noDriver);
   Check(home.Cards[2].Title=="GPU dedicada"&&home.Cards[2].Value==dedicated.Name&&home.Cards[2].Description=="","Dedicated driver absent omits description: "+dedicated.Name);
  }
  foreach(var ambiguous in new[]{
   Gpu(new GpuInformation {Name="Intel Arc A770"},nvidia),
   Gpu(new GpuInformation {Name="Intel(R) Iris(R) Xe MAX Graphics"},nvidia),
   Gpu(intel,new GpuInformation {Name="AMD Radeon(TM) Graphics"}),
   Gpu(intel,nvidia,new GpuInformation {Name="Other Graphics"})}) {
   Check(!ambiguous.IsDedicated&&ambiguous.IntegratedName==null,"Ambiguous families/combinations stay generic");
   Apply(home,InformationBlock.Gpu,ambiguous);
   Check(home.Cards[2].Title=="GPU"&&home.Cards[2].Value==GraphicsName(ambiguous.Name),"No invented classification or lost names");
  }
  var arrival=new HomeViewModel(null,logger);
  Apply(arrival,InformationBlock.Gpu,mixed);
  Check(arrival.Cards[1].Value=="Consultando...","GPU arriving first does not invent CPU information");
  Apply(arrival,InformationBlock.Cpu,new CpuInformation {Model=cpuName,PhysicalCores=8,LogicalProcessors=16,MaxClockSpeedMHz=3800});
  Check(arrival.Cards[1].Description=="8 núcleos · 16 hilos · 3.8 GHz\nIntel UHD Graphics 630","GPU-first arrival keeps clean integrated line when CPU arrives");
  typeof(HomeViewModel).GetMethod("ApplyUpdate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(arrival,new object[]{new InformationUpdate {Block=InformationBlock.Gpu,Status=InformationStatus.Failed}});
  Check(arrival.Cards[1].Description=="8 núcleos · 16 hilos · 3.8 GHz"&&arrival.Cards[2].Title=="GPU","GPU failure clears stale classification only");
  typeof(HomeViewModel).GetMethod("ApplyUpdate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(arrival,new object[]{new InformationUpdate {Block=InformationBlock.Cpu,Status=InformationStatus.Failed}});
  Apply(arrival,InformationBlock.Gpu,mixed);
  Check(arrival.Cards[1].Value=="No se pudo consultar","GPU update cannot resurrect failed CPU data");
  Apply(home,InformationBlock.Cpu,new CpuInformation {Model=cpuName,PhysicalCores=8,LogicalProcessors=16,MaxClockSpeedMHz=3800});
  Apply(home,InformationBlock.Gpu,mixed);
  gpu=mixed;
  var restartResult=new MaintenanceTaskResult {RequiresRestart=true,FindingStatus=FindingStatus.Healthy,ExecutionStatus=ExecutionStatus.Success};
  var history=new ActionHistoryService();
  history.Record(new TaskProgress {State=RunnerState.Completed,CurrentTask=new MaintenanceTask {Id="operation",Name="Operation"},Result=restartResult});
  Check(history.Entries.Single().StateText.Contains("Reinicio requerido"),"Operation-specific restart notice retained");
  Check(Enum.GetNames(typeof(FindingStatus)).Contains("RestartRequired"),"Operation restart enum retained");

  var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
  app.Resources.MergedDictionaries.Add(new ResourceDictionary {Source=new Uri("/WinSereno;component/Themes/Styles.xaml",UriKind.Relative)});
  var dialogs=new HomeCheckDialogs();var operations=new OperationCoordinator();var integrity=new IntegritySessionState();
  var vm=new MainViewModel(new PortableStorage(project),new AppSettings(),new ThemeService(),dialogs,new HomeCheckRunner(),home,
   new DiagnosticViewModel(new DiagnosticService(null,logger,integrity),operations,logger,dialogs,integrity),operations,integrity,logger);
  Check(vm.PageDescription=="Información del equipo obtenida directamente desde Windows.","Single Home introductory sentence");
  Check(!vm.HasPageNotice&&vm.PageNotice=="","Home notice empty");
  int notifications=0;vm.PropertyChanged+=(s,e)=>{if(e.PropertyName=="HasPageNotice")notifications++;};
  vm.Navigate(NavigationSection.Settings);vm.Navigate(NavigationSection.Home);
  Check(notifications==2,"Notice visibility notification on navigation");
  foreach(string theme in new[]{"Light","Dark"}) {
   var colors=new ResourceDictionary {Source=new Uri("/WinSereno;component/Themes/"+theme+".xaml",UriKind.Relative)};
   app.Resources.MergedDictionaries.Add(colors);
   var window=new MainWindow(vm,dialogs);var content=(FrameworkElement)window.Content;Layout(content);
   var notice=(Border)Logical<TextBlock>(content).Single(t=>BindingPath(t,TextBlock.TextProperty)=="PageNotice").Parent;
   Check(notice.Visibility==Visibility.Collapsed&&notice.DesiredSize.Height==0,"Home notice really collapsed: "+theme);
   Check(((Grid)notice.Parent).RowDefinitions[1].ActualHeight==0,"No banner margin gap: "+theme);
   var cards=Logical<ItemsControl>(content).Single(c=>ReferenceEquals(c.ItemsSource,home.Cards));
   var volumes=Logical<ItemsControl>(content).Single(c=>ReferenceEquals(c.ItemsSource,home.Disks));
   Check(cards.Items.Count==6&&volumes.Items.Count==3,"All six cards and volumes present: "+theme);
   Check(cards.ItemsPanel.LoadContent() is UniformGrid&&((UniformGrid)cards.ItemsPanel.LoadContent()).Columns==3&&volumes.ItemsPanel.LoadContent() is WrapPanel,"Three-column Home grid, independent disk wrapping retained: "+theme);
   var cardBorders=Visual<Border>(cards).Where(b=>ReferenceEquals(b.Style,window.FindResource("ThreeColumnCard"))).ToList();
   var diskBorders=Visual<Border>(volumes).Where(b=>b.Width==252).ToList();
   Check(cardBorders.Count==6&&diskBorders.Count==3,"Six stretched information cards and three unchanged disk cards: "+theme);
   var networkCards=(ItemsControl)window.FindName("NetworkAdapterCards");
   Check(ReferenceEquals(cards.Style,networkCards.Style)&&ReferenceEquals(cards.ItemsPanel,networkCards.ItemsPanel),"Home and Network reuse the same layout resources: "+theme);
   CheckGrid(window,content,cards,home,theme+" 1150");
   var gpuBorder=cardBorders.Single(b=>ReferenceEquals(b.DataContext,home.Cards[2]));
   Check(gpuBorder.Padding==new Thickness(12)&&ReferenceEquals(gpuBorder.Style,cardBorders[0].Style),"GPU uses identical card padding/style: "+theme);
   Check(Equals(gpuBorder.Background,cardBorders[0].Background),"GPU themed background matches: "+theme);
   Check(Visual<TextBlock>(gpuBorder).Any(t=>t.Text==gpu.Name&&t.FontSize==17),"GPU model hierarchy preserved: "+theme);
   Check(Visual<TextBlock>(gpuBorder).Any(t=>t.Text=="Controlador "+gpu.DriverVersion&&t.FontSize==12),"GPU driver hierarchy preserved: "+theme);
   Check(Visual<TextBlock>(gpuBorder).Any(t=>t.Text=="GPU dedicada"),"Dedicated GPU title binding: "+theme);
   var cpuBorder=cardBorders.Single(b=>ReferenceEquals(b.DataContext,home.Cards[1]));
   Check(Visual<TextBlock>(cpuBorder).Any(t=>t.Text=="8 núcleos · 16 hilos · 3.8 GHz\nIntel UHD Graphics 630"&&t.FontSize==12&&Equals(t.Foreground,app.Resources["MutedBrush"])),"Both CPU secondary lines use existing muted style: "+theme);
   var positions=cardBorders.Select(b=>b.TransformToAncestor(cards).Transform(new Point())).ToList();
   Check(positions.Take(3).All(p=>p.Y==positions[0].Y)&&positions.Skip(3).All(p=>p.Y==positions[3].Y)&&positions[3].Y>positions[0].Y,"Three by two Home rows: "+theme);
   Check(Enumerable.Range(0,3).All(i=>positions[i].X==positions[i+3].X),"Three aligned Home columns: "+theme);
   Check(diskBorders.All(b=>b.Background is SolidColorBrush),"Disk card themed surfaces: "+theme);
   var lowText=Visual<TextBlock>(volumes).Single(t=>t.Text=="Atención");
   Check(Equals(lowText.Foreground,app.Resources["WarningBrush"]),"Warning themed color: "+theme);
   Check(Visual<TextBlock>(volumes).Where(t=>t.Text=="Correcto").All(t=>Equals(t.Foreground,app.Resources["GoodBrush"])),"Healthy themed colors: "+theme);
   var scroll=(ScrollViewer)window.FindName("PageScroll");
   Check(scroll.ScrollableHeight==0,"Normal-size Home fits without scrolling: "+theme);
   var imageDir=Path.Combine(project,"bin","Debug","VisualChecks");Directory.CreateDirectory(imageDir);Save(content,Path.Combine(imageDir,"HomeThreeColumns-"+theme+".png"));
   double normalWidth=cardBorders[0].ActualWidth;
   Layout(content,1280);CheckGrid(window,content,cards,home,theme+" 1280");
   Check(cardBorders[0].ActualWidth>normalWidth,"Cards expand with available width: "+theme);
   Layout(content,900,550);
   CheckGrid(window,content,cards,home,theme+" 900");
   Check(cardBorders[0].ActualWidth<normalWidth,"Cards shrink while maintaining three columns: "+theme);
   Check(scroll.VerticalScrollBarVisibility==ScrollBarVisibility.Auto,"Small-window scrolling retained: "+theme);
   Check(Logical<Button>(content).Any(b=>Convert.ToString(b.Content)=="Actualizar"&&ReferenceEquals(b.Command,home.RefreshCommand)),"Refresh command retained");
   window.Close();
   foreach(var item in vm.Navigation.Where(n=>n.Section!=NavigationSection.Home&&n.Section!=NavigationSection.Diagnosis&&n.Section!=NavigationSection.Repair&&n.Section!=NavigationSection.Network&&n.Section!=NavigationSection.Cleanup)) {
    // Change only the test backing field: normal Network navigation would perform real connectivity queries.
    typeof(MainViewModel).GetField("selectedNavigation",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(vm,item);
    Check(vm.HasPageNotice&&!string.IsNullOrWhiteSpace(vm.PageNotice),"Other notice content retained: "+item.Label);
    var other=new MainWindow(vm,dialogs);Layout((FrameworkElement)other.Content);
    var otherNotice=(Border)Logical<TextBlock>((FrameworkElement)other.Content).Single(t=>BindingPath(t,TextBlock.TextProperty)=="PageNotice").Parent;
    Check(otherNotice.Visibility==Visibility.Visible&&otherNotice.DesiredSize.Height>0,"Other notice visible: "+item.Label+" / "+theme);
    other.Close();
   }
   typeof(MainViewModel).GetField("selectedNavigation",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(vm,vm.Navigation[0]);
   app.Resources.MergedDictionaries.Remove(colors);
  }
  string xaml=File.ReadAllText(Path.Combine(project,"Views","MainWindow.xaml"));
  Check(!xaml.Contains("xmlns:local")&&!xaml.Contains("x:Static m:"),"No local compiled XAML type dependencies");
  Check(!File.Exists(Path.Combine(project,"Services","RestartPendingService.cs")),"Unused detector file removed");
  return count+" checks passed: simulated Home data, WMI failures, operation restart notices and both themes; no real analysis, network, maintenance or UAC.";
 }
}
'@
$provider = [Microsoft.CSharp.CSharpCodeProvider]::new()
$parameters = [CodeDom.Compiler.CompilerParameters]::new()
$parameters.GenerateInMemory = $true
foreach ($reference in @($exe,'System.dll','System.Core.dll','System.Xml.dll','System.Xaml.dll','System.Runtime.Serialization.dll',
    [System.Windows.Application].Assembly.Location,[System.Windows.Media.Visual].Assembly.Location,[System.Windows.DependencyObject].Assembly.Location)) {
    [void]$parameters.ReferencedAssemblies.Add($reference)
}
$compiled = $provider.CompileAssemblyFromSource($parameters,$source)
if ($compiled.Errors.HasErrors) { throw ($compiled.Errors | Out-String) }
[HomeInformationChecks]::Run($project)
