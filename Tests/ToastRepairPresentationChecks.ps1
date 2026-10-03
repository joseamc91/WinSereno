$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $project 'bin\Debug\WinSereno.exe'
[void][Reflection.Assembly]::LoadFrom($exe)
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Xaml
$source = @"
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinSereno.Models;
using WinSereno.Services;
using WinSereno.ViewModels;
using WinSereno.Views;
public sealed class ToastRunner : IMaintenanceTaskRunner {
 public TaskProgress Current {get;private set;} public bool IsActive {get {return Current!=null&&Current.State==RunnerState.Running;}}
 public event EventHandler<TaskProgress> ProgressChanged;
 public void Publish(TaskProgress value) {Current=value;if(ProgressChanged!=null)ProgressChanged(this,value);}
 public Task<MaintenanceTaskResult> RunAsync(MaintenanceTask task) {throw new Exception("No real tasks permitted");}
 public bool RequestCancellation() {return true;} public Task WaitForIdleAsync() {return Task.CompletedTask;}
}
public sealed class ToastDialogs : IDialogService {
 public TaskProgress Output; public void ShowOutput(TaskProgress value) {Output=value;}
 public void ShowDiagnosticDetails(DiagnosticResult result) {} public void ShowMessage(string text) {throw new Exception(text);}
 public bool ConfirmTask(MaintenanceTask task) {throw new Exception("No commands/UAC permitted");} public bool ConfirmCancelAndClose() {return false;}
 public RestartAdapter SelectRestartAdapter(IReadOnlyList<RestartAdapter> values) {throw new Exception("No network permitted");}
}
public sealed class ToastLogger : ISessionLogger {
 public void Write(string text) {} public void TaskStarted(MaintenanceTask task) {} public void TaskFinished(MaintenanceTask task,MaintenanceTaskResult result) {} public void Dispose() {}
}
public static class ToastRepairPresentationChecks {
 static int count; static void Check(bool value,string label) {if(!value)throw new Exception(label);count++;}
 static IEnumerable<T> Visual<T>(DependencyObject parent) where T:DependencyObject {
  if(parent is T)yield return (T)parent;
  for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)foreach(var value in Visual<T>(VisualTreeHelper.GetChild(parent,i)))yield return value;
 }
 static bool Shown(DependencyObject item) {for(var p=item;p!=null;p=VisualTreeHelper.GetParent(p))if(p is UIElement&&((UIElement)p).Visibility!=Visibility.Visible)return false;return true;}
 static void Layout(FrameworkElement root,double width=1150,double height=760) {root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();}
 static string BindingPath(DependencyObject item,DependencyProperty property) {var b=BindingOperations.GetBinding(item,property);return b==null?null:b.Path.Path;}
 static TaskProgress Completed(string id,string summary="Resultado completo.") {
  var r=new MaintenanceTaskResult {StartedAt=DateTimeOffset.Now.AddSeconds(-4),FinishedAt=DateTimeOffset.Now,Duration=TimeSpan.FromSeconds(4),ExecutionStatus=ExecutionStatus.Success,FindingStatus=FindingStatus.Healthy,UserSummary=summary,StdOut="Full stdout",StdErr="Full stderr"};
  return new TaskProgress {CurrentTask=new MaintenanceTask {Id=id,Name=id=="diagnosis.general"?"Diagnóstico":"Tarea de prueba"},State=RunnerState.Completed,Result=r,Elapsed=r.Duration,StdOut=r.StdOut,StdErr=r.StdErr};
 }
 static void DiagnosticCounts(MainViewModel vm,ToastRunner runner,int good,int attention,int errors,int unknown,string expected) {
  vm.Diagnosis.Results.Clear();int index=0;
  foreach(var pair in new[]{Tuple.Create(DiagnosticStatus.Healthy,good),Tuple.Create(DiagnosticStatus.Attention,attention),Tuple.Create(DiagnosticStatus.Error,errors),Tuple.Create(DiagnosticStatus.NotChecked,unknown)})
   for(int i=0;i<pair.Item2;i++)vm.Diagnosis.Results.Add(new DiagnosticResult {Id=(index++).ToString(),Status=pair.Item1});
  runner.Publish(Completed("diagnosis.general","Texto deliberadamente sin contadores"));
  Check(string.Concat(vm.DiagnosticToastCounts.Select(c=>c.Separator+c.Text))==expected,"Structured counters "+expected);
  Check(vm.ShowDiagnosticToastCounts&&vm.DiagnosticToastCounts.Count==new[]{good,attention,errors,unknown}.Count(n=>n>0),"Zero counts absent");
  Check(vm.StatusText=="Diagnóstico finalizado.","No parsing of supplied summary");
 }
 static void Save(FrameworkElement root,string path) {
  var bitmap=new RenderTargetBitmap((int)root.ActualWidth,(int)root.ActualHeight,96,96,PixelFormats.Pbgra32);
  var backdrop=new DrawingVisual();using(var drawing=backdrop.RenderOpen())drawing.DrawRectangle((Brush)Application.Current.Resources["BackgroundBrush"],null,new Rect(0,0,root.ActualWidth,root.ActualHeight));bitmap.Render(backdrop);bitmap.Render(root);
  var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(path))encoder.Save(file);
 }
 public static string Run(string project) {
  var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};app.Resources.MergedDictionaries.Add(new ResourceDictionary {Source=new Uri("/WinSereno;component/Themes/Styles.xaml",UriKind.Relative)});
  var expectedTitles=new[]{"Comprobar integridad de Windows","Analizar imagen de Windows","Reparar imagen de Windows","Comprobar archivos del sistema","Limpiar almacén de componentes","Comprobar disco"};
  var expectedTech=new[]{"DISM /CheckHealth","DISM /ScanHealth","DISM /RestoreHealth","SFC /scannow","DISM /StartComponentCleanup","CHKDSK"};
  var expectedDescriptions=new[]{"Comprueba rápidamente si Windows tiene registrada corrupción en el almacén de componentes.","Busca corrupción en profundidad sin realizar reparaciones.","Busca y repara corrupción del almacén de componentes de Windows.","Comprueba los archivos protegidos de Windows y repara los dañados cuando es posible.","Elimina versiones reemplazadas de componentes que Windows ya no necesita.","Comprueba el sistema de archivos de la unidad de Windows sin realizar reparaciones."};
  foreach(string theme in new[]{"Light","Dark"}) {
   var colors=new ResourceDictionary {Source=new Uri("/WinSereno;component/Themes/"+theme+".xaml",UriKind.Relative)};app.Resources.MergedDictionaries.Add(colors);
   var ops=new OperationCoordinator();var runner=new ToastRunner();var dialogs=new ToastDialogs();var logger=new ToastLogger();var integrity=new IntegritySessionState();
   var diagnosis=new DiagnosticViewModel(null,ops,logger,dialogs,integrity);
   var vm=new MainViewModel(new PortableStorage(project),new AppSettings(),new ThemeService(),dialogs,runner,new HomeViewModel(null,logger),diagnosis,ops,integrity,logger);
   var window=new MainWindow(vm,dialogs);var root=(FrameworkElement)window.Content;Layout(root);
   var layer=(Canvas)window.FindName("ToastLayer");var host=(ContentControl)window.FindName("TaskExecutionHost");var main=(Grid)window.FindName("MainArea");var scroll=(ScrollViewer)window.FindName("PageScroll");
   var defaultScroll=new ScrollViewer();Check(defaultScroll.Focusable&&defaultScroll.FocusVisualStyle!=null,"WPF ScrollViewer defaults to focusable with focus visual");
   string focusTemplate=System.Windows.Markup.XamlWriter.Save(app.FindResource(SystemParameters.FocusVisualStyleKey));
   Check(focusTemplate.Contains("StrokeDashArray=\"1 2\""),"Native focus visual is the dotted rectangle, not a page Border");
   Check(scroll.Focusable&&scroll.FocusVisualStyle!=null,"Other pages retain ScrollViewer keyboard focus visual");
   Check(host.Parent==layer&&layer.Parent==main&&Grid.GetColumn(main)==1,"Overlay only in main area");
   Check(main.RowDefinitions.Count==3&&Grid.GetRowSpan(layer)==3&&layer.DesiredSize.Height==16,"Canvas does not reserve page height (margin only)");
   Check(Canvas.GetRight(host)==0&&Canvas.GetBottom(host)==0&&Panel.GetZIndex(layer)>0,"Bottom right above page");
   double originalHeight=scroll.ActualHeight,originalScrollable=scroll.ScrollableHeight;
   var active=new TaskProgress {CurrentTask=new MaintenanceTask {Id="repair.complete",Name="Reparación completa",CanBeCancelled=false},State=RunnerState.Running,StepLabel="Paso 2 de 3",LastRelevantLine="Salida relevante",Percentage=37,Elapsed=TimeSpan.FromSeconds(7),StdOut="Full stream"};
   var panel=(TaskExecutionPanel)host.Content;var close=Visual<Button>(panel).Single(b=>Convert.ToString(b.Content)=="X");var cancel=Visual<Button>(panel).Single(b=>Convert.ToString(b.Content)=="Cancelar");var details=Visual<Button>(panel).Single(b=>Convert.ToString(b.Content)=="Ver detalles");
   using(var lease=ops.Begin("Simulated repair",false)) {
    runner.Publish(active);Layout(root);
    Check(vm.ShowTaskPanel&&!Shown(close)&&!Shown(cancel),"Active noncancelable toast has neither X nor cancel");
    Check(vm.HasPercentage&&Visual<ProgressBar>(panel).Single().Value==37&&Shown(Visual<ProgressBar>(panel).Single()),"Only real progress shown");
    Check(scroll.ActualHeight==originalHeight&&scroll.ScrollableHeight==originalScrollable,"Toast changes neither page height nor scrolling");
    Check(host.Width==500,"Normal toast width 500");
    vm.DismissTaskPanelCommand.Execute(null);Check(vm.ShowTaskPanel&&ops.IsActive,"Cannot dismiss active operation");
    vm.DetailsCommand.Execute(null);Check(dialogs.Output.StdOut=="Full stream","Live details unchanged");
    foreach(var nav in vm.Navigation) {typeof(MainViewModel).GetField("selectedNavigation",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(vm,nav);Check(vm.ShowTaskPanel&&host.Parent==layer,"Global across "+nav.Label);}
    scroll.ScrollToVerticalOffset(100);Layout(root);double y=host.TranslatePoint(new Point(),main).Y;scroll.ScrollToTop();Layout(root);Check(y==host.TranslatePoint(new Point(),main).Y,"Fixed while page scrolls");
    runner.Publish(Completed("repair.complete"));Layout(root);Check(!Shown(close),"X waits for global idle");
   }
   Layout(root);Check(Shown(close)&&vm.CanDismissTaskPanel,"X shown only at completion");
   Check(Equals(close.Background,app.Resources["ToastCloseBrush"])&&Equals(close.Foreground,app.Resources["ToastTextBrush"]),"Red X with white label");
   Check(Visual<TextBlock>(close).Any()&&Visual<TextBlock>(close).All(t=>Equals(t.Foreground,app.Resources["ToastTextBrush"])),"Rendered X stays white in both themes");
   Check(Visual<TextBlock>(details).Any()&&Visual<TextBlock>(details).All(t=>Equals(t.Foreground,app.Resources["ToastTextBrush"])),"Rendered button text contrasts with dark toast");
   Check(((SolidColorBrush)close.Background).Color.R>((SolidColorBrush)close.Background).Color.G*2,"Close is clearly red");
   var shell=(Border)panel.FindName("ToastShell");var toastColor=((SolidColorBrush)shell.Background).Color;var cardColor=((SolidColorBrush)app.Resources["SurfaceBrush"]).Color;
   Check(Equals(shell.Background,app.Resources["ToastBackgroundBrush"])&&Equals(shell.BorderBrush,app.Resources["ToastBorderBrush"]),"Toast uses theme resources");
   var shadow=shell.Effect as System.Windows.Media.Effects.DropShadowEffect;Check(shadow!=null&&Equals(shadow,app.Resources["ToastShadowEffect"]),"Theme-specific floating shadow");
   if(theme=="Light") {
    Check(toastColor==Color.FromRgb(20,30,45)&&((SolidColorBrush)shell.BorderBrush).Color==Color.FromRgb(52,67,89),"Light dark surface and border unchanged");
    Check(shadow.BlurRadius==12&&shadow.ShadowDepth==2&&shadow.Opacity==0.25,"Light shadow unchanged");
   } else {
    Check(toastColor.R>0&&toastColor.R<cardColor.R&&toastColor.G<cardColor.G&&toastColor.B<cardColor.B,"Dark toast is deeper navy than cards, not pure black");
    var edge=((SolidColorBrush)shell.BorderBrush).Color;var cardEdge=((SolidColorBrush)app.Resources["BorderBrush"]).Color;
    Check(edge.R>cardEdge.R&&edge.G>cardEdge.G&&edge.B>cardEdge.B,"Dark toast border more visible than card border");
    Check(shadow.BlurRadius==20&&shadow.ShadowDepth==3&&shadow.Opacity==0.45,"Dark shadow strengthened");
   }
   Check(((SolidColorBrush)app.Resources["ToastTextBrush"]).Color==Colors.White,"High-contrast toast text");
   int history=vm.ActionHistory.Count;vm.DismissTaskPanelCommand.Execute(null);Layout(root);Check(!vm.ShowTaskPanel&&vm.ActionHistory.Count==history,"Dismiss retains history");
   vm.HistoryDetailsCommand.Execute(vm.ActionHistory[0]);Check(dialogs.Output.StdOut.Contains("Full stdout"),"Historical details retained");
   using(var lease=ops.Begin("Simulated cancelable",true)) {
    active.CurrentTask.CanBeCancelled=true;active.Percentage=null;runner.Publish(active);Layout(root);
    Check(vm.ShowTaskPanel&&Shown(cancel)&&!Shown(close)&&!vm.HasPercentage,"New action reopens and valid cancellation visible");
    Check(!Shown(Visual<ProgressBar>(panel).Single()),"Absent percentage hides progress bar");
   }
   var longSummary=string.Join(" ",Enumerable.Repeat("Resultado extenso sin recortar información importante.",12));runner.Publish(Completed("repair.test",longSummary));Layout(root);
   var summary=Visual<TextBlock>(panel).Single(t=>t.Text==longSummary);Check(summary.TextWrapping==TextWrapping.Wrap&&summary.ActualHeight>30,"Long summary wraps intact");
   foreach(double width in new[]{1150.0,900.0,640.0,450.0}) {
    Layout(root,width,760);var point=host.TranslatePoint(new Point(),main);
    Check(host.ActualWidth<=main.ActualWidth&&point.X>=0&&point.X+host.ActualWidth<=main.ActualWidth&&point.Y>=0&&point.Y+host.ActualHeight<=main.ActualHeight,"Toast stays in main area at "+width);
   }
   DiagnosticCounts(vm,runner,6,0,0,0,"6 Correctos");DiagnosticCounts(vm,runner,5,1,0,0,"5 Correctos · 1 Atención");DiagnosticCounts(vm,runner,3,2,1,0,"3 Correctos · 2 Atención · 1 Error");DiagnosticCounts(vm,runner,4,0,0,2,"4 Correctos · 2 No comprobados");DiagnosticCounts(vm,runner,1,1,1,1,"1 Correcto · 1 Atención · 1 Error · 1 No comprobado");Layout(root);
   foreach(var item in vm.DiagnosticToastCounts) {
    var text=Visual<TextBlock>(panel).Single(t=>t.Text==item.Text);string brush=item.StatusCode=="Healthy"?"ToastGoodBrush":item.StatusCode=="Attention"?"ToastWarningBrush":item.StatusCode=="Error"?"ToastErrorBrush":"ToastMutedBrush";
    Check(Equals(text.Foreground,app.Resources[brush]),"Diagnostic count color "+item.StatusCode);
   }
   int before=vm.ActionHistory.Count;runner.Publish(Completed("repair.complete"));Check(!vm.ShowDiagnosticToastCounts&&vm.ActionHistory.Count==before+1,"Other operations use normal summary and one parent entry");
   // Presentation simulation of an in-flight Diagnostic; never invoke AnalyzeAsync or a worker.
   using(var lease=ops.Begin("Diagnóstico",false)) {
    typeof(DiagnosticViewModel).GetProperty("IsRunning").SetValue(diagnosis,true,null);
    typeof(DiagnosticViewModel).GetProperty("IsIntegrityRunning").SetValue(diagnosis,true,null);
    typeof(DiagnosticViewModel).GetProperty("LiveProgress").SetValue(diagnosis,new TaskProgress {Percentage=73,StepLabel="Paso 2 de 2",LastRelevantLine="SFC VerifyOnly",StdOut="Partial VerifyOnly stdout"},null);
    typeof(DiagnosticViewModel).GetProperty("Summary").SetValue(diagnosis,"Comprobando integridad de Windows...",null);Layout(root);
    Check(vm.TaskName=="Diagnóstico"&&vm.ShowTaskPanel&&vm.ToastPercentage==73&&!vm.CanCancelToast&&!vm.CanDismissTaskPanel,"Diagnostic toast uses live structured progress");
    vm.DetailsCommand.Execute(null);Check(dialogs.Output.StdOut.Contains("Partial VerifyOnly stdout"),"Diagnostic live output available in details");
    typeof(DiagnosticViewModel).GetProperty("IsRunning").SetValue(diagnosis,false,null);
   }
   vm.Navigate(NavigationSection.Repair);Layout(root);
   Check(scroll.Focusable&&scroll.FocusVisualStyle==null,"Repair viewport retains keyboard scrolling but no giant focus rectangle");
   foreach(var list in Visual<ItemsControl>(root).Where(c=>BindingPath(c,ItemsControl.ItemsSourceProperty)=="RealRepairTasks"||BindingPath(c,ItemsControl.ItemsSourceProperty)=="RepairTools"))
    Check(!list.Focusable,"Non-interactive Repair list does not receive keyboard focus");
   Check(Visual<ListBox>(root).Count()==2&&Visual<ListBox>(root).All(l=>l.Focusable&&l.FocusVisualStyle!=null),"Both sidebar groups retain keyboard accessibility");
   vm.Navigate(NavigationSection.Home);Layout(root);Check(scroll.FocusVisualStyle!=null,"Repair-only focus correction restores other page visuals");
   vm.Navigate(NavigationSection.Repair);Layout(root);
   Check(vm.PageDescription=="Comprueba y repara componentes de Windows mediante acciones explícitas; las herramientas administrativas solicitan confirmación y permisos de administrador antes de ejecutarse.","Single Repair description");
   var notice=(Border)Visual<TextBlock>(root).Single(t=>BindingPath(t,TextBlock.TextProperty)=="PageNotice").Parent;
   Check(!vm.HasPageNotice&&notice.Visibility==Visibility.Collapsed&&main.RowDefinitions[1].ActualHeight==0,"Repair notice fully collapsed");
   Check(!Visual<TextBlock>(root).Any(t=>t.Text=="Control específico sobre las comprobaciones, reparaciones y mantenimiento."),"Redundant subtitle removed");
   var tools=vm.RealRepairTasks.Where(t=>t.Task.Id!="repair.complete").ToList();Check(tools.Count==6,"Six individual tools");
   var titles=Visual<TextBlock>(root).Where(t=>t.Name=="RepairToolTitle"&&Shown(t)).ToList();Check(titles.Count==6,"Six visible individual title rows");
   for(int i=0;i<6;i++) {
    Check(tools[i].DisplayTitle==expectedTitles[i]&&tools[i].DisplayTechnicalName==expectedTech[i]&&tools[i].DisplayDescription==expectedDescriptions[i],"Exact display mapping "+i);
    var title=titles.Single(t=>ReferenceEquals(t.DataContext,tools[i]));var runs=title.Inlines.OfType<Run>().ToList();
    Check(runs.Count==3&&runs[0].Text==expectedTitles[i]&&runs[2].Text==expectedTech[i]&&runs[0].FontWeight==FontWeights.SemiBold&&runs[2].FontWeight==FontWeights.Normal&&runs[2].FontSize==11,"Technical annotation inline and muted");
    Check(Equals(runs[2].Foreground,app.Resources["MutedBrush"]),"Technical annotation theme");
    var row=(StackPanel)title.Parent;Check(Visual<TextBlock>(row).Any(t=>t.Text==expectedDescriptions[i]&&Shown(t)),"Description below");
    Check(Visual<TextBlock>(row).Where(t=>BindingPath(t,TextBlock.TextProperty)=="ResultText").All(t=>!Shown(t)),"No empty session result text");
    var grid=(Grid)row.Parent;var button=Visual<Button>(grid).Single();Check(Grid.GetColumn(button)==1&&ReferenceEquals(button.Command,vm.RunRepairCommand)&&Equals(button.CommandParameter,tools[i].Task.Id),"Right button and unchanged action");
    Check(button.Focusable&&button.IsTabStop&&button.FocusVisualStyle!=null,"Repair button keeps keyboard tab and visible focus");
    // Normal width: enough vertical room for a single title/annotation line.
    Check(title.ActualHeight<28,"Annotation stays on same line at normal width "+expectedTech[i]);
   }
   Check(Visual<TextBlock>(root).Any(t=>Shown(t)&&t.Text=="Reparación completa"&&t.FontSize==22),"Complete repair remains prominent");
   var imgDir=Path.Combine(project,"bin","Debug","VisualChecks");Directory.CreateDirectory(imgDir);Save(root,Path.Combine(imgDir,"RepairToast-"+theme+".png"));
   foreach(double width in new[]{900.0,720.0}) {
    Layout(root,width,760);
    foreach(var title in titles) {var grid=(Grid)((StackPanel)title.Parent).Parent;var button=Visual<Button>(grid).Single();var titlePoint=title.TranslatePoint(new Point(),grid);var buttonPoint=button.TranslatePoint(new Point(),grid);
     Check(titlePoint.X+title.ActualWidth<=buttonPoint.X&&buttonPoint.X+button.ActualWidth<=grid.ActualWidth,"No tool overlap at "+width);}
   }
   window.Close();app.Resources.MergedDictionaries.Remove(colors);
  }
  return count+" comprobaciones de toast/Reparación correctas; ambos temas, estado e historial simulados, sin herramientas reales ni UAC.";
 }
}
"@
$provider=[Microsoft.CSharp.CSharpCodeProvider]::new()
$parameters=[CodeDom.Compiler.CompilerParameters]::new()
$parameters.GenerateInMemory=$true
foreach($reference in @($exe,'System.dll','System.Core.dll','System.Xml.dll','System.Xaml.dll','System.Runtime.Serialization.dll',[System.Windows.Application].Assembly.Location,[System.Windows.Media.Visual].Assembly.Location,[System.Windows.DependencyObject].Assembly.Location)) { [void]$parameters.ReferencedAssemblies.Add($reference) }
$compiled=$provider.CompileAssemblyFromSource($parameters,$source)
if($compiled.Errors.HasErrors) { throw ($compiled.Errors | Out-String) }
[ToastRepairPresentationChecks]::Run($project)
