using System.Collections.Generic;
using System.Collections.ObjectModel;
using WinSereno.Models;

namespace WinSereno.Services
{
    public sealed class ActionHistoryService
    {
        private readonly ObservableCollection<ActionHistoryEntry> entries = new ObservableCollection<ActionHistoryEntry>();
        private readonly HashSet<MaintenanceTaskResult> recorded = new HashSet<MaintenanceTaskResult>();
        public ReadOnlyObservableCollection<ActionHistoryEntry> Entries { get; }
        public ActionHistoryService() { Entries = new ReadOnlyObservableCollection<ActionHistoryEntry>(entries); }
        public void Record(TaskProgress progress)
        {
            if (progress?.State != RunnerState.Completed || progress.CurrentTask == null || progress.Result == null ||
                progress.CurrentTask.IsMock || !recorded.Add(progress.Result)) return;
            var entry = new ActionHistoryEntry(progress.CurrentTask, progress.Result);
            int index = 0;
            while (index < entries.Count && entries[index].FinishedAt > entry.FinishedAt) index++;
            entries.Insert(index, entry);
        }
    }
}
