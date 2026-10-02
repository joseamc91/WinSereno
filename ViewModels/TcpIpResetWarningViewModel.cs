using WinSereno.Infrastructure;
using WinSereno.Models;

namespace WinSereno.ViewModels
{
    public sealed class TcpIpResetWarningViewModel : ObservableObject
    {
        public TcpIpResetSnapshot Snapshot { get; }
        private bool acknowledged;
        public bool Acknowledged { get => acknowledged; set { if (Set(ref acknowledged, value)) Raise(nameof(CanContinue)); } }
        public bool CanContinue => Acknowledged;
        public TcpIpResetWarningViewModel(TcpIpResetSnapshot snapshot) { Snapshot = snapshot; }
    }
}
