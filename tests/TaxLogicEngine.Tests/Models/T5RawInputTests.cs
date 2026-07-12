using TaxLogicEngine.Models;

namespace TaxLogicEngine.Tests.Models;

public class T5RawInputTests
{
    [Fact]
    public void Defaults_HaveEmptyBoxes_AndNullFields()
    {
        var input = new T5RawInput();

        Assert.Null(input.TaxYear);
        Assert.Null(input.PayerName);
        Assert.Null(input.RecipientName);
        Assert.Empty(input.Boxes);
    }

    [Fact]
    public void Fields_AreSettable()
    {
        var input = new T5RawInput
        {
            TaxYear = 2024,
            PayerName = "Payer",
            PayerAddress = "1 Bay St",
            RecipientName = "Doe, Jane",
            RecipientAddress = "2 King St",
            Boxes = new[] { new T5RawBox("13", "100.00") },
        };

        Assert.Equal(2024, input.TaxYear);
        Assert.Equal("Payer", input.PayerName);
        Assert.Equal("13", Assert.Single(input.Boxes).Code);
    }

    [Fact]
    public void Boxes_CanHoldDuplicateCodes()
    {
        // The whole reason Boxes is a list: duplicates are preserved for the
        // parser to reject, not silently collapsed.
        var input = new T5RawInput
        {
            Boxes = new[]
            {
                new T5RawBox("13", "100.00"),
                new T5RawBox("13", "200.00"),
            },
        };

        Assert.Equal(2, input.Boxes.Count);
        Assert.All(input.Boxes, b => Assert.Equal("13", b.Code));
    }

    [Fact]
    public void RawBox_AllowsNullValue_AndHasValueEquality()
    {
        Assert.Null(new T5RawBox("14", null).Value);
        Assert.Equal(new T5RawBox("13", "100.00"), new T5RawBox("13", "100.00"));
        Assert.NotEqual(new T5RawBox("13", "100.00"), new T5RawBox("13", "200.00"));
    }
}
