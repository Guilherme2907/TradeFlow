namespace TradeFlow.Application.Common.Models;

public enum ErrorType
{
    Failure = 1,
    Validation = 2,
    NotFound = 3,
    Conflict = 4
}

public readonly record struct Error(string Code, string Description, ErrorType Type = ErrorType.Failure)
{
    public static Error None => new(string.Empty, string.Empty);
    public static Error NullValue => new("ERROR_NULL_VALUE", "A null value was provided.", ErrorType.Failure);

    public static Error Failure(string code, string description) => new(code, description, ErrorType.Failure);
    public static Error Validation(string code, string description) => new(code, description, ErrorType.Validation);
    public static Error NotFound(string code, string description) => new(code, description, ErrorType.NotFound);
    public static Error Conflict(string code, string description) => new(code, description, ErrorType.Conflict);
}
