using System;
using System.Windows.Input;

namespace WinSereno.Infrastructure
{
    public sealed class RelayCommand : ICommand
    {
        private readonly Action<object> execute;
        private readonly Predicate<object> canExecute;
        public RelayCommand(Action<object> execute, Predicate<object> canExecute = null)
        {
            this.execute = execute;
            this.canExecute = canExecute;
        }
        public bool CanExecute(object parameter) => canExecute == null || canExecute(parameter);
        public void Execute(object parameter) => execute(parameter);
        public event EventHandler CanExecuteChanged;
        public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
