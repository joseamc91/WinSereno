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
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using WinSereno.Models;
using WinSereno.Services;
using WinSereno.Views;
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
  Check(border.Padding==new Thickness(18),"Padding original del resultado");
  var texts=Children<TextBlock>(card).ToList();
  Check(texts.Any(t=>t.Text==result.Name&&Shown(t)),"Nombre visible");
  Check(texts.Any(t=>t.Text==result.StatusLabel&&Shown(t)),"Estado visible");
  Check(texts.Any(t=>t.Text==result.Summary&&Shown(t)),"Resumen completo visible");
  Check(texts.Any(t=>t.Text=="Recomendación: "+result.Recommendation&&Shown(t)),"Recomendación completa visible");
  var buttons=Children<Button>(card).ToList();
  var details=buttons.Single(b=>(string)b.Content=="Ver detalles");
  Check(Shown(details)==result.HasDetails,"Detalles conservados");
  Check(object.ReferenceEquals(details.CommandParameter,result),"Parámetro de detalles original");
  var navigation=buttons.Single(b=>b!=details);
  Check(Shown(navigation)==result.HasNavigation,"Navegación conservada");
  Check(object.Equals(navigation.CommandParameter,result.NavigationTarget),"Destino original");
 }
 public static string Run() {
  var app=new Application();
  app.Resources.MergedDictionaries.Add(new ResourceDictionary {Source=new Uri("/WinSereno;component/Themes/Styles.xaml",UriKind.Relative)});
  foreach(string theme in new[]{"Dark","Light"}) {
   var colors=new ResourceDictionary {Source=new Uri("/WinSereno;component/Themes/"+theme+".xaml",UriKind.Relative)};
   app.Resources.MergedDictionaries.Add(colors);
   var card=new DiagnosticCard();double total=0;
   var initial=DiagnosticService.CreatePendingResults();Check(initial.Count==8,"Ocho comprobaciones iniciales");
   foreach(var result in initial) {
    var border=Present(card,result);total+=card.DesiredSize.Height;
    Check((string)border.Tag=="True","Solo presentación inicial marcada");
    Check(border.Padding==new Thickness(12),"Padding inicial compacto");
    var texts=Children<TextBlock>(card).ToList();
    Check(texts.Any(t=>t.Text==result.Name&&Shown(t)),"Nombre inicial visible");
    Check(texts.Any(t=>t.Text=="No comprobado"&&Shown(t)),"Estado inicial visible");
    Check(!texts.Any(t=>t.Text==result.Summary&&Shown(t)),"Orientación repetida oculta");
    Check(!texts.Any(t=>t.Text=="Recomendación: "+result.Recommendation&&Shown(t)),"Recomendación inicial oculta");
    var actions=Children<WrapPanel>(card).Single();
    Check(Shown(actions)==result.HasNavigation,"Sin hueco de acciones; integridad conserva navegación");
    if(result.Id=="integrity") Check(Children<Button>(card).Any(b=>(string)b.Content=="Ir a Reparación"&&Shown(b)),"Integridad sigue accesible: "+string.Join(";",Children<Button>(card).Select(b=>Convert.ToString(b.Content)+":"+b.Visibility+":"+Shown(b))));
   }
   Check(total<520,"Las ocho tarjetas iniciales ocupan menos de 520 px: "+total);
   foreach(var status in new[]{DiagnosticStatus.Healthy,DiagnosticStatus.Attention,DiagnosticStatus.Error,DiagnosticStatus.NotChecked}) {
    RealResult(card,new DiagnosticResult {Id="events",Name="Eventos de Windows",Status=status,Summary="Resultado real "+status,
     Recommendation="Recomendación real",DetailedDescription="Descripción original",TechnicalDetails="Información técnica",
     NavigationTarget=NavigationSection.Repair,NavigationLabel="Ir a Reparación",Events=new List<DiagnosticEvent> {new DiagnosticEvent {EventId=41,Interpretation="Evento conocido"}}});
   }
   // Unknown/failed APIs are genuine NotChecked results, not the initial presentation.
   RealResult(card,new DiagnosticResult {Name="No disponible",Status=DiagnosticStatus.NotChecked,Summary="No se pudo obtener información fiable para esta comprobación.",Recommendation="El resto de comprobaciones puede continuar."});
   RealResult(card,new IntegritySessionState().Read());
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
   Check(!Children<TextBlock>(card).Any(t=>t.Text==pending.Summary&&Shown(t)),"Binding reacciona a sustitución inicial");
   app.Resources.MergedDictionaries.Remove(colors);
  }
  return count+" comprobaciones de presentación correctas; temas claro/oscuro, sin análisis ni mantenimiento/UAC.";
 }
}
'@
$provider = [Microsoft.CSharp.CSharpCodeProvider]::new()
$parameters = [CodeDom.Compiler.CompilerParameters]::new()
$parameters.GenerateInMemory = $true
foreach ($reference in @($exe,'System.dll','System.Core.dll',
    [System.Windows.Application].Assembly.Location,[System.Windows.Media.Visual].Assembly.Location,
    [System.Windows.DependencyObject].Assembly.Location,[System.Xaml.XamlReader].Assembly.Location)) {
    [void]$parameters.ReferencedAssemblies.Add($reference)
}
$compiled = $provider.CompileAssemblyFromSource($parameters,$source)
if ($compiled.Errors.HasErrors) { throw ($compiled.Errors | Out-String) }
[DiagnosticPresentationChecks]::Run()
