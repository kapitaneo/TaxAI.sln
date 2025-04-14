using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TaxLogicEngine.Models
{
    public class TaxLineEntry
    {
        public string LineNumber { get; set; } = string.Empty;       // e.g., "10100"
        public string Description { get; set; } = string.Empty;      // e.g., "Employment income"
        public decimal Amount { get; set; }                          // e.g., 52300
        public bool IsFederal { get; set; } = true;                  // Is this a federal line?
        public bool IsProvincial { get; set; } = false;              // Optional: provincial-specific
    }
}
