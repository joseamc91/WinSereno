param([switch]$CompileOnly)
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
$exe=Join-Path $project 'bin\Debug\WinSereno.exe'
if(-not $CompileOnly){[void][Reflection.Assembly]::LoadFrom($exe)}
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Xaml
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
using System.Windows.Data;
using InlineRun = System.Windows.Documents.Run;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ellipse = System.Windows.Shapes.Ellipse;
using WinSereno.Models;
using WinSereno.Services;
using WinSereno.ViewModels;
using WinSereno.Views;
public sealed class NetworkFixtureRunner:IMaintenanceTaskRunner {
 public TaskProgress Current{get{return null;}}public bool IsActive{get{return false;}}
 public event EventHandler<TaskProgress> ProgressChanged{add{}remove{}}
 public Task<MaintenanceTaskResult> RunAsync(MaintenanceTask task){throw new Exception("No real tasks allowed");}
 public bool RequestCancellation(){return false;}public Task WaitForIdleAsync(){return Task.CompletedTask;}
}
public sealed class NetworkFixtureDialogs:IDialogService {
 public void ShowOutput(TaskProgress p){}public void ShowDiagnosticDetails(DiagnosticResult r){}public void ShowMessage(string s){throw new Exception(s);}
 public bool ConfirmTask(MaintenanceTask t){throw new Exception("No confirmations/UAC allowed");}public bool ConfirmCancelAndClose(){return false;}
 public RestartAdapter SelectRestartAdapter(IReadOnlyList<RestartAdapter> values){throw new Exception("No real actions allowed");}
}
public static class NetworkPresentationChecks {
 static int count;static void Check(bool value,string name){if(!value)throw new Exception(name);count++;}
 static IEnumerable<T> Visual<T>(DependencyObject root)where T:DependencyObject{
  if(root is T)yield return (T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in Visual<T>(VisualTreeHelper.GetChild(root,i)))yield return child;
 }
 static bool Shown(DependencyObject item){for(var p=item;p!=null;p=VisualTreeHelper.GetParent(p))if(p is UIElement&&((UIElement)p).Visibility!=Visibility.Visible)return false;return true;}
 static void Layout(FrameworkElement root,double width=1150){root.Measure(new Size(width,800));root.Arrange(new Rect(0,0,width,800));root.UpdateLayout();}
 static void Set(object target,string name,object value){target.GetType().GetProperty(name).SetValue(target,value,null);}
 static void Save(FrameworkElement root,string file){var b=new RenderTargetBitmap((int)root.ActualWidth,(int)root.ActualHeight,96,96,PixelFormats.Pbgra32);var backdrop=new DrawingVisual();using(var drawing=backdrop.RenderOpen())drawing.DrawRectangle((Brush)Application.Current.Resources["BackgroundBrush"],null,new Rect(0,0,root.ActualWidth,root.ActualHeight));b.Render(backdrop);b.Render(root);var e=new PngBitmapEncoder();e.Frames.Add(BitmapFrame.Create(b));using(var s=File.Create(file))e.Save(s);}
 static void CheckGrid(MainWindow window,FrameworkElement root,ItemsControl cards,NetworkViewModel network,string label){
  var panel=Visual<UniformGrid>(cards).Single();var borders=Visual<Border>(cards).Where(b=>ReferenceEquals(b.Style,window.FindResource("ThreeColumnCard"))).ToList();
  Check(panel.Columns==3&&borders.Count==network.Adapters.Count,"Three-column dynamic adapter grid: "+label);
  Check(borders.Select(b=>b.DataContext).SequenceEqual(network.Adapters.Cast<object>()),"Stable adapter order: "+label);
  Check(borders.All(b=>double.IsNaN(b.Width)&&double.IsPositiveInfinity(b.MaxWidth)&&b.HorizontalAlignment==HorizontalAlignment.Stretch),"No fixed or manually calculated adapter width: "+label);
  double cellWidth=panel.ActualWidth/3;int rows=(network.Adapters.Count+2)/3;double cellHeight=panel.ActualHeight/rows;
  var points=borders.Select(b=>b.TransformToAncestor(root).Transform(new Point())).ToList();
  for(int i=0;i<borders.Count;i++){
   Check(Math.Abs(borders[i].ActualWidth-(cellWidth-10))<0.5,"Equal column width even with empty cells: "+label+" / "+i);
   Check(Math.Abs(points[i].X-points[0].X-(i%3)*cellWidth)<0.5&&Math.Abs(points[i].Y-points[0].Y-(i/3)*cellHeight)<0.5,"Row/column placement: "+label+" / "+i);
   Check(Math.Abs(borders[i].ActualHeight-borders[0].ActualHeight)<0.5,"Equal card height: "+label+" / "+i);
   var details=Visual<Expander>(borders[i]).Single(e=>Convert.ToString(e.Header)=="Detalles del adaptador");
   var footer=details.TransformToAncestor(borders[i]).Transform(new Point());
   Check(Shown(details)&&Math.Abs(footer.Y+details.ActualHeight-(borders[i].ActualHeight-13))<0.5,"Details anchored at bottom, regardless of content: "+label+" / "+i);
   foreach(var text in Visual<TextBlock>(details).Where(t=>Shown(t))){var p=text.TransformToAncestor(details).Transform(new Point());Check(p.Y>=-0.5&&p.Y+text.ActualHeight<=details.ActualHeight+0.5,"Expanded detail text not vertically clipped: "+label);}
   var layout=(Grid)details.Parent;
   Check(Grid.GetRow(details)==2&&layout.RowDefinitions[1].Height.IsStar&&layout.RowDefinitions[0].Height.IsAuto&&layout.RowDefinitions[2].Height.IsAuto,"Structural Auto/star/Auto footer: "+label);
   foreach(var text in Visual<TextBlock>(borders[i]).Where(t=>Shown(t))){var p=text.TransformToAncestor(borders[i]).Transform(new Point());Check(p.X>=-0.5&&p.X+text.ActualWidth<=borders[i].ActualWidth+0.5,"Text contained in adapter card: "+label+" / "+text.Text);}
  }
  var refresh=Visual<Button>(root).Single(b=>ReferenceEquals(b.Command,network.RefreshCommand));var buttonPoint=refresh.TransformToAncestor(root).Transform(new Point());
  Check(Math.Abs(points[0].X+2*cellWidth+borders[0].ActualWidth-buttonPoint.X-refresh.ActualWidth)<0.5,"Third column aligns with Refresh even if empty: "+label);
  var parent=(FrameworkElement)cards.Parent;var parentPoint=parent.TransformToAncestor(root).Transform(new Point());
  Check(Math.Abs(points[0].X-parentPoint.X)<0.5&&Math.Abs(cards.ActualWidth+cards.Margin.Left+cards.Margin.Right-parent.ActualWidth)<0.5,"Adapter grid fills useful page width: "+label);
 }
 public static string Run(string project){
  var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/WinSereno;component/Themes/Styles.xaml",UriKind.Relative)});
  foreach(string theme in new[]{"Light","Dark"}){
   var colors=new ResourceDictionary{Source=new Uri("/WinSereno;component/Themes/"+theme+".xaml",UriKind.Relative)};app.Resources.MergedDictionaries.Add(colors);
   var ops=new OperationCoordinator();var dialogs=new NetworkFixtureDialogs();var state=new IntegritySessionState();
   var vm=new MainViewModel(new PortableStorage(project),new AppSettings(),new ThemeService(),dialogs,new NetworkFixtureRunner(),new HomeViewModel(null,null),new DiagnosticViewModel(null,ops,null,dialogs,state),ops,state,null);
   // Select without invoking the navigation refresh; these are presentation fixtures only.
   typeof(MainViewModel).GetField("selectedNavigation",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(vm,vm.Navigation.Single(n=>n.Section==NavigationSection.Network));
   var ethernet=new NetworkAdapterInformation{Name="Ethernet",Description="Intel Ethernet Controller",Kind="Ethernet",Status="Conectado",LinkSpeed="1 Gbps",IPv4="192.0.2.10",Gateway="192.0.2.1",Dns="192.0.2.53",MaximumSpeed="10 Gbps"};vm.Network.Adapters.Add(ethernet);
   Set(vm.Network,"Summary","Correcto · Conectividad funcional de Internet confirmada mediante HTTPS.");Set(vm.Network,"ConnectivityStatusCode","Healthy");Set(vm.Network,"Connectivity","✓ Gateway: respuesta ICMP recibida\n✓ DNS: hostname resuelto\n✓ HTTPS: conexión TLS válida");
   typeof(NetworkViewModel).GetField("refreshedAt",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(vm.Network,(DateTimeOffset?)DateTimeOffset.Now);
   var window=new MainWindow(vm,dialogs);var root=(FrameworkElement)window.Content;Layout(root);
   var main=(Grid)window.FindName("MainArea");var cards=(ItemsControl)window.FindName("NetworkAdapterCards");var scroll=(ScrollViewer)window.FindName("PageScroll");
   Check(!vm.HasPageNotice&&main.RowDefinitions[1].ActualHeight==0,"Network notice fully collapsed");
   Check(vm.PageDescription=="Consulta y actualiza el estado de la red; las herramientas solicitan confirmación y permisos de administrador cuando corresponde.","One precise header");
   Check(cards.Items.Count==1&&Visual<UniformGrid>(cards).Single().Columns==3,"Physical adapter cards in three-column grid");
   var homeCards=(ItemsControl)window.FindName("HomeInformationCards");
   Check(ReferenceEquals(cards.Style,homeCards.Style)&&ReferenceEquals(cards.ItemsPanel,homeCards.ItemsPanel),"Network and Home use identical layout resources");
   Check(!ethernet.HasInterfaceName&&ethernet.HasCardName,"Ethernet not repeated as friendly alias");
   Check(Visual<TextBlock>(cards).Count(t=>Shown(t)&&t.Text=="Ethernet")==1,"Ethernet visible exactly once");
   Check(Visual<TextBlock>(cards).Any(t=>Shown(t)&&t.Text==ethernet.Description),"Real hardware name shown");
   Check(!Visual<TextBlock>(cards).Any(t=>t.Text.Contains("Capacidad máxima")||t.Text=="10 Gbps"),"Maximum capacity absent");
   Check(Visual<TextBlock>(cards).Any(t=>Shown(t)&&t.Text=="1 Gbps")&&Visual<TextBlock>(cards).Any(t=>Shown(t)&&t.Text.Contains("192.0.2.10")),"Current link/IPv4 visible");
   Check(Equals(Visual<Ellipse>(cards).Single(e=>e.Name=="NetworkStateDot").Fill,app.Resources["GoodBrush"]),"Connected green dot");
   Check(Visual<TextBlock>(root).Any(t=>Shown(t)&&t.Text==vm.Network.RefreshedText),"Refresh timestamp visible");
   Check(scroll.ScrollableHeight<160,"Single adapter keeps normal page compact");
   var local=Visual<Expander>(cards).Single();Check(!local.IsExpanded,"Secondary adapter fields initially compact");local.IsExpanded=true;Layout(root);
   Check(Visual<TextBlock>(cards).Any(t=>Shown(t)&&t.Text=="Gateway: 192.0.2.1")&&Visual<TextBlock>(cards).Any(t=>Shown(t)&&t.Text=="DNS: 192.0.2.53"),"Gateway and DNS retained in adapter details");local.IsExpanded=false;
   var global=Visual<Expander>(root).Single(e=>Convert.ToString(e.Header)=="Detalles de conectividad");global.IsExpanded=true;Layout(root);
   Check(vm.Network.HasConnectivityDetails&&!vm.Network.Connectivity.Contains(ethernet.Description)&&!vm.Network.Connectivity.Contains("IPv4:"),"Global details complement adapter data");
   var wifi=new NetworkAdapterInformation{Name="Wi-Fi",Description="Wireless Controller",Kind="Wi-Fi",Status="Desconectado",LinkSpeed="No disponible"};vm.Network.Adapters.Add(wifi);Layout(root);
   Check(Visual<TextBlock>(cards).Count(t=>Shown(t)&&t.Text=="Wi-Fi")==1&&!wifi.HasLinkSpeed&&!wifi.HasIPv4,"Offline WiFi avoids duplication/unavailable filler");
   Check(Equals(Visual<Ellipse>(cards).Single(e=>e.Name=="NetworkStateDot"&&ReferenceEquals(e.DataContext,wifi)).Fill,app.Resources["ErrorBrush"]),"Disconnected red dot");
   wifi.Status="No conectado (Unknown)";cards.Items.Refresh();Layout(root);
   Check(Equals(Visual<Ellipse>(cards).Single(e=>e.Name=="NetworkStateDot"&&ReferenceEquals(e.DataContext,wifi)).Fill,app.Resources["MutedBrush"]),"Unknown state neutral");
   var custom=new NetworkAdapterInformation{Name="Oficina LAN",Description="Other Ethernet Controller",Kind="Ethernet",Status="Conectado"};vm.Network.Adapters.Add(custom);Layout(root);
   Check(custom.HasInterfaceName&&Visual<TextBlock>(cards).Any(t=>Shown(t)&&t.Text==custom.Name),"Custom real interface name retained");
   wifi.Status="Desconectado";global.IsExpanded=false;
   wifi.WifiDetails="Perfil Wi-Fi: prueba controlada\nSeñal: 75 %";cards.Items.Refresh();Layout(root,900);
   var wifiExpander=Visual<Expander>(cards).Single(e=>ReferenceEquals(e.DataContext,wifi));wifiExpander.IsExpanded=true;Layout(root,900);
   Check(Visual<TextBlock>(wifiExpander).Any(t=>Shown(t)&&t.Text==wifi.WifiDetails),"Expanded WiFi fields preserved at narrow width");
   wifiExpander.IsExpanded=false;wifi.WifiDetails=null;cards.Items.Refresh();
   var samples=new[]{ethernet,wifi,custom,
    new NetworkAdapterInformation{Name="Ethernet 2",Description="Additional Ethernet Controller",Kind="Ethernet",Status="Conectado",LinkSpeed="1 Gbps",IPv4="192.0.2.14"},
    new NetworkAdapterInformation{Name="Wi-Fi 2",Description="Additional Wireless Controller",Kind="Wi-Fi",Status="Desconectado"},
    new NetworkAdapterInformation{Name="Ethernet 3",Description="Sixth Physical Adapter",Kind="Ethernet",Status="Conectado",LinkSpeed="100 Mbps",IPv4="192.0.2.16"}};
   var imageDir=Path.Combine(project,"bin","Debug","VisualChecks");Directory.CreateDirectory(imageDir);
   for(int total=1;total<=6;total++){
    vm.Network.Adapters.Clear();foreach(var adapter in samples.Take(total))vm.Network.Adapters.Add(adapter);
    foreach(double width in new[]{1150.0,900.0,1280.0}){Layout(root,width);CheckGrid(window,root,cards,vm.Network,theme+" / "+total+" adapters / "+width);}
    var detailList=Visual<Expander>(cards).ToList();detailList[0].IsExpanded=true;Layout(root,900);
    Check(Visual<TextBlock>(cards).Any(t=>Shown(t)&&t.Text=="Gateway: 192.0.2.1")&&Visual<TextBlock>(cards).Any(t=>Shown(t)&&t.Text=="DNS: 192.0.2.53"),"Expanded details retained at narrow width: "+total);
    CheckGrid(window,root,cards,vm.Network,theme+" / expanded / "+total);
    detailList[0].IsExpanded=false;
    Layout(root);if(total>=2&&total<=4)Save(root,Path.Combine(imageDir,"NetworkThreeColumns-"+total+"-"+theme+".png"));
   }
   var header=(Grid)window.FindName("NetworkStatusHeader");var heading=(TextBlock)window.FindName("NetworkConnectivityHeading");
   var statusRun=heading.Inlines.OfType<InlineRun>().Single(r=>BindingOperations.GetBinding(r,InlineRun.TextProperty)!=null);
   var refresh=Visual<Button>(header).Single(b=>ReferenceEquals(b.Command,vm.Network.RefreshCommand));
   Check(ReferenceEquals(heading.Parent,header)&&ReferenceEquals(refresh.Parent,header)&&Grid.GetColumn(refresh)==1,"Title, status and right-aligned Refresh share one header");
   Check(heading.Inlines.OfType<InlineRun>().First().Text=="Estado de red","Network title retained in the same text line");
   foreach(string status in new[]{"Healthy","Attention","Error","Failed","Unknown","NotChecked"}){
    Set(vm.Network,"ConnectivityStatusCode",status);
    string expected=status=="Healthy"?"Internet disponible":status=="Attention"?"Conexión con incidencias":status=="Error"||status=="Failed"?"Sin conexión a Internet":"Conexión no comprobada";
    foreach(double width in new[]{1150.0,900.0}){
     Layout(root,width);Check(statusRun.Text==expected&&vm.Network.ConnectivityStatusText==expected,"Structured short status: "+theme+" / "+status);
     Check(Equals(statusRun.Foreground,app.Resources[status=="Healthy"?"GoodBrush":status=="Attention"?"WarningBrush":status=="Error"||status=="Failed"?"ErrorBrush":"MutedBrush"]),"Global status color: "+theme+" / "+status);
     var textPoint=heading.TransformToAncestor(header).Transform(new Point());var buttonPoint=refresh.TransformToAncestor(header).Transform(new Point());
     Check(textPoint.X+heading.ActualWidth<=buttonPoint.X&&buttonPoint.X+refresh.ActualWidth<=header.ActualWidth+0.5,"Header avoids overlap at "+width);
     Check(heading.ActualHeight<34,"Title and short status stay on one line at "+width);
     var stamp=Visual<TextBlock>(root).Single(t=>t.Text==vm.Network.RefreshedText);var stampPoint=stamp.TransformToAncestor(root).Transform(new Point());var headerPoint=header.TransformToAncestor(root).Transform(new Point());
     Check(Shown(stamp)&&stampPoint.Y>=headerPoint.Y+header.ActualHeight,"Timestamp remains visible beneath header");
    }
   }
   Set(vm.Network,"ConnectivityStatusCode","Healthy");Layout(root);Save(root,Path.Combine(imageDir,"Network-"+theme+".png"));
   Check(Visual<Button>(root).Count(b=>Shown(b)&&new[]{"Vaciar caché DNS","Renovar dirección DHCP","Reiniciar adaptador de red","Restablecer Winsock","Restablecer TCP/IP"}.Contains(Convert.ToString(b.Content)))==5,"All five tools retained without execution");
   window.Close();app.Resources.MergedDictionaries.Remove(colors);
  }
  return count+" comprobaciones de presentación Red correctas; temas y adaptadores simulados, sin consultas reales ni UAC.";
 }
}
'@
$provider=[Microsoft.CSharp.CSharpCodeProvider]::new();$parameters=[CodeDom.Compiler.CompilerParameters]::new();$parameters.GenerateInMemory=$true
foreach($reference in @($exe,'System.dll','System.Core.dll','System.Xml.dll','System.Xaml.dll','System.Runtime.Serialization.dll',[System.Windows.Application].Assembly.Location,[System.Windows.Media.Visual].Assembly.Location,[System.Windows.DependencyObject].Assembly.Location)){[void]$parameters.ReferencedAssemblies.Add($reference)}
$compiled=$provider.CompileAssemblyFromSource($parameters,$source)
if($compiled.Errors.HasErrors){throw ($compiled.Errors | Out-String)}
if($CompileOnly){'Harness visual de Red compilado sin ejecutar ni cargar WinSereno.exe.';return}
[NetworkPresentationChecks]::Run($project)
