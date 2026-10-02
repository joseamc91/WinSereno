$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $project 'bin\Debug\WinSereno.exe'
[void][Reflection.Assembly]::LoadFrom($exe)
Add-Type -AssemblyName PresentationFramework, WindowsBase
$source = @'
using System;
using System.IO;
using System.Windows;
using WinSereno.Services;
using WinSereno.Models;
using WinSereno.ViewModels;
public sealed class ThemeDialogs : IDialogService {
 public string Message;
 public void ShowMessage(string text) { Message = text; }
 public bool ConfirmTask(MaintenanceTask task) { throw new Exception("No se admite ejecutar tareas"); }
 public RestartAdapter SelectRestartAdapter(System.Collections.Generic.IReadOnlyList<RestartAdapter> adapters) { throw new Exception("No se admite red"); }
 public void ShowOutput(TaskProgress progress) { }
 public void ShowDiagnosticDetails(DiagnosticResult result) { }
 public bool ConfirmCancelAndClose() { return false; }
}
public static class ThemePersistenceChecks {
 static int count;
 static void Check(bool valid, string name) { if (!valid) throw new Exception(name); count++; }
 public static string Run(string root, string project) {
  var storage = new PortableStorage(root);
  Check(storage.LoadSettings().Theme == "Light", "Primera ejecución");
  Check(!File.Exists(storage.ConfigPath), "Lectura no crea configuración");
  foreach (string value in new[] {"Light", "Dark"}) {
   storage.SaveSettings(new AppSettings { Theme = value });
   Check(File.ReadAllText(storage.ConfigPath).Contains("\"Theme\":\""+value+"\""), "Valor simple persistido");
   Check(new PortableStorage(root).LoadSettings().Theme == value, "Nueva instancia restaura "+value);
   Check(!File.Exists(storage.ConfigPath+".tmp"), "Sin residuo de escritura");
  }
  foreach(string json in new[] {"{}", "{\"Theme\":\"Unexpected\"}", "{\"Theme\":null}", "null"}) {
   File.WriteAllText(storage.ConfigPath,json);
   Check(storage.LoadSettings().Theme == "Light", "Compatibilidad/valor inválido");
   Check(File.ReadAllText(storage.ConfigPath)==json, "Carga no reescribe preferencias");
  }
  File.WriteAllText(storage.ConfigPath,"{malformed");
  bool rejected=false; try { storage.LoadSettings(); } catch(System.Runtime.Serialization.SerializationException) { rejected=true; }
  Check(rejected, "JSON malformado mantiene rechazo controlado del inicio");
  Check(File.ReadAllText(storage.ConfigPath)=="{malformed", "Archivo malformado intacto");
  var app = new Application(); Application.ResourceAssembly = typeof(WinSereno.App).Assembly;
  var themes = new ThemeService();
  foreach(string value in new[] {"Light","Dark"}) {
   themes.Apply(value);
   Check(app.Resources.MergedDictionaries.Count==1, "Tema reemplazado en caliente");
   Check(app.Resources.MergedDictionaries[0].Source.ToString().Contains(value+".xaml"), "Recurso aplicado");
  }
  storage.SaveSettings(new AppSettings { Theme="Light" });
  var settings=storage.LoadSettings(); var dialogs=new ThemeDialogs(); var ops=new OperationCoordinator();
  using(var log = new SessionLogger(storage)) {
   var runner=new MockMaintenanceTaskRunner(log,ops); var integrity=new IntegritySessionState();
   var diagnosis=new DiagnosticViewModel(null,ops,log,dialogs,integrity);
   var vm=new MainViewModel(storage,settings,themes,dialogs,runner,null,diagnosis,ops,integrity,log);
   vm.SelectedTheme="Oscuro";
   Check(storage.LoadSettings().Theme=="Dark", "Selector guarda Dark");
   vm.SelectedTheme="Claro";
   Check(storage.LoadSettings().Theme=="Light", "Selector guarda Light");
   File.SetAttributes(storage.ConfigPath,FileAttributes.ReadOnly);
   try {
    vm.SelectedTheme="Oscuro";
    Check(vm.SelectedTheme=="Oscuro", "Fallo mantiene cambio de sesión");
    Check(dialogs.Message!=null && dialogs.Message.Contains("no se pudo guardar"), "Aviso de persistencia");
    Check(File.ReadAllText(storage.ConfigPath).Contains("Light"), "Config previa intacta");
    Check(!File.Exists(storage.ConfigPath+".tmp"), "Fallo no deja temporal");
   } finally { File.SetAttributes(storage.ConfigPath,FileAttributes.Normal); }
   Check(!ops.IsActive && !runner.IsActive, "Sin operación ni elevación");
  }
  Check(storage.ConfigPath==Path.Combine(root,"config.json"), "Única ubicación portable");
  string code=File.ReadAllText(Path.Combine(project,"Services","PortableStorage.cs"));
  Check(!code.Contains("ApplicationData") && !code.Contains("GetTempPath") && !code.Contains("Process.Start"), "Sin fallback ni UAC");
  string startup=File.ReadAllText(Path.Combine(project,"App.xaml.cs"));
  Check(startup.IndexOf("storage.LoadSettings()")<startup.IndexOf("themes.Apply(settings.Theme)") &&
        startup.IndexOf("themes.Apply(settings.Theme)")<startup.IndexOf("window.Show()"), "Tema anterior a ventana visible");
  Check(startup.Contains("settings = new AppSettings();") && startup.Contains("catch (Exception ex)"), "Inicio tolera JSON malformado");
  return count+" comprobaciones correctas; sin abrir ventanas ni ejecutar acciones/UAC.";
 }
}
'@
$provider = [Microsoft.CSharp.CSharpCodeProvider]::new()
$parameters = [CodeDom.Compiler.CompilerParameters]::new()
$parameters.GenerateInMemory = $true
foreach($reference in @($exe,'System.dll','System.Core.dll','System.Runtime.Serialization.dll','System.Xaml.dll',
 [System.Windows.Application].Assembly.Location,[System.Windows.Threading.Dispatcher].Assembly.Location)) {
 [void]$parameters.ReferencedAssemblies.Add($reference)
}
$compiled = $provider.CompileAssemblyFromSource($parameters,$source)
if($compiled.Errors.HasErrors) { throw ($compiled.Errors | Out-String) }
$fixture = Join-Path $PSScriptRoot ('theme-fixture-'+[Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($fixture)
try { [ThemePersistenceChecks]::Run($fixture,$project) }
finally {
 if([IO.Path]::GetFullPath($fixture).StartsWith([IO.Path]::GetFullPath($PSScriptRoot)+'\',[StringComparison]::OrdinalIgnoreCase)) {
  [IO.Directory]::Delete($fixture,$true)
 }
}
