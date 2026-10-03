using System.Reflection;

namespace WinSereno.Infrastructure
{
    public static class ProductInformation
    {
        public static string Version => typeof(ProductInformation).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion;
        public static string DisplayVersion => Version;
    }
}
