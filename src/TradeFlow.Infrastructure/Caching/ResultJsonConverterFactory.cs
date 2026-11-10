using System.Text.Json;
using System.Text.Json.Serialization;
using TradeFlow.Application.Common.Models;

namespace TradeFlow.Infrastructure.Caching;

/// <summary>
/// Custom <see cref="JsonConverterFactory"/> for <see cref="Result{TValue}"/> that unwraps successful
/// results when writing to Redis (storing clean domain/DTO JSON without the Result envelope)
/// and re-wraps them via <see cref="Result.Success{TValue}(TValue)"/> when reading from Redis.
/// Keeps <see cref="Result{TValue}"/> in the Application layer 100% pure and free of serialization attributes.
/// </summary>
public sealed class ResultJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType &&
        typeToConvert.GetGenericTypeDefinition() == typeof(Result<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var valueType = typeToConvert.GetGenericArguments()[0];
        var converterType = typeof(ResultJsonConverter<>).MakeGenericType(valueType);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }

    private sealed class ResultJsonConverter<TValue> : JsonConverter<Result<TValue>>
    {
        public override Result<TValue>? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            var value = JsonSerializer.Deserialize<TValue>(ref reader, options);
            return value is not null
                ? Result.Success(value)
                : Result.Failure<TValue>(Error.NullValue);
        }

        public override void Write(
            Utf8JsonWriter writer,
            Result<TValue> value,
            JsonSerializerOptions options)
        {
            if (value.IsFailure)
            {
                throw new InvalidOperationException("Failure results must not be serialized into the distributed cache.");
            }

            JsonSerializer.Serialize(writer, value.Value, options);
        }
    }
}
