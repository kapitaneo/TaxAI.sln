using System.Text.Json;
using TaxLogicEngine.Models;
using TaxLogicEngine.Services;

namespace TaxLogicEngine.Tests
{
    public class TaxMapperTests
    {
        [Fact]
        public void MapFromT4_ShouldMapKnownBoxesToLines()
        {
            // Arrange
            var testMapping = new Dictionary<string, object>
            {
                ["14"] = new
                {
                    line = "10100",
                    description = "Employment income",
                    isFederal = true,
                    isProvincial = true
                },
                ["22"] = new
                {
                    line = "43700",
                    description = "Income tax deducted",
                    isFederal = true,
                    isProvincial = true
                }
            };

            var tempFile = Path.GetTempFileName();
            File.WriteAllText(tempFile, JsonSerializer.Serialize(testMapping));

            var slip = new T4Slip
            {
                SIN = "123456789",
                TaxYear = "2023",
                ProvinceCode = "ON",
                Boxes = new Dictionary<string, decimal>
                {
                    ["14"] = 52300,
                    ["22"] = 7392
                }
            };

            var mapper = new TaxMapper(tempFile);

            // Act
            var result = mapper.MapFromT4(slip);

            // Assert
            Assert.Equal("123456789", result.SIN);
            Assert.Equal("2023", result.TaxYear);
            Assert.Equal(2, result.Lines.Count);

            var line10100 = result.Lines.FirstOrDefault(x => x.LineNumber == "10100");
            Assert.NotNull(line10100);
            Assert.Equal(52300, line10100.Amount);

            var line43700 = result.Lines.FirstOrDefault(x => x.LineNumber == "43700");
            Assert.NotNull(line43700);
            Assert.Equal(7392, line43700.Amount);
        }
    }
}
