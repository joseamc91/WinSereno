using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
namespace WinSereno.Models
{
    public sealed class RestartAdapter
    {
        internal RestartAdapter(string id, string name, string description, string kind)
        { InterfaceId = id; Name = name; Description = description; Kind = kind; }
        public string InterfaceId { get; }
        public string Name { get; }
        public string Description { get; }
        public string Kind { get; }
        public string DisplayName => Name + WinSereno.Localization.LocalizationService.Source("Text.Separator") + Kind + WinSereno.Localization.LocalizationService.Source("Text.Separator") + Description;
    }
    public sealed class AdapterRestartSelection
    {
        internal AdapterRestartSelection(IList<RestartAdapter> adapters, bool succeeded)
        { Adapters = new ReadOnlyCollection<RestartAdapter>(adapters); ReadSucceeded = succeeded; }
        public IReadOnlyList<RestartAdapter> Adapters { get; }
        public bool ReadSucceeded { get; }
    }
    public enum AdapterRestartState { Unknown, Disabled, Enabled, Active }
    public sealed class AdapterRestartAttempt
    {
        public MaintenanceTaskResult Result { get; set; }
        public bool CommandStarted { get; set; }
        public AdapterRestartState State { get; set; }
    }
}
