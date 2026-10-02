$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $project 'bin\Debug\WinSereno.exe'
[void][Reflection.Assembly]::LoadFrom($exe)
foreach ($name in @('WindowsBase','PresentationCore','PresentationFramework','System.Xaml')) {
    [void][Reflection.Assembly]::LoadWithPartialName($name)
}
$source = @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using WinSereno.Models;
using WinSereno.Services;
using WinSereno.Views;
using WinSereno.ViewModels;
using WinSereno.Infrastructure;
public sealed class DiagnosticTestLogger : ISessionLogger {
 public void Write(string text) {}
 public void TaskStarted(MaintenanceTask task) {}
 public void TaskFinished(MaintenanceTask task,MaintenanceTaskResult result) {}
 public void Dispose() {}
}
public sealed class DiagnosticTestRunner : IMaintenanceTaskRunner {
 public bool IsActive {get {return false;}}
 public TaskProgress Current {get {return new TaskProgress();}}
 public event EventHandler<TaskProgress> ProgressChanged {add {} remove {}}
 public System.Threading.Tasks.Task<MaintenanceTaskResult> RunAsync(MaintenanceTask task) {throw new Exception("No real tasks allowed");}
 public bool RequestCancellation() {throw new Exception("No real tasks allowed");}
 public System.Threading.Tasks.Task WaitForIdleAsync() {return System.Threading.Tasks.Task.CompletedTask;}
}
public sealed class DiagnosticTestDialogs : IDialogService {
 public void ShowOutput(TaskProgress progress) {throw new Exception("No real actions allowed");}
 public void ShowMessage(string text) {throw new Exception("Unexpected dialog");}
 public void ShowDiagnosticDetails(DiagnosticResult result) {throw new Exception("No real actions allowed");}
 public bool ConfirmTask(MaintenanceTask task) {throw new Exception("No UAC allowed");}
 public bool ConfirmCancelAndClose() {return false;}
 public RestartAdapter SelectRestartAdapter(IReadOnlyList<RestartAdapter> list) {throw new Exception("No real network allowed");}
}
public static class DiagnosticPresentationChecks {
 static int count;
 static void Check(bool value,string name) { if(!value) throw new Exception(name); count++; }
 static IEnumerable<T> Children<T>(DependencyObject parent) where T:DependencyObject {
  for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++) {
   var child=VisualTreeHelper.GetChild(parent,i); if(child is T) yield return (T)child;
   foreach(var nested in Children<T>(child)) yield return nested;
  }
 }
 static bool Shown(DependencyObject item) {
  for(var current=item;current!=null;current=VisualTreeHelper.GetParent(current)) {
   var element=current as UIElement; if(element!=null&&element.Visibility!=Visibility.Visible) return false;
  } return true;
 }
 static Border Present(DiagnosticCard card,DiagnosticResult result) {
  card.DataContext=result;
  card.Measure(new Size(850,double.PositiveInfinity));
  card.Arrange(new Rect(0,0,850,card.DesiredSize.Height));card.UpdateLayout();
  return (Border)card.Content;
 }
 static void RealResult(DiagnosticCard card,DiagnosticResult result) {
  var border=Present(card,result);
  Check((string)border.Tag=="False","Resultado real no se compacta: "+result.Summary);
  Check(border.Padding==new Thickness(12),"Padding compacto del resultado");
  var texts=Children<TextBlock>(card).ToList();
  Check(texts.Any(t=>t.Text==result.Name&&Shown(t)),"Nombre visible");
  Check(texts.Any(t=>t.Text==result.CardStatusLabel&&Shown(t)),"Estado visible en mayúsculas");
  Check(texts.Any(t=>t.Text==result.Summary&&Shown(t)),"Resumen completo visible");
  Check(typeof(DiagnosticResult).GetProperty("Recommendation")!=null,"Recommendation permanece en el modelo");
  Check(!texts.Any(t=>Shown(t)&&t.Text.StartsWith("Recomendación:")),"Recomendación ausente de la tarjeta resumida");
  var fullDetails=new DiagnosticDetailsViewModel(result).Text;
  Check(fullDetails.Contains("Recomendación: "+result.Recommendation)==!string.IsNullOrWhiteSpace(result.Recommendation),"Recomendación conservada únicamente en detalles cuando existe");
  var buttons=Children<Button>(card).ToList();
  var details=buttons.Single(b=>(string)b.Content=="Ver detalles");
  Check(Shown(details)==result.HasDetails,"Detalles conservados");
  Check(object.ReferenceEquals(details.CommandParameter,result),"Parámetro de detalles original");
  var navigation=buttons.Single(b=>b!=details);
  Check(Shown(navigation)==result.HasNavigation,"Navegación conservada");
  Check(object.Equals(navigation.CommandParameter,result.NavigationTarget),"Destino original");
  var header=(Grid)card.FindName("ResultHeader");var actions=(WrapPanel)card.FindName("HeaderActions");
  Check(actions.Parent==header&&Grid.GetColumn(actions)==1,"Acciones en columna derecha del encabezado");
  Check(buttons.All(b=>ReferenceEquals(b.Parent,actions)&&b.Padding==new Thickness(8,4,8,4)&&b.FontSize==12),"Botones compactos dentro del encabezado");
  Check(ReferenceEquals(actions.Children[0],navigation)&&ReferenceEquals(actions.Children[1],details),"Orden: navegación, detalles");
  var statusText=texts.Single(t=>t.Text==result.CardStatusLabel);
  Check(ReferenceEquals(header.Children[header.Children.Count-1],statusText)&&Grid.GetColumn(statusText)==2,"Estado al final, separado de botones");
  string brush=result.Status==DiagnosticStatus.Healthy?"GoodBrush":result.Status==DiagnosticStatus.Attention?"WarningBrush":result.Status==DiagnosticStatus.Error?"ErrorBrush":"MutedBrush";
  Check(Equals(statusText.Foreground,Application.Current.Resources[brush]),"Color funcional del estado conservado");
  Check(statusText.TranslatePoint(new Point(),header).X>=header.ActualWidth/2,"Estado situado a la derecha");
  foreach(double width in new[]{850.0,400.0,320.0}) {
   card.Measure(new Size(width,double.PositiveInfinity));card.Arrange(new Rect(0,0,width,card.DesiredSize.Height));card.UpdateLayout();
   var title=Children<TextBlock>(card).Single(t=>t.Text==result.Name);
   var titleRect=new Rect(title.TranslatePoint(new Point(),header),new Size(title.ActualWidth,title.ActualHeight));
   double statusStart=statusText.TranslatePoint(new Point(),header).X;
   Check(Math.Abs(statusText.TranslatePoint(new Point(statusText.ActualWidth,0),header).X-header.ActualWidth)<0.1,"Estado alineado al borde derecho: "+width);
   foreach(var button in buttons.Where(Shown)) {
    var buttonRect=new Rect(button.TranslatePoint(new Point(),header),new Size(button.ActualWidth,button.ActualHeight));
    var overlap=Rect.Intersect(titleRect,buttonRect);
    Check(overlap.IsEmpty||overlap.Width<0.1||overlap.Height<0.1,"Título y botones sin solapamiento: "+width);
    Check(buttonRect.Right<=statusStart+0.1,"Botones antes del estado: "+width);
   }
   if(Shown(navigation)&&Shown(details)) {
    var first=navigation.TranslatePoint(new Point(),header);var second=details.TranslatePoint(new Point(),header);
    Check(first.Y<second.Y||first.Y==second.Y&&first.X<second.X,"Orden visual navegación, detalles, estado: "+width);
   }
   Check(buttons.Where(Shown).All(b=>b.TranslatePoint(new Point(b.ActualWidth,0),card).X<=width+0.1),"Acciones dentro del ancho disponible: "+width);
  }
 }
 static void Layout(FrameworkElement content,double width=1150,double height=760) {
  content.Measure(new Size(width,height));content.Arrange(new Rect(0,0,width,height));content.UpdateLayout();
 }
 static string BindingPath(DependencyObject obj,DependencyProperty property) {
  var b=BindingOperations.GetBinding(obj,property);return b==null?null:b.Path.Path;
 }
 static void Page(string project,string theme,Application app) {
  var logger=new DiagnosticTestLogger();var dialogs=new DiagnosticTestDialogs();var ops=new OperationCoordinator();var integrity=new IntegritySessionState();
  var home=new HomeViewModel(null,logger);
  var diagnosis=new DiagnosticViewModel(null,ops,logger,dialogs,integrity);
  var vm=new MainViewModel(new PortableStorage(project),new AppSettings(),new ThemeService(),dialogs,new DiagnosticTestRunner(),home,diagnosis,ops,integrity,logger);
  vm.Navigate(NavigationSection.Diagnosis);
  Check(vm.PageDescription=="Analiza el estado general del PC sin realizar reparaciones. La integridad de Windows requiere permisos de administrador.","Una sola frase superior: "+theme);
  Check(!vm.HasPageNotice&&vm.PageNotice=="","Notice de Diagnóstico vacío: "+theme);
  Check(diagnosis.Results.Count==6&&diagnosis.NotCheckedCount==6&&diagnosis.HealthyCount==0&&diagnosis.AttentionCount==0&&diagnosis.ErrorCount==0,"Contadores iniciales dinámicos: "+theme);
  Check(diagnosis.Results.Single(r=>r.Id=="integrity").Status==DiagnosticStatus.NotChecked&&integrity.Read().Status==DiagnosticStatus.NotChecked,"Integridad sin resultado administrativo permanece No comprobado");
  var window=new MainWindow(vm,dialogs);var content=(FrameworkElement)window.Content;Layout(content);
  var notice=(Border)Children<TextBlock>(content).Single(t=>BindingPath(t,TextBlock.TextProperty)=="PageNotice").Parent;
  Check(notice.Visibility==Visibility.Collapsed&&notice.DesiredSize.Height==0,"Notice colapsado sin tamaño");
  Check(((Grid)notice.Parent).RowDefinitions[1].ActualHeight==0,"Sin margen residual de notice");
  var summary=(Border)window.FindName("DiagnosticSummaryCard");var controls=(WrapPanel)window.FindName("DiagnosticControls");
  Check(summary.Padding==new Thickness(12)&&summary.ActualHeight<=90,"Tarjeta superior compacta: "+summary.ActualHeight);
  var summaryText=Children<TextBlock>(summary).Single(t=>BindingPath(t,TextBlock.TextProperty)=="Diagnosis.Summary");
  Check(controls.TranslatePoint(new Point(),summary).X<summaryText.TranslatePoint(new Point(),summary).X,"Botones a la izquierda, resumen a la derecha");
  Check(summaryText.Text=="Aún no se ha analizado este PC en esta sesión.","Orientación inicial única conservada");
  var analyze=Children<Button>(controls).Single(b=>ReferenceEquals(b.Command,diagnosis.AnalyzeCommand));
  var cancel=Children<Button>(controls).Single(b=>ReferenceEquals(b.Command,diagnosis.CancelCommand));
  Check((string)analyze.Content=="Analizar este PC"&&analyze.IsEnabled&&!Shown(cancel),"Analizar y Cancelar conservan estado inicial");
  foreach(var pair in new[]{new[]{"Diagnosis.HealthyCount","GoodBrush"},new[]{"Diagnosis.AttentionCount","WarningBrush"},new[]{"Diagnosis.ErrorCount","ErrorBrush"},new[]{"Diagnosis.NotCheckedCount","MutedBrush"}}) {
   var text=Children<TextBlock>(summary).Single(t=>BindingPath(t,TextBlock.TextProperty)==pair[0]);
   Check(Equals(text.Foreground,app.Resources[pair[1]]),"Color de contador conservado: "+pair[0]+" / "+theme);
  }
  Check(Equals(summary.Background,app.Resources["SurfaceBrush"]),"Superficie temática de tarjeta superior");
  var resultHosts=Children<ContentControl>(content).Where(h=>h.DataContext is DiagnosticResult&&h.Content==null).ToList();
  Check(resultHosts.Count==6,"Seis fichas generadas en Diagnóstico");
  foreach(var host in resultHosts) host.Content=new DiagnosticCard();
  Layout(content);
  var integrityCard=Children<DiagnosticCard>(content).Single(c=>((DiagnosticResult)c.DataContext).Id=="integrity");
  var repair=Children<Button>(integrityCard).Single(b=>BindingPath(b,Button.ContentProperty)=="NavigationLabel");
  Check(!Shown(repair),"Integridad inicial no ofrece navegación innecesaria");
  var integrityHost=resultHosts.Single(h=>((DiagnosticResult)h.DataContext).Id=="integrity");
  integrityHost.DataContext=new DiagnosticResult {Id="integrity",Name="Integridad de Windows",Status=DiagnosticStatus.Attention,Summary="Problemas detectados sin reparación",DetailedDescription="Almacén de componentes y archivos protegidos",NavigationTarget=NavigationSection.Repair,NavigationLabel="Ir a Reparación"};Layout(content);
  Check(Shown(repair)&&ReferenceEquals(repair.Command,vm.NavigateCommand)&&object.Equals(repair.CommandParameter,NavigationSection.Repair),"Navegación contextual conservada cuando hay problemas");
  var details=Children<Button>(integrityCard).Single(b=>Convert.ToString(b.Content)=="Ver detalles");
  Check(Shown(details)&&ReferenceEquals(details.Command,diagnosis.DetailsCommand)&&ReferenceEquals(details.CommandParameter,integrityHost.DataContext),"Detalles con comando y parámetro originales");
  // Simulated lease only; never invoke a real diagnostic or worker.
  using(var simulatedOperation=ops.Begin("Diagnóstico",true)) {
  typeof(DiagnosticViewModel).GetProperty("IsRunning").SetValue(diagnosis,true,null);
  typeof(DiagnosticViewModel).GetProperty("Summary").SetValue(diagnosis,"Analizando... · 00:07",null);
  Layout(content);
  Check(Shown(cancel)&&summaryText.Text=="Analizando... · 00:07"&&!diagnosis.AnalyzeCommand.CanExecute(null)&&diagnosis.CancelCommand.CanExecute(null),"Presentación durante ejecución conservada");
  typeof(DiagnosticViewModel).GetProperty("IsIntegrityRunning").SetValue(diagnosis,true,null);
  Check(!diagnosis.CancelCommand.CanExecute(null),"Cancelación deshabilitada durante integridad elevada");
  typeof(DiagnosticViewModel).GetProperty("IsIntegrityRunning").SetValue(diagnosis,false,null);
  typeof(DiagnosticViewModel).GetProperty("IsRunning").SetValue(diagnosis,false,null);
  }
  typeof(DiagnosticViewModel).GetField("hasRun",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(diagnosis,true);
  typeof(ObservableObject).GetMethod("Raise",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(diagnosis,new object[]{"AnalyzeLabel"});
  typeof(DiagnosticViewModel).GetProperty("Summary").SetValue(diagnosis,"Diagnóstico finalizado en 1,2 s",null);
  diagnosis.Results.Clear();foreach(var pending in DiagnosticService.CreatePendingResults()) {pending.Status=pending.Id=="integrity"?DiagnosticStatus.NotChecked:DiagnosticStatus.Healthy;diagnosis.Results.Add(pending);}
  // Existing count bindings receive the same notification normally emitted by ApplyResult.
  typeof(DiagnosticViewModel).GetMethod("CountsChanged",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(diagnosis,null);
  Layout(content);
  Check((string)analyze.Content=="Analizar de nuevo"&&!Shown(cancel)&&summaryText.Text=="Diagnóstico finalizado en 1,2 s","Resultado final conserva botón y resumen");
  Check(Children<TextBlock>(summary).Any(t=>t.Text=="5 Correctos")&&Children<TextBlock>(summary).Any(t=>t.Text=="1 No comprobado"),"Contadores finales conservados");
  Layout(content,800,650);
  Check(summary.ActualHeight<180,"Cabecera se adapta a menor ancho: "+summary.ActualHeight);
  Check(((ScrollViewer)window.FindName("PageScroll")).VerticalScrollBarVisibility==ScrollBarVisibility.Auto,"Scroll conservado para ventanas pequeñas");
  window.Close();
  foreach(var item in vm.Navigation.Where(n=>n.Section!=NavigationSection.Home&&n.Section!=NavigationSection.Diagnosis&&n.Section!=NavigationSection.Repair&&n.Section!=NavigationSection.Network)) {
   // Bypass navigation callback so Network performs no real connectivity checks in these tests.
   typeof(MainViewModel).GetField("selectedNavigation",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(vm,item);
   Check(vm.HasPageNotice&&!string.IsNullOrWhiteSpace(vm.PageNotice),"Resto de notices intacto: "+item.Label);
  }
  Check(!ops.IsActive,"Pruebas de presentación sin operaciones ni UAC");
 }
 public static string Run(string project) {
  var app=new Application();
  Check(!File.ReadAllText(Path.Combine(project,"Views","DiagnosticCard.xaml")).Contains("Binding Recommendation"),"La tarjeta no tiene binding de recomendación");
  Check(File.ReadAllText(Path.Combine(project,"ViewModels","DiagnosticViewModel.cs")).Contains("text.AppendLine(result.Recommendation)"),"Resultado completo de Diagnóstico conserva recomendación para Actividad");
  app.Resources.MergedDictionaries.Add(new ResourceDictionary {Source=new Uri("/WinSereno;component/Themes/Styles.xaml",UriKind.Relative)});
  foreach(string theme in new[]{"Dark","Light"}) {
   var colors=new ResourceDictionary {Source=new Uri("/WinSereno;component/Themes/"+theme+".xaml",UriKind.Relative)};
   app.Resources.MergedDictionaries.Add(colors);
   var card=new DiagnosticCard();double total=0;
   var initial=DiagnosticService.CreatePendingResults();Check(initial.Count==6,"Seis comprobaciones iniciales");
   Check(initial.Select(r=>r.Id).SequenceEqual(new[]{"space","storage-health","network","services","events","integrity"}),"Solo las seis comprobaciones previstas");
   Check(!initial.Any(r=>r.Id=="power"||r.Name=="Plan de energía"),"Plan de energía eliminado");
   var checks=typeof(DiagnosticService).Assembly.GetType("WinSereno.Services.BasicDiagnosticChecks");
   Check(checks.GetMethod("Power")==null&&typeof(DiagnosticService).Assembly.GetType("WinSereno.Services.NativePowerInformation")==null,"Código de energía sin consumidores eliminado");
   foreach(var result in initial) {
    var border=Present(card,result);total+=card.DesiredSize.Height;
    Check((string)border.Tag=="False","Explicación inicial visible en el mismo formato compacto");
    Check(border.Padding==new Thickness(12),"Padding inicial compacto");
    var texts=Children<TextBlock>(card).ToList();
    Check(texts.Any(t=>t.Text==result.Name&&Shown(t)),"Nombre inicial visible");
    Check(texts.Any(t=>t.Text=="NO COMPROBADO"&&Shown(t)),"Estado inicial visible en mayúsculas");
    var header=(Grid)card.FindName("ResultHeader");var state=(TextBlock)card.FindName("ResultStatus");
    Check(Math.Abs(state.TranslatePoint(new Point(state.ActualWidth,0),header).X-header.ActualWidth)<0.1,"Estado inicial sin botones alineado a la derecha");
    Check(texts.Any(t=>t.Text==result.Summary&&Shown(t)),"Explicación inicial visible");
    Check(!texts.Any(t=>t.Text=="Recomendación: "+result.Recommendation&&Shown(t)),"Recomendación inicial oculta");
    var actions=Children<WrapPanel>(card).Single();
    Check(actions.Parent==card.FindName("ResultHeader"),"Acciones y estado en el encabezado");
    Check(Children<Button>(actions).Count(Shown)==(result.HasNavigation?1:0),"Solo acciones iniciales disponibles; integridad conserva navegación");
    Check(!result.HasNavigation&&string.IsNullOrEmpty(result.Recommendation),"Sin navegación ni recomendaciones repetidas antes del análisis");
   }
   Check(total<520,"Las seis tarjetas iniciales ocupan menos de 520 px: "+total);
   foreach(var status in new[]{DiagnosticStatus.Healthy,DiagnosticStatus.Attention,DiagnosticStatus.Error,DiagnosticStatus.NotChecked}) {
    string expected=status==DiagnosticStatus.Healthy?"CORRECTO":status==DiagnosticStatus.Attention?"ATENCIÓN":status==DiagnosticStatus.Error?"ERROR":"NO COMPROBADO";
    Check(new DiagnosticResult {Status=status}.CardStatusLabel==expected,"Texto exacto en mayúsculas: "+expected);
    RealResult(card,new DiagnosticResult {Id="events",Name="Eventos de Windows",Status=status,Summary="Resultado real "+status,
     Recommendation="Recomendación real",DetailedDescription="Descripción original",TechnicalDetails="Información técnica",
     NavigationTarget=NavigationSection.Repair,NavigationLabel="Ir a Reparación",Events=new List<DiagnosticEvent> {new DiagnosticEvent {EventId=41,Interpretation="Evento conocido"}}});
   }
   // Unknown/failed APIs are genuine NotChecked results, not the initial presentation.
   RealResult(card,new DiagnosticResult {Name="No disponible",Status=DiagnosticStatus.NotChecked,Summary="No se pudo obtener información fiable para esta comprobación.",Recommendation="El resto de comprobaciones puede continuar."});
   RealResult(card,new IntegritySessionState().Read());
   RealResult(card,new DiagnosticResult {Name="Sin recomendación",Status=DiagnosticStatus.Healthy,Summary="Resultado sin acción adicional",Recommendation=""});
   RealResult(card,new DiagnosticResult {Name="Solo detalles",Status=DiagnosticStatus.Healthy,Summary="Resultado correcto",DetailedDescription="Información disponible"});
   RealResult(card,new DiagnosticResult {Name="Solo navegación",Status=DiagnosticStatus.Attention,Summary="Revisar resultado",NavigationTarget=NavigationSection.Repair,NavigationLabel="Ir a Reparación"});
   // Matching initial text must still retain real technical details or a different recommendation.
   var withDetails=DiagnosticService.CreatePendingResults()[0];withDetails.TechnicalDetails="Dato real";RealResult(card,withDetails);
   var changed=DiagnosticService.CreatePendingResults()[0];changed.Recommendation="Verificar acceso";RealResult(card,changed);
   var admin=new IntegritySessionState();
   foreach(var finding in new[]{FindingStatus.Healthy,FindingStatus.Repaired,FindingStatus.RepairRequired,FindingStatus.Unrepairable,FindingStatus.Unknown}) {
    var start=new DateTimeOffset(2026,10,1,10,0,0,TimeSpan.Zero);
    var taskResult=new MaintenanceTaskResult {StartedAt=start,FinishedAt=start.AddMinutes(1),ExecutionStatus=ExecutionStatus.Success,
     FindingStatus=finding,UserSummary="Administrativo "+finding,StdOut="Salida DISM",StdErr="Salida stderr"};
    typeof(MaintenanceTaskResult).GetProperty("CommandStarted").SetValue(taskResult,true,null);
    admin.Update(ElevatedTaskCatalog.CheckHealthId,taskResult);RealResult(card,admin.Read());
   }
   // Replacing a real result with a pending item restores the compact presentation.
   var pending=DiagnosticService.CreatePendingResults()[0];Present(card,pending);
   Check(Children<TextBlock>(card).Any(t=>t.Text==pending.Summary&&Shown(t)),"Binding restaura explicación al volver al estado inicial");
   Page(project,theme,app);
   app.Resources.MergedDictionaries.Remove(colors);
  }
  return count+" comprobaciones de presentación correctas; temas claro/oscuro, sin análisis ni mantenimiento/UAC.";
 }
}
'@
$provider = [Microsoft.CSharp.CSharpCodeProvider]::new()
$parameters = [CodeDom.Compiler.CompilerParameters]::new()
$parameters.GenerateInMemory = $true
foreach ($reference in @($exe,'System.dll','System.Core.dll','System.Xml.dll','System.Runtime.Serialization.dll',
    [System.Windows.Application].Assembly.Location,[System.Windows.Media.Visual].Assembly.Location,
    [System.Windows.DependencyObject].Assembly.Location,[System.Xaml.XamlReader].Assembly.Location)) {
    [void]$parameters.ReferencedAssemblies.Add($reference)
}
$compiled = $provider.CompileAssemblyFromSource($parameters,$source)
if ($compiled.Errors.HasErrors) { throw ($compiled.Errors | Out-String) }
[DiagnosticPresentationChecks]::Run($project)
