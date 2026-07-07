namespace TaxLogicEngine.Models;

/// <summary>
/// The outcome of a parse/validation operation. Fail-closed by construction:
/// a successful result carries a non-null <see cref="Value"/> and no errors;
/// a failed result carries at least one <see cref="ValidationError"/> and no
/// value. There is no partial/best-effort state, so invalid data cannot leak
/// downstream.
/// </summary>
/// <typeparam name="T">The parsed value type produced on success.</typeparam>
public sealed class ParseResult<T>
{
    private ParseResult(bool isSuccess, T? value, IReadOnlyList<ValidationError> errors)
    {
        IsSuccess = isSuccess;
        Value = value;
        Errors = errors;
    }

    /// <summary>True when parsing succeeded and <see cref="Value"/> is populated.</summary>
    public bool IsSuccess { get; }

    /// <summary>True when parsing failed and <see cref="Errors"/> is populated.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// The parsed value on success; <c>default</c> (null for reference types) on
    /// failure. Guard access with <see cref="IsSuccess"/>.
    /// </summary>
    public T? Value { get; }

    /// <summary>
    /// The validation errors on failure; empty on success. Never null.
    /// </summary>
    public IReadOnlyList<ValidationError> Errors { get; }

    /// <summary>Creates a successful result wrapping <paramref name="value"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public static ParseResult<T> Success(T value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        return new ParseResult<T>(true, value, Array.Empty<ValidationError>());
    }

    /// <summary>Creates a failed result carrying <paramref name="errors"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="errors"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="errors"/> is empty.</exception>
    public static ParseResult<T> Failure(IReadOnlyList<ValidationError> errors)
    {
        if (errors is null)
        {
            throw new ArgumentNullException(nameof(errors));
        }

        if (errors.Count == 0)
        {
            throw new ArgumentException("A failed result must contain at least one error.", nameof(errors));
        }

        return new ParseResult<T>(false, default, errors);
    }

    /// <summary>Convenience overload for creating a failed result from error params.</summary>
    public static ParseResult<T> Failure(params ValidationError[] errors) =>
        Failure((IReadOnlyList<ValidationError>)errors);
}
