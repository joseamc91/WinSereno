using System;
using System.Text;

namespace WinSereno.Services
{
    internal sealed class OutputLineBuffer
    {
        private readonly StringBuilder pending = new StringBuilder();
        private readonly Action<string> emit;
        private bool completed;

        public OutputLineBuffer(Action<string> emit) { this.emit = emit ?? throw new ArgumentNullException(nameof(emit)); }

        public void Append(string chunk)
        {
            if (completed) throw new InvalidOperationException("La salida ya ha finalizado.");
            foreach (char character in chunk)
            {
                // Hold CR until the next character to preserve CRLF even across reads.
                if (pending.Length > 0 && pending[pending.Length - 1] == '\r' && character != '\n') Emit();
                pending.Append(character);
                if (character == '\n') Emit();
            }
        }

        public void Complete()
        {
            if (completed) return;
            completed = true;
            if (pending.Length > 0) Emit();
        }

        private void Emit()
        {
            string line = pending.ToString();
            pending.Clear();
            emit(line);
        }
    }
}
