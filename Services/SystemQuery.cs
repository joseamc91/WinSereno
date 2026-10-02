using System;
using System.Collections.Generic;
using System.Management;

namespace WinSereno.Services
{
    internal static class SystemQuery
    {
        public static IList<Dictionary<string, object>> Read(string query, params string[] fields)
            => ReadNamespace(@"\\.\root\cimv2", query, fields);
        public static IList<Dictionary<string, object>> ReadNamespace(string scope, string query, params string[] fields)
        {
            var rows = new List<Dictionary<string, object>>();
            using (var searcher = new ManagementObjectSearcher(new ManagementScope(scope), new ObjectQuery(query),
                new EnumerationOptions { Timeout = TimeSpan.FromSeconds(8), ReturnImmediately = true }))
            using (var results = searcher.Get())
            {
                foreach (ManagementObject item in results)
                using (item)
                {
                    var row = new Dictionary<string, object>();
                    foreach (var field in fields) row[field] = item[field];
                    rows.Add(row);
                }
            }
            return rows;
        }
        public static void Log(ISessionLogger logger, string message)
        {
            // A log failure during shutdown must not turn a read-only query into an application failure.
            try { logger.Write(message); } catch (ObjectDisposedException) { } catch (System.IO.IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
