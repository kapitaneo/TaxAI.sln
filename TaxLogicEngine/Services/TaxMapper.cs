using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TaxLogicEngine.Models;

namespace TaxLogicEngine.Services
{
    public class TaxMapper
    {
        private readonly Dictionary<string, MappingRule> _mapping;

        public TaxMapper(string mappingFilePath)
        {
            var json = File.ReadAllText(mappingFilePath);
            _mapping = JsonSerializer.Deserialize<Dictionary<string, MappingRule>>(json)
                       ?? new Dictionary<string, MappingRule>();
        }

        public TaxReturn MapFromT4(T4Slip slip)
        {
            var taxReturn = new TaxReturn
            {
                SIN = slip.SIN,
                TaxYear = slip.TaxYear,
                ProvinceCode = slip.ProvinceCode
            };

            foreach (var box in slip.Boxes)
            {
                if (_mapping.TryGetValue(box.Key, out var rule))
                {
                    taxReturn.Lines.Add(new TaxLineEntry
                    {
                        LineNumber = rule.Line,
                        Description = rule.Description,
                        Amount = box.Value,
                        IsFederal = rule.IsFederal,
                        IsProvincial = rule.IsProvincial
                    });
                }
            }

            return taxReturn;
        }

        private class MappingRule
        {
            public string Line { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public bool IsFederal { get; set; }
            public bool IsProvincial { get; set; }
        }
    }
}
