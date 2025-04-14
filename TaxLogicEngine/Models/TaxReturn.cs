
namespace TaxLogicEngine.Models
{
    public class TaxReturn
    {
        public string SIN { get; set; } = string.Empty;
        public string TaxYear { get; set; } = DateTime.UtcNow.Year.ToString();
        public string ProvinceCode { get; set; } = "ON";

        public List<TaxLineEntry> Lines { get; set; } = new();

        // Optional: summary fields
        public decimal TotalIncome => Lines
            .Where(l => l.LineNumber.StartsWith("10")) // Example: line 10100, 10400, etc.
            .Sum(l => l.Amount);

        public decimal TotalTaxPaid => Lines
            .Where(l => l.LineNumber == "43700") // Line 43700: Total tax deducted
            .Sum(l => l.Amount);
    }

}
