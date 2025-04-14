
namespace TaxLogicEngine.Models
{
    public class T4Slip
    {
        public string EmployeeName { get; set; } = string.Empty;
        public string SIN { get; set; } = string.Empty;              // Box 12
 
        public string EmployerName { get; set; } = string.Empty;     // Box 50
        public string EmployerAccountNumber { get; set; } = string.Empty; // Box 54
  
        public string ProvinceOfEmployment { get; set; } = "ON";     // Box 10
        public bool IsExemptFromCPP { get; set; } = false;           // Box 28 (partly)
        public bool IsExemptFromEI { get; set; } = false;            // Box 28 (partly)
 
        public Dictionary<string, decimal> Boxes { get; set; } = new();

        public string TaxYear { get; set; } = DateTime.UtcNow.Year.ToString();
        public string ProvinceCode { get; set; } = "ON";
    }
}