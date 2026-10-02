namespace WinSereno.ViewModels
{
    // Structured presentation of diagnostic counters; never extracted from localized summaries.
    public sealed class OperationToastCount
    {
        public string StatusCode { get; }
        public string Text { get; }
        public string Separator { get; }
        public OperationToastCount(string statusCode, int count, bool first)
        {
            StatusCode = statusCode; Separator = first ? "" : " · ";
            string label = statusCode == "Healthy" ? (count == 1 ? "Correcto" : "Correctos") :
                statusCode == "Attention" ? "Atención" : statusCode == "Error" ? (count == 1 ? "Error" : "Errores") :
                (count == 1 ? "No comprobado" : "No comprobados");
            Text = count + " " + label;
        }
    }
}
