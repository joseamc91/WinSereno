param([switch]$CompileOnly)
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
$exe=Join-Path $project 'bin\Debug\WinSereno.exe'
if(!$CompileOnly){[void][Reflection.Assembly]::LoadFrom($exe)}
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Xaml,UIAutomationProvider,UIAutomationTypes
$source=@'
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinSereno.Services;
using WinSereno.Models;
using WinSereno.ViewModels;
using WinSereno.Views;
using WinSereno.Infrastructure;
public sealed class Beta4Dialogs:IDialogService,IPreferencesDialogs {
 public bool Accept;public int Confirmations;public bool ConfirmResetPreferences(){Confirmations++;return Accept;}
 public bool ConfirmTask(MaintenanceTask t){throw new Exception("No maintenance");}public void ShowMessage(string s){throw new Exception(s);}
 public void ShowOutput(TaskProgress p){}public void ShowDiagnosticDetails(DiagnosticResult r){}public bool ConfirmCancelAndClose(){return false;}
 public RestartAdapter SelectRestartAdapter(IReadOnlyList<RestartAdapter> a){throw new Exception("No network");}
}
public sealed class Beta4Runner:IMaintenanceTaskRunner{
 public bool IsActive{get{return false;}}public TaskProgress Current{get{return new TaskProgress();}}public event EventHandler<TaskProgress> ProgressChanged{add{}remove{}}
 public Task<MaintenanceTaskResult> RunAsync(MaintenanceTask t){throw new Exception("No actual tasks");}public bool RequestCancellation(){return false;}public Task WaitForIdleAsync(){return Task.CompletedTask;}
}
public static class Beta4ExperienceChecks{
 static int count;static void Check(bool ok,string label){if(!ok)throw new Exception(label);count++;}
 static IEnumerable<T> Visual<T>(DependencyObject root) where T:DependencyObject{if(root is T)yield return (T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in Visual<T>(VisualTreeHelper.GetChild(root,i)))yield return child;}
 static bool Shown(FrameworkElement e){for(DependencyObject n=e;n!=null;n=VisualTreeHelper.GetParent(n)){var f=n as FrameworkElement;if(f!=null&&f.Visibility!=Visibility.Visible)return false;}return e.ActualWidth>0;}
 static void Layout(FrameworkElement root,double width){((Panel)root).Background=(Brush)Application.Current.Resources["BackgroundBrush"];root.Measure(new Size(width,800));root.Arrange(new Rect(0,0,width,800));root.UpdateLayout();Application.Current.Dispatcher.Invoke(new Action(()=>{}),DispatcherPriority.Background);}
 static void State(DependencyObject control,Type owner,string key,bool value){control.SetValue((DependencyPropertyKey)owner.GetField(key,BindingFlags.Static|BindingFlags.NonPublic).GetValue(null),value);}
 static void Save(FrameworkElement root,string path){var image=new RenderTargetBitmap((int)root.ActualWidth,(int)root.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var stream=File.Create(path))encoder.Save(stream);}
 static ThemeService Theme(Func<string> resolve){return (ThemeService)typeof(ThemeService).GetConstructor(BindingFlags.NonPublic|BindingFlags.Instance,null,new[]{typeof(Func<string>)},null).Invoke(new object[]{resolve});}
 static void Select(MainViewModel vm,NavigationSection section){vm.SelectedNavigation=vm.Navigation.Single(n=>n.Section==section);}
 static void ActivateItem(ListBox list,int index){
  var peer=new ListBoxItemAutomationPeer(list.Items[index],new ListBoxAutomationPeer(list));
  ((ISelectionItemProvider)peer.GetPattern(PatternInterface.SelectionItem)).Select();
 }
 static void KeyPress(ListBox list,Key key){
  var focused=Keyboard.FocusedElement as UIElement;Check(focused!=null&&focused.IsDescendantOf(list),"Keyboard event originates in the focused navigation item");
  focused.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(list),Environment.TickCount,key){RoutedEvent=Keyboard.KeyDownEvent});
 }
 static void CheckNavigation(MainWindow window,MainViewModel vm,NavigationSection section,double width){
  var root=(FrameworkElement)window.Content;Layout(root,width);
  var main=(ListBox)window.FindName("MainNavigationList");var settings=(ListBox)window.FindName("SettingsNavigationList");
  Check(vm.SelectedNavigation.Section==section&&vm.CurrentSection==section&&vm.CurrentSectionCode==section.ToString(),"Control interaction changes the real active section: expected="+section+", actual="+vm.CurrentSectionCode+", focus="+Keyboard.FocusedElement);
  Check(ReferenceEquals(main.SelectedItem,section==NavigationSection.Settings?null:vm.SelectedNavigation)&&ReferenceEquals(settings.SelectedItem,section==NavigationSection.Settings?vm.SelectedNavigation:null),"Only the active group has a selected item");
  var items=Visual<ListBoxItem>(main).Concat(Visual<ListBoxItem>(settings)).ToArray();
  Check(items.Count(i=>i.IsSelected)==1&&main.SelectedItems.Count+settings.SelectedItems.Count==1,"Exactly one global visual selection");
  foreach(var item in items){var icon=Visual<System.Windows.Shapes.Path>(item).Single();
   Check(Equals(icon.Stroke,Application.Current.Resources[item.IsSelected?"AccentBrush":item.IsMouseOver?"TextBrush":"MutedBrush"]),"Icon follows real selection after control interaction: label="+((NavigationItem)item.DataContext).Label+", selected="+item.IsSelected+", hover="+item.IsMouseOver+", stroke="+icon.Stroke);
   var shell=(Border)item.Template.FindName("ItemShell",item);
   Check(Equals(shell.Background,item.IsSelected?Application.Current.Resources["NavigationSelectedBrush"]:item.IsMouseOver?Application.Current.Resources["NavigationHoverBrush"]:Brushes.Transparent),"Focus never creates a second selected background");
  }
  var pages=(Grid)((ScrollViewer)window.FindName("PageScroll")).Content;
  var panels=pages.Children.OfType<StackPanel>().ToArray();
  Check(panels.Count(p=>p.Visibility==Visibility.Visible)==1,"Exactly one central page visible");
  foreach(var page in panels){bool active=page.Style.Triggers.OfType<DataTrigger>().Any(t=>Equals(t.Value,section.ToString()));Check(page.Visibility==(active?Visibility.Visible:Visibility.Collapsed),"Page visibility agrees with real navigation");}
  var settingsPage=(FrameworkElement)window.FindName("SettingsPage");Check(settingsPage.Visibility==(section==NavigationSection.Settings?Visibility.Visible:Visibility.Collapsed),"Settings page changes with sidebar interaction");
  foreach(string property in new[]{"PageTitle","PageDescription"}){var text=Visual<TextBlock>(root).Single(t=>{var binding=System.Windows.Data.BindingOperations.GetBinding(t,TextBlock.TextProperty);return binding!=null&&binding.Path.Path==property;});Check(text.Text==(property=="PageTitle"?vm.PageTitle:vm.PageDescription),"Header updated: "+property);}
 }
 public static string Run(string project){
  var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};Application.ResourceAssembly=typeof(WinSereno.App).Assembly;
  app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/WinSereno;component/Themes/Styles.xaml",UriKind.Relative)});
  var root=Path.Combine(project,"bin","Debug","Beta4Fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try{
   var storage=new PortableStorage(root);Directory.CreateDirectory(storage.LogsDirectory);var log=Path.Combine(storage.LogsDirectory,"retained.txt");File.WriteAllText(log,"retained log");
   foreach(var value in new[]{"Light","Dark","System"}){storage.SaveSettings(new AppSettings{Theme=value});Check(storage.LoadSettings().Theme==value,"Persist "+value);Check(!File.Exists(storage.ConfigPath+".tmp"),"No temporary config");}
   foreach(var json in new[]{"{}","{\"Theme\":\"invalid\"}","{\"Theme\":null}","null"}){File.WriteAllText(storage.ConfigPath,json);Check(storage.LoadSettings().Theme=="Light","Invalid theme safe fallback");}
   string system="Dark";var themes=Theme(()=>system);themes.Apply("System");Check(themes.EffectiveTheme=="Dark","System at startup uses Windows preference");system="Light";themes.Apply("System");Check(themes.EffectiveTheme=="Light","System resolves again on selection");
   Theme(()=>"invalid").Apply("System");Check(((SolidColorBrush)app.Resources["BackgroundBrush"]).Color==Color.FromRgb(243,245,248),"Unknown Windows preference Light fallback");var unavailable=Theme(()=>{throw new IOException("controlled");});unavailable.Apply("System");Check(unavailable.EffectiveTheme=="Light","Windows query failure fallback");
   var launches=new List<System.Diagnostics.ProcessStartInfo>();var shell=(ApplicationShellService)typeof(ApplicationShellService).GetConstructor(BindingFlags.NonPublic|BindingFlags.Instance,null,new[]{typeof(PortableStorage),typeof(Action<System.Diagnostics.ProcessStartInfo>)},null).Invoke(new object[]{storage,new Action<System.Diagnostics.ProcessStartInfo>(i=>launches.Add(i))});
   var dialogs=new Beta4Dialogs();var ops=new OperationCoordinator();var state=new IntegritySessionState();var home=new HomeViewModel(null,null);
   string[] values={"Windows 11","Intel Core i7-10700K","NVIDIA GeForce RTX 3070 Ti","32 GB","Ethernet","2 días"};string[] details={"25H2 · x64","8 núcleos · 16 hilos · 3.8 GHz\nIntel UHD Graphics 630","Controlador 32.0.16.1692","DDR4 · 2 × 16 GB · 2933 MT/s","1 Gbps · IPv4 192.168.0.200","Tiempo desde el último inicio"};for(int i=0;i<6;i++){home.Cards[i].Value=values[i];home.Cards[i].Description=details[i];}
   home.LocalDisks.Add(new DiskViewModel{Name="C:",CapacityText="476 GB libres de 931 GB",FreeText="51,1 % libre"});
   typeof(HomeViewModel).GetProperty("DisksMessage").SetValue(home,"",null);
   var vm=new MainViewModel(storage,new AppSettings(),themes,dialogs,new Beta4Runner(),home,new DiagnosticViewModel(null,ops,null,dialogs,state),ops,state,null,null,null,null,shell);
   Check(vm.MainNavigation.Select(n=>n.Label).SequenceEqual(new[]{"Inicio","Diagnóstico","Reparación","Red","Limpieza","Actividad"}),"Primary navigation order");Check(vm.Navigation.Count==7&&vm.Navigation.Select(n=>n.Section).Distinct().Count()==7&&vm.SettingsNavigation.Single().Label=="Ajustes","Exactly seven distinct items; Settings separate");
   vm.OpenLogsCommand.Execute(null);vm.OpenApplicationFolderCommand.Execute(null);vm.OpenGitHubCommand.Execute(null);vm.OpenReleasesCommand.Execute(null);
   Check(launches.Select(i=>i.FileName).SequenceEqual(new[]{storage.LogsDirectory,Path.GetFullPath(root),ApplicationShellService.GitHubUrl,ApplicationShellService.ReleasesUrl}),"Fixed folder/link targets");Check(launches.All(i=>i.UseShellExecute&&i.Verb=="open"&&string.IsNullOrEmpty(i.Arguments)),"Default shell without UAC or arbitrary arguments");
   vm.SelectedTheme="Sistema";Check(storage.LoadSettings().Theme=="System"&&vm.SelectedTheme=="Sistema","UI persists System");vm.SelectedTheme="Oscuro";Check(storage.LoadSettings().Theme=="Dark","UI Dark");vm.SelectedTheme="Claro";Check(storage.LoadSettings().Theme=="Light","UI Light");vm.SelectedTheme="Oscuro";
   string previous=File.ReadAllText(storage.ConfigPath);vm.ResetPreferencesCommand.Execute(null);Check(File.ReadAllText(storage.ConfigPath)==previous&&vm.SelectedTheme=="Oscuro","Reset cancellation no change");dialogs.Accept=true;vm.ResetPreferencesCommand.Execute(null);Check(storage.LoadSettings().Theme=="Light"&&vm.SelectedTheme=="Claro"&&themes.EffectiveTheme=="Light","Reset defaults persisted and applied without restart");Check(File.ReadAllText(log)=="retained log"&&Directory.GetFiles(storage.LogsDirectory).Length==1&&!ops.IsActive&&vm.ActionHistory.Count==0,"Reset leaves logs/system/Activity untouched");
   string output=Path.Combine(project,"bin","Debug","VisualChecks");Directory.CreateDirectory(output);
   foreach(string theme in new[]{"Light","Dark"}){
    vm.SelectedTheme=theme=="Dark"?"Oscuro":"Claro";themes.Apply(theme);
    foreach(var section in new[]{NavigationSection.Home,NavigationSection.Diagnosis,NavigationSection.Repair,NavigationSection.Cleanup,NavigationSection.Settings,NavigationSection.Activity}){
     Select(vm,section);var window=new MainWindow(vm,dialogs);var content=(FrameworkElement)window.Content;
     Layout(content,1150);
     if(section==NavigationSection.Diagnosis){var hosts=Visual<ContentControl>(content).Where(h=>h.DataContext is DiagnosticResult&&h.Content==null).ToArray();Check(hosts.Length==6,"Six simulated diagnostic cards");foreach(var host in hosts)host.Content=new DiagnosticCard();}
     foreach(double width in new[]{1150.0,900.0}){
      Layout(content,width);
      var primary=(ListBox)window.FindName("MainNavigationList");var settings=(ListBox)window.FindName("SettingsNavigationList");Check(primary.Items.Count==6&&settings.Items.Count==1,"Two navigation groups");Check(ReferenceEquals(primary.SelectedItem,section==NavigationSection.Settings?null:vm.SelectedNavigation)&&ReferenceEquals(settings.SelectedItem,section==NavigationSection.Settings?vm.SelectedNavigation:null),"Correct selection across groups");
      var icons=Visual<System.Windows.Shapes.Path>(primary).Concat(Visual<System.Windows.Shapes.Path>(settings)).ToArray();Check(icons.Length==7,"Seven vector icons");foreach(var icon in icons){Check(icon.Width==18&&icon.Height==18&&icon.StrokeThickness==1.6&&icon.Data!=null,"Uniform line icon scale");var item=Visual<ListBoxItem>(content).Single(c=>Visual<System.Windows.Shapes.Path>(c).Contains(icon));Check(item.Focusable&&KeyboardNavigation.GetIsTabStop(item),"Keyboard navigation retained");Check(Equals(icon.Stroke,app.Resources[item.IsSelected?"AccentBrush":"MutedBrush"]),"Selected/normal icon color");Check(Visual<TextBlock>(item).Any(t=>!string.IsNullOrEmpty(t.Text)),"Icon always accompanied by label");}
      Check(settings.TransformToAncestor(content).Transform(new Point()).Y>primary.TransformToAncestor(content).Transform(new Point()).Y+primary.ActualHeight,"Settings anchored below primary navigation");
      foreach(var item in Visual<ListBoxItem>(primary).Concat(Visual<ListBoxItem>(settings)).ToArray()){State(item,typeof(UIElement),"IsMouseOverPropertyKey",true);Layout(content,width);var icon=Visual<System.Windows.Shapes.Path>(item).Single();Check(Equals(icon.Stroke,app.Resources[item.IsSelected?"AccentBrush":"TextBrush"]),"Navigation hover contrast preserves selection");Check(Equals(((Border)item.Template.FindName("ItemShell",item)).Background,app.Resources[item.IsSelected?"NavigationSelectedBrush":"NavigationHoverBrush"]),"Navigation hover/selected surface");State(item,typeof(UIElement),"IsMouseOverPropertyKey",false);}
      var notice=Visual<TextBlock>(content).Single(t=>System.Windows.Data.BindingOperations.GetBinding(t,TextBlock.TextProperty)!=null&&System.Windows.Data.BindingOperations.GetBinding(t,TextBlock.TextProperty).Path.Path=="PageNotice").Parent as Border;Check(!vm.HasPageNotice&&notice.Visibility==Visibility.Collapsed&&((Grid)window.FindName("MainArea")).RowDefinitions[1].ActualHeight==0,"Single description, fully collapsed notice");
      foreach(var button in Visual<Button>(content).Where(b=>Shown(b)&&b.Style==app.Resources["PrimaryButton"]).ToArray()){Check(button.FontSize==13&&button.FontWeight==FontWeights.SemiBold,"Common primary typography");Check(Equals(button.Background,app.Resources["PrimaryBackgroundBrush"])&&Equals(button.Foreground,app.Resources["PrimaryForegroundBrush"]),"Primary theme surface/text");State(button,typeof(UIElement),"IsMouseOverPropertyKey",true);Layout(content,width);Check(Equals(((Border)button.Template.FindName("Shell",button)).Background,app.Resources["PrimaryHoverBrush"]),"Primary hover");State(button,typeof(ButtonBase),"IsPressedPropertyKey",true);Layout(content,width);Check(Equals(((Border)button.Template.FindName("Shell",button)).Background,app.Resources["PrimaryPressedBrush"]),"Primary pressed");State(button,typeof(ButtonBase),"IsPressedPropertyKey",false);State(button,typeof(UIElement),"IsMouseOverPropertyKey",false);button.IsEnabled=false;Layout(content,width);Check(((Border)button.Template.FindName("Shell",button)).Opacity==0.45,"Primary disabled state");button.IsEnabled=true;}
      foreach(var text in Visual<TextBlock>((FrameworkElement)window.FindName("SidebarLayout"))){var point=text.TransformToAncestor(content).Transform(new Point());Check(point.X>=0&&point.X+text.ActualWidth<=220,"Sidebar text fits at minimum width");}
      if(section==NavigationSection.Settings){Check(vm.PageDescription=="Personaliza la apariencia y consulta la configuración de WinSereno.","Settings exact description");foreach(var title in new[]{"Apariencia","Datos y registros","Acerca de","Preferencias"})Check(Visual<TextBlock>(content).Any(t=>Shown(t)&&t.Text==title),"Settings block "+title);Check(!vm.CheckUpdatesCommand.CanExecute(null),"Updater remains disabled");}
      if(section==NavigationSection.Activity)Check(vm.PageDescription=="Consulta las acciones realizadas durante esta sesión; los logs TXT conservan el registro persistente.","Activity exact description");
     }
     Layout(content,1150);Save(content,Path.Combine(output,"Beta4-"+section+"-"+theme+".png"));Layout(content,900);Save(content,Path.Combine(output,"Beta4-"+section+"-"+theme+"-900.png"));window.Close();
    }
   }
   // Exercise the actual two WPF selectors in the same loaded shell, rather than
   // assigning the ViewModel before constructing a fresh window for each page.
   home.Stop(); // The loaded shell must never start a hardware query in this fixture.
   foreach(string theme in new[]{"Light","Dark"})foreach(double width in new[]{1150.0,900.0}){
    vm.SelectedTheme=theme=="Dark"?"Oscuro":"Claro";themes.Apply(theme);var window=new MainWindow(vm,dialogs){Left=-30000,Top=-30000,ShowInTaskbar=false,Width=width};
    try{
     window.Show();var navigationRoot=(FrameworkElement)window.Content;Layout(navigationRoot,width);
     var main=(ListBox)window.FindName("MainNavigationList");var settings=(ListBox)window.FindName("SettingsNavigationList");
     ActivateItem(main,0);CheckNavigation(window,vm,NavigationSection.Home,width);
     ActivateItem(settings,0);CheckNavigation(window,vm,NavigationSection.Settings,width);
     ActivateItem(main,2);CheckNavigation(window,vm,NavigationSection.Repair,width);
     if(width==1150)Save(navigationRoot,Path.Combine(output,"SidebarNavigation-Repair-"+theme+".png"));
     ActivateItem(settings,0);CheckNavigation(window,vm,NavigationSection.Settings,width);
     if(width==1150)Save(navigationRoot,Path.Combine(output,"SidebarNavigation-Settings-"+theme+".png"));
     ActivateItem(main,5);CheckNavigation(window,vm,NavigationSection.Activity,width);
     ActivateItem(settings,0);CheckNavigation(window,vm,NavigationSection.Settings,width);
     ActivateItem(main,0);CheckNavigation(window,vm,NavigationSection.Home,width);
     // Use WPF routed keyboard input without OS key injection or real tools.
     window.Activate();Check(((ListBoxItem)main.ItemContainerGenerator.ContainerFromIndex(0)).Focus(),"Sidebar accepts keyboard focus");
     KeyPress(main,Key.Down);CheckNavigation(window,vm,NavigationSection.Diagnosis,width);
     KeyPress(main,Key.Down);CheckNavigation(window,vm,NavigationSection.Repair,width);
     KeyPress(main,Key.Up);CheckNavigation(window,vm,NavigationSection.Diagnosis,width);
     Check(((ListBoxItem)settings.ItemContainerGenerator.ContainerFromIndex(0)).Focus(),"Settings accepts keyboard focus");
     CheckNavigation(window,vm,NavigationSection.Diagnosis,width);
     KeyPress(settings,Key.Enter);CheckNavigation(window,vm,NavigationSection.Settings,width);
     KeyPress(settings,Key.Space);CheckNavigation(window,vm,NavigationSection.Settings,width);
     KeyPress(settings,Key.Enter);CheckNavigation(window,vm,NavigationSection.Settings,width);
     vm.SelectedMainNavigation=null;vm.SelectedSettingsNavigation=null;CheckNavigation(window,vm,NavigationSection.Settings,width);
     vm.SelectedMainNavigation=vm.SettingsNavigation[0];CheckNavigation(window,vm,NavigationSection.Settings,width);
     ActivateItem(main,0);CheckNavigation(window,vm,NavigationSection.Home,width);
     vm.SelectedSettingsNavigation=vm.MainNavigation[0];CheckNavigation(window,vm,NavigationSection.Home,width);
    }finally{window.Close();}
   }
   Check(!ops.IsActive&&launches.Count==4,"No real actions/UAC or automatic networking");
   // Cover the production constructor (no shell argument), then replace only its
   // resolved service with the safe launch recorder before executing UI commands.
   var defaultOps=new OperationCoordinator();var defaultIntegrity=new IntegritySessionState();
   var defaultVm=new MainViewModel(storage,new AppSettings(),themes,dialogs,new Beta4Runner(),new HomeViewModel(null,null),new DiagnosticViewModel(null,defaultOps,null,dialogs,defaultIntegrity),defaultOps,defaultIntegrity,null);
   var shellField=typeof(MainViewModel).GetField("shell",BindingFlags.NonPublic|BindingFlags.Instance);Check(shellField.GetValue(defaultVm) is ApplicationShellService,"Normal constructor resolves an internal ApplicationShellService");shellField.SetValue(defaultVm,shell);
   Check(ReferenceEquals(shellField.GetValue(defaultVm),shell),"Default constructor launch service safely mocked");
   var failures=new List<string>();foreach(var command in new[]{defaultVm.OpenApplicationFolderCommand,defaultVm.OpenGitHubCommand,defaultVm.OpenReleasesCommand}){try{command.Execute(null);}catch(Exception ex){failures.Add(ex.GetType().Name);}}
   Check(failures.Count==0,"Production constructor UI commands must use the resolved service: "+string.Join(", ",failures));
   Check(launches.Count==7&&launches.Skip(4).Select(i=>i.FileName).SequenceEqual(new[]{Path.GetFullPath(root),ApplicationShellService.GitHubUrl,ApplicationShellService.ReleasesUrl}),"Default constructor uses fixed targets without launching real processes");
   Check(launches.Skip(4).All(i=>i.UseShellExecute&&i.Verb=="open"&&string.IsNullOrEmpty(i.Arguments)),"Normal constructor commands never use arbitrary arguments or UAC");
  }finally{var target=Path.GetFullPath(root);var allowed=Path.GetFullPath(Path.Combine(project,"bin","Debug"))+Path.DirectorySeparatorChar;if(!target.StartsWith(allowed,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(target).StartsWith("Beta4Fixture-"))throw new Exception("Unsafe fixture cleanup");Directory.Delete(target,true);}
  return count+" comprobaciones Beta4 correctas; navegación, tema, preferencias, enlaces y capturas simuladas, sin acciones reales ni UAC.";
 }
}
'@
$provider=[Microsoft.CSharp.CSharpCodeProvider]::new();$parameters=[CodeDom.Compiler.CompilerParameters]::new();$parameters.GenerateInMemory=$true
foreach($reference in @($exe,'System.dll','System.Core.dll','System.Xaml.dll','System.Runtime.Serialization.dll',[System.Windows.Automation.Provider.ISelectionItemProvider].Assembly.Location,[System.Windows.Automation.AutomationElementIdentifiers].Assembly.Location,[System.Windows.Application].Assembly.Location,[System.Windows.Media.Visual].Assembly.Location,[System.Windows.DependencyObject].Assembly.Location)){[void]$parameters.ReferencedAssemblies.Add($reference)}
$compiled=$provider.CompileAssemblyFromSource($parameters,$source)
if($compiled.Errors.HasErrors){throw ($compiled.Errors | Out-String)}
if($CompileOnly){'Harness Beta4 compilado sin ejecutar.';return}
[Beta4ExperienceChecks]::Run($project)
