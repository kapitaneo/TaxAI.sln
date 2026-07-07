using TaxLogicEngine.Models;

namespace TaxLogicEngine.Tests.Models;

public class ParseResultTests
{
    [Fact]
    public void Success_ExposesValue_AndHasNoErrors()
    {
        var result = ParseResult<string>.Success("ok");

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal("ok", result.Value);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Success_WithNullValue_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ParseResult<string>.Success(null!));
    }

    [Fact]
    public void Failure_ExposesErrors_AndHasNoValue()
    {
        var errors = new[]
        {
            new ValidationError("TaxYear", "Tax year is required."),
            new ValidationError("RecipientType", "Recipient type is out of range."),
        };

        var result = ParseResult<string>.Failure(errors);

        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        Assert.Equal(errors, result.Errors);
    }

    [Fact]
    public void Failure_WithParams_CollectsAllErrors()
    {
        var result = ParseResult<string>.Failure(
            new ValidationError("Box13", "Not a number."),
            new ValidationError("Box24", "Must be >= 0."));

        Assert.True(result.IsFailure);
        Assert.Equal(2, result.Errors.Count);
    }

    [Fact]
    public void Failure_WithNoErrors_Throws()
    {
        Assert.Throws<ArgumentException>(() => ParseResult<string>.Failure(Array.Empty<ValidationError>()));
    }

    [Fact]
    public void Failure_WithNullErrors_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ParseResult<string>.Failure((IReadOnlyList<ValidationError>)null!));
    }

    [Fact]
    public void ValidationError_HasValueEquality()
    {
        var a = new ValidationError("TaxYear", "Tax year is required.");
        var b = new ValidationError("TaxYear", "Tax year is required.");

        Assert.Equal(a, b);
    }
}
