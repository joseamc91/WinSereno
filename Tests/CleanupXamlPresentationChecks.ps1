$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
# Source-XAML presentation tests with inert fixtures. No WinSereno assembly or execution services are loaded.
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Xaml,UIAutomationTypes,UIAutomationProvider
$source=@'
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
public sealed class CleanupUiCommand:ICommand{
 public bool Enabled;public bool CanExecute(object p){return Enabled;}public void Execute(object p){throw new Exception("No execution permitted in presentation fixture");}public event EventHandler CanExecuteChanged{add{}remove{}}
}
public sealed class CleanupUiRow:INotifyPropertyChanged{
 public string Name{get;set;}public string Summary{get;set;}public string Status{get;set;}public string DisplayPath{get;set;}
 public string ElevatedAnalysisText{get;set;}public bool HasElevatedAnalysisTime{get;set;}public bool HasAmounts{get;set;}
 public bool IsUserTemporary{get;set;}public bool IsWindowsTemporary{get;set;}public bool IsThumbnailCache{get;set;}public bool IsRecycleBin{get;set;}
 public object Result{get;set;}private bool selected;public bool IsSelected{get{return selected;}set{selected=value;if(PropertyChanged!=null)PropertyChanged(this,new PropertyChangedEventArgs("IsSelected"));}}
 public event PropertyChangedEventHandler PropertyChanged;
}
public sealed class CleanupUiState:INotifyPropertyChanged{
 public List<CleanupUiRow> Categories{get;set;}public ICommand AnalyzeCommand{get;set;}public string AnalyzeLabel{get;set;}public string SelectionSummary{get;set;}public string Summary{get;set;}
 private bool canChange=true;public bool CanChangeSelection{get{return canChange;}set{canChange=value;if(PropertyChanged!=null)PropertyChanged(this,new PropertyChangedEventArgs("CanChangeSelection"));}}
 public event PropertyChangedEventHandler PropertyChanged;
}
public sealed class CleanupUiRoot{
 public string CurrentSectionCode{get{return "Cleanup";}}public string PageTitle{get{return "Limpieza";}}public string PageDescription{get;set;}
 public string PageNotice{get{return "";}}public bool HasPageNotice{get{return false;}}public string ProductVersion{get;set;}
 public bool ShowTaskPanel{get{return false;}}public CleanupUiState Cleanup{get;set;}public object[] Navigation{get;set;}
 public object[] MainNavigation{get;set;}public object[] SettingsNavigation{get;set;}public object SelectedNavigation{get;set;}
 public object SelectedMainNavigation{get{return MainNavigation.Contains(SelectedNavigation)?SelectedNavigation:null;}set{if(value!=null&&MainNavigation.Contains(value))SelectedNavigation=value;}}
 public object SelectedSettingsNavigation{get{return SettingsNavigation.Contains(SelectedNavigation)?SelectedNavigation:null;}set{if(value!=null&&SettingsNavigation.Contains(value))SelectedNavigation=value;}}
 public ICommand CleanUserTempCommand{get;set;}public ICommand CleanWindowsTempCommand{get;set;}public ICommand CleanThumbnailsCommand{get;set;}
 public ICommand EmptyRecycleBinCommand{get;set;}public ICommand CleanSelectedCommand{get;set;}
}
public static class CleanupXamlPresentationChecks{
 static int count;static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;}
 static IEnumerable<T> Visual<T>(DependencyObject root)where T:DependencyObject{if(root is T)yield return (T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in Visual<T>(VisualTreeHelper.GetChild(root,i)))yield return child;}
 static bool Shown(DependencyObject root){for(var p=root;p!=null;p=VisualTreeHelper.GetParent(p))if(p is UIElement&&((UIElement)p).Visibility==Visibility.Collapsed)return false;return true;}
 static void Layout(FrameworkElement root,double width){root.Measure(new Size(width,800));root.Arrange(new Rect(0,0,width,800));root.UpdateLayout();}
 static void Save(FrameworkElement root,string path){var bitmap=new RenderTargetBitmap((int)root.ActualWidth,(int)root.ActualHeight,96,96,PixelFormats.Pbgra32);var bg=new DrawingVisual();using(var drawing=bg.RenderOpen())drawing.DrawRectangle((Brush)Application.Current.Resources["BackgroundBrush"],null,new Rect(0,0,root.ActualWidth,root.ActualHeight));bitmap.Render(bg);bitmap.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(path))encoder.Save(stream);}
 static CleanupUiRoot Fixture(string project,string state){
  bool ready=state!="Before",partial=state=="Partial";var rows=new List<CleanupUiRow>();string[] names={"Temporales del usuario","Temporales de Windows","Caché de miniaturas del usuario","Papelera de reciclaje"};
  string[] summaries={"158,9 MB encontrados · 30,7 MB potencialmente limpiables","161 MB encontrados · 150 MB potencialmente limpiables","35,1 MB encontrados · 5 MB potencialmente limpiables","7,4 GB en la Papelera"};
  for(int i=0;i<4;i++)rows.Add(new CleanupUiRow{Name=names[i],IsSelected=i!=3,IsUserTemporary=i==0,IsWindowsTemporary=i==1,IsThumbnailCache=i==2,IsRecycleBin=i==3,
   Summary=!ready?"Aún no analizado":partial&&i==1?"No comprobado":summaries[i],Status=!ready?"Aún no analizado":partial&&i==1?"No comprobado":"Analizado",
   DisplayPath=i==0?@"C:\Users\usuario\AppData\Local\Temp":i==1?@"C:\Windows\Temp":i==2?@"C:\Users\usuario\AppData\Local\Microsoft\Windows\Explorer":"Papelera del usuario actual.",
   HasAmounts=ready&&!(partial&&i==1),HasElevatedAnalysisTime=ready&&!(partial&&i==1)&&i==1,ElevatedAnalysisText="Último análisis con permisos: 02/10/2026 12:34",
   Result=new{WasAnalyzed=ready}});
  string main=File.ReadAllText(Path.Combine(project,"ViewModels","MainViewModel.cs"));var description=Regex.Match(main,"case NavigationSection.Cleanup: return \"([^\"]+)\"").Groups[1].Value;
  string info=File.ReadAllText(Path.Combine(project,"Properties","AssemblyInfo.cs"));var version=Regex.Match(info,"AssemblyInformationalVersion\\(\"([^\"]+)\"\\)").Groups[1].Value;
  var navigation=new[]{new{Label="Inicio",SectionCode="Home"},new{Label="Diagnóstico",SectionCode="Diagnosis"},new{Label="Reparación",SectionCode="Repair"},new{Label="Red",SectionCode="Network"},new{Label="Limpieza",SectionCode="Cleanup"},new{Label="Actividad",SectionCode="Activity"},new{Label="Ajustes",SectionCode="Settings"}};
  return new CleanupUiRoot{PageDescription=description,ProductVersion="v"+version,Navigation=navigation.Cast<object>().ToArray(),MainNavigation=navigation.Take(6).Cast<object>().ToArray(),SettingsNavigation=navigation.Skip(6).Cast<object>().ToArray(),SelectedNavigation=navigation[4],
   Cleanup=new CleanupUiState{Categories=rows,AnalyzeLabel=ready?"Analizar de nuevo":"Analizar",AnalyzeCommand=new CleanupUiCommand{Enabled=true},SelectionSummary=!ready?"3 categorías marcadas":partial?"3 categorías marcadas · Estimación parcial: 35,7 MB":"3 categorías marcadas · Espacio recuperable estimado: 185,7 MB",Summary=!ready?"Aún no se ha realizado un análisis.":partial?"Análisis finalizado · Estimación parcial: 7,4 GB recuperables estimados":"Análisis finalizado · 7,6 GB recuperables estimados"},
   CleanUserTempCommand=new CleanupUiCommand{Enabled=ready},CleanWindowsTempCommand=new CleanupUiCommand{Enabled=ready&&!partial},CleanThumbnailsCommand=new CleanupUiCommand{Enabled=ready},EmptyRecycleBinCommand=new CleanupUiCommand{Enabled=ready},CleanSelectedCommand=new CleanupUiCommand{Enabled=ready&&!partial}};
 }
 public static string Run(string project){
  var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(File.ReadAllText(Path.Combine(project,"Themes","Styles.xaml"))));
  string xaml=File.ReadAllText(Path.Combine(project,"Views","MainWindow.xaml"));xaml=Regex.Replace(xaml," x:Class=\"[^\"]+\"","");xaml=Regex.Replace(xaml," (?:Loaded|SizeChanged|KeyDown)=\"[^\"]+\"","");
  string output=Path.Combine(project,"bin","Debug","VisualChecks");Directory.CreateDirectory(output);
  foreach(string theme in new[]{"Light","Dark"}){
   var colors=(ResourceDictionary)XamlReader.Parse(File.ReadAllText(Path.Combine(project,"Themes",theme+".xaml")).Replace("/WinSereno;component/Assets/Brand/",new Uri(Path.Combine(project,"Assets","Brand")+Path.DirectorySeparatorChar).AbsoluteUri));app.Resources.MergedDictionaries.Add(colors);
   foreach(string state in new[]{"Before","After","Partial"}){
    var vm=Fixture(project,state);var window=(Window)XamlReader.Parse(xaml);window.DataContext=vm;var root=(FrameworkElement)window.Content;var page=(FrameworkElement)window.FindName("CleanupPage");
    foreach(double width in new[]{1150.0,900.0}){
     Layout(root,width);var main=(Grid)window.FindName("MainArea");Check(main.RowDefinitions[1].ActualHeight==0,"Cleanup notice collapses fully");
     var boxes=Visual<CheckBox>(page).ToArray();Check(boxes.Length==4&&boxes.Take(3).All(b=>b.IsChecked==true)&&boxes[3].IsChecked==false,"Four visible checkboxes and defaults");
     Check(Visual<Button>(page).Single(b=>ReferenceEquals(b.Command,vm.Cleanup.AnalyzeCommand)).Style==window.FindResource("PrimaryButton"),"Analyze is primary action");
     var buttons=Visual<Button>(page).Where(b=>Shown(b)&&!ReferenceEquals(b.Command,vm.Cleanup.AnalyzeCommand)).ToArray();Check(buttons.Length==5,"Four individual actions plus marked-category cleanup");
     Check(Visual<Button>(page).Any(b=>Convert.ToString(b.Content)=="Limpiar categorías marcadas"),"Unambiguous batch button");
     Check(Visual<TextBlock>(page).Count(t=>Shown(t)&&t.Text.Contains("encontrados"))==(state=="Before"?0:state=="Partial"?2:3),"Amounts appear only for valid category results");
     foreach(var button in buttons)Check(button.IsEnabled==button.Command.CanExecute(null),"Validity gate reflected in button state");
     foreach(var box in boxes){
      var shell=(Border)box.Template.FindName("CheckShell",box);var mark=(System.Windows.Shapes.Path)box.Template.FindName("CheckMark",box);
      Check(shell.ActualWidth==20&&shell.ActualHeight==20&&box.ActualHeight>=30,"Large box and comfortable click area");Check(box.Focusable&&KeyboardNavigation.GetIsTabStop(box),"Keyboard focus/navigation retained");
      var checkedBefore=box.IsChecked;var peer=new CheckBoxAutomationPeer(box);((IToggleProvider)peer).Toggle();Check(box.IsChecked!=checkedBefore && box.IsChecked==vm.Cleanup.Categories[Array.IndexOf(boxes,box)].IsSelected,"Accessible toggle updates selection");
      box.IsChecked=!box.IsChecked;Layout(root,width);Check(mark.Visibility==(box.IsChecked==true?Visibility.Visible:Visibility.Collapsed),"Check mark visible with themed accent");
     }
     // Exercise the inherited native CheckBox click without any application command.
     var before=boxes[0].IsChecked;typeof(CheckBox).GetMethod("OnClick",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(boxes[0],new object[0]);Check(boxes[0].IsChecked!=before,"Click toggles selection");boxes[0].IsChecked=before;
     vm.Cleanup.CanChangeSelection=false;Layout(root,width);Check(boxes.All(b=>!b.IsEnabled),"Disabled selection state");
     Check(Visual<Grid>(boxes[0]).Any(g=>g.Name=="HitArea"&&g.Opacity==0.45),"Disabled appearance distinct");vm.Cleanup.CanChangeSelection=true;Layout(root,width);
     foreach(var text in Visual<TextBlock>(page).Where(t=>Shown(t))){var p=text.TransformToAncestor(page).Transform(new Point());Check(p.X>=-0.5&&p.X+text.ActualWidth<=page.ActualWidth+0.5,"No text overflow at "+width);Check(!text.Text.Contains("SHQuery")&&!text.Text.Contains("archivos accesibles")&&!text.Text.Contains("48 horas")&&!text.Text.Contains("Tamaño lógico"),"No technical counters/heuristics in normal view");}
     Check(!Visual<TextBlock>(root).Any(t=>t.Text=="Portable · Acciones explícitas"),"Old sidebar tagline removed");Check(Visual<TextBlock>(root).Any(t=>t.Text=="v0.1.0-beta.5"),"Public development version visible");
     var details=Visual<Expander>(page).ToArray();Check(details.All(e=>!e.IsExpanded),"Details remain collapsed by default");
     if(state!="Before")foreach(var detail in details){detail.IsExpanded=true;Layout(root,width);Check(!Visual<TextBlock>(detail).Any(t=>t.Text.Contains("SHQuery")||t.Text.Contains("archivos accesibles")||t.Text.Contains("Tamaño lógico")),"Expanded details stay simple");detail.IsExpanded=false;}
    }
    Layout(root,1150);Save(root,Path.Combine(output,"Cleanup-"+state+"-"+theme+".png"));window.Close();
   }
   app.Resources.MergedDictionaries.Remove(colors);
  }
  return count+" comprobaciones WPF de presentación Limpieza correctas; XAML fuente y datos simulados, sin cargar WinSereno ni ejecutar operaciones.";
 }
}
'@
$provider=[Microsoft.CSharp.CSharpCodeProvider]::new();$parameters=[CodeDom.Compiler.CompilerParameters]::new();$parameters.GenerateInMemory=$true
foreach($reference in @('System.dll','System.Core.dll','System.Xaml.dll',[System.Windows.Application].Assembly.Location,[System.Windows.Media.Visual].Assembly.Location,[System.Windows.DependencyObject].Assembly.Location,[System.Windows.Automation.Provider.IToggleProvider].Assembly.Location,[System.Windows.Automation.ToggleState].Assembly.Location)){[void]$parameters.ReferencedAssemblies.Add($reference)}
$compiled=$provider.CompileAssemblyFromSource($parameters,$source)
if($compiled.Errors.HasErrors){throw ($compiled.Errors | Out-String)}
[CleanupXamlPresentationChecks]::Run($project)
