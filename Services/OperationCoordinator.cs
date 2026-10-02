using System;
using System.Threading;
using System.Threading.Tasks;

namespace WinSereno.Services
{
    // Shared by every maintenance/mock/diagnostic entry point. No process execution belongs here.
    public sealed class OperationCoordinator
    {
        private readonly object sync = new object();
        private OperationLease current;
        public bool IsActive { get { lock (sync) return current != null; } }
        public string Name { get { lock (sync) return current?.Name; } }
        public bool CanBeCancelled { get { lock (sync) return current?.CanBeCancelled == true; } }
        public event EventHandler Changed;
        public OperationLease Begin(string name, bool canBeCancelled)
        {
            OperationLease lease;
            lock (sync)
            {
                if (current != null) throw new InvalidOperationException("Ya hay una operación activa.");
                current = lease = new OperationLease(this, name, canBeCancelled);
            }
            Changed?.Invoke(this, EventArgs.Empty);
            return lease;
        }
        public bool RequestCancellation()
        {
            lock (sync)
            {
                if (current == null || !current.CanBeCancelled) return false;
                current.Cancel(); return true;
            }
        }
        public Task WaitForIdleAsync() { lock (sync) return current?.Completion ?? Task.CompletedTask; }
        internal void End(OperationLease lease)
        {
            lock (sync) { if (current == lease) current = null; }
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
    public sealed class OperationLease : IDisposable
    {
        private OperationCoordinator owner;
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Name { get; }
        public bool CanBeCancelled { get; }
        public CancellationToken Token => cancellation.Token;
        internal Task Completion => completion.Task;
        internal OperationLease(OperationCoordinator owner, string name, bool cancelable) { this.owner = owner; Name = name; CanBeCancelled = cancelable; }
        internal void Cancel() => cancellation.Cancel();
        public void Dispose()
        {
            var previous = Interlocked.Exchange(ref owner, null);
            if (previous == null) return;
            previous.End(this);
            cancellation.Dispose(); completion.TrySetResult(true);
        }
    }
}
