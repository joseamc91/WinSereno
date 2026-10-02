using System.Collections.Generic;
using System.Collections.ObjectModel;
namespace WinSereno.Models
{
    public sealed class DhcpAdapter
    {
        public string InterfaceId { get; }
        public string Name { get; }
        public string Description { get; }
        public string Kind { get; }
        public string IPv4 { get; }
        internal DhcpAdapter(string id, string name, string description, string kind, string ipv4)
        { InterfaceId = id; Name = name; Description = description; Kind = kind; IPv4 = ipv4; }
    }
    public sealed class DhcpRenewalPlan
    {
        public ReadOnlyCollection<DhcpAdapter> Adapters { get; }
        public ReadOnlyCollection<string> Exclusions { get; }
        public bool ReadSucceeded { get; }
        internal DhcpRenewalPlan(IList<DhcpAdapter> adapters, IList<string> exclusions, bool succeeded)
        { Adapters = new ReadOnlyCollection<DhcpAdapter>(adapters); Exclusions = new ReadOnlyCollection<string>(exclusions); ReadSucceeded = succeeded; }
    }
    public enum DhcpVerification : byte { Unknown, Released, Renewed, NotConfirmed }
    public sealed class DhcpRunOutcome
    {
        public IList<SequenceStepResult> Steps { get; } = new List<SequenceStepResult>();
        public int? ElevationResumeOrdinal { get; internal set; }
    }
}
