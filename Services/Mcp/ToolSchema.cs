using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KillerMCP.Services.Mcp;

/// <summary>
/// Checks tool arguments against the input schema the tool advertises, before the tool runs. It covers the
/// JSON Schema keywords KillerMCP's tools use: type, enum, const, string lengths and patterns, number ranges,
/// array items and counts, object properties, required, dependentRequired, additionalProperties and property
/// counts, and anyOf and oneOf. A keyword it does not know is ignored, so schemas must not rely on others.
/// </summary>
internal static class ToolSchema
{
    private static readonly TimeSpan PatternTimeout = TimeSpan.FromMilliseconds(250);

    /// <summary>Null when the value matches, otherwise a short reason naming the first property that does not.</summary>
    public static string? Validate(JsonElement value, JsonElement schema) => Check(value, schema, "arguments");

    private static string? Check(JsonElement value, JsonElement schema, string where)
    {
        if (schema.ValueKind == JsonValueKind.False) return $"{where} is not allowed";
        if (schema.ValueKind != JsonValueKind.Object) return null;

        if (schema.TryGetProperty("type", out var type) && !TypeMatches(value, type))
            return $"{where} must be {Describe(type)}";
        if (schema.TryGetProperty("enum", out var choices) && choices.ValueKind == JsonValueKind.Array
            && !choices.EnumerateArray().Any(choice => JsonElement.DeepEquals(choice, value)))
            return $"{where} must be one of {string.Join(", ", choices.EnumerateArray().Select(choice => choice.GetRawText()))}";
        if (schema.TryGetProperty("const", out var constant) && !JsonElement.DeepEquals(constant, value))
            return $"{where} must be {constant.GetRawText()}";

        var problem = value.ValueKind switch
        {
            JsonValueKind.String => CheckString(value.GetString()!, schema, where),
            JsonValueKind.Number => CheckNumber(value, schema, where),
            JsonValueKind.Array => CheckArray(value, schema, where),
            JsonValueKind.Object => CheckObject(value, schema, where),
            _ => null,
        };
        if (problem is not null) return problem;

        if (schema.TryGetProperty("anyOf", out var anyOf) && anyOf.ValueKind == JsonValueKind.Array
            && !anyOf.EnumerateArray().Any(option => Check(value, option, where) is null))
            return $"{where} does not match any allowed form";
        if (schema.TryGetProperty("oneOf", out var oneOf) && oneOf.ValueKind == JsonValueKind.Array
            && oneOf.EnumerateArray().Count(option => Check(value, option, where) is null) != 1)
            return $"{where} must match exactly one allowed form";
        return null;
    }

    private static bool TypeMatches(JsonElement value, JsonElement type) => type.ValueKind switch
    {
        JsonValueKind.String => IsType(value, type.GetString()!),
        JsonValueKind.Array => type.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.String && IsType(value, item.GetString()!)),
        _ => true,
    };

    private static bool IsType(JsonElement value, string type) => type switch
    {
        "string" => value.ValueKind == JsonValueKind.String,
        // Tools read integers as whole 64-bit values, so 1.5 and 1e300 are not integers here.
        "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
        "number" => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number),
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "object" => value.ValueKind == JsonValueKind.Object,
        "array" => value.ValueKind == JsonValueKind.Array,
        "null" => value.ValueKind == JsonValueKind.Null,
        _ => false,
    };

    private static string Describe(JsonElement type) => type.ValueKind == JsonValueKind.Array
        ? string.Join(" or ", type.EnumerateArray().Select(item => Article(item.GetString())))
        : Article(type.GetString());

    private static string Article(string? type) => type switch
    {
        "integer" or "object" or "array" => "an " + type,
        "null" => "null",
        _ => "a " + type,
    };

    private static string? CheckString(string text, JsonElement schema, string where)
    {
        // JSON Schema counts characters, so a surrogate pair is one.
        var length = text.EnumerateRunes().Count();
        if (Limit(schema, "minLength") is double minimum && length < minimum) return $"{where} must be at least {minimum} characters";
        if (Limit(schema, "maxLength") is double maximum && length > maximum) return $"{where} must be at most {maximum} characters";
        if (schema.TryGetProperty("pattern", out var pattern) && pattern.ValueKind == JsonValueKind.String && !MatchesPattern(text, pattern.GetString()!))
            return $"{where} has an invalid format";
        return null;
    }

    // JSON Schema patterns are ECMA-262 expressions. ECMAScript mode keeps \d and \w to ASCII, and since .NET's $ also
    // matches before a final line break, a pattern that ends in $ must match up to the very end of the value.
    private static bool MatchesPattern(string text, string pattern)
    {
        try
        {
            var match = Regex.Match(text, pattern, RegexOptions.ECMAScript, PatternTimeout);
            return match.Success && (!pattern.EndsWith('$') || match.Index + match.Length == text.Length);
        }
        catch (Exception exception) when (exception is RegexMatchTimeoutException or ArgumentException)
        {
            return false;
        }
    }

    private static string? CheckNumber(JsonElement value, JsonElement schema, string where)
    {
        if (!value.TryGetDouble(out var number) || !double.IsFinite(number)) return $"{where} must be a finite number";
        if (Limit(schema, "minimum") is double minimum && number < minimum) return $"{where} must be at least {Format(minimum)}";
        if (Limit(schema, "maximum") is double maximum && number > maximum) return $"{where} must be at most {Format(maximum)}";
        if (Limit(schema, "exclusiveMinimum") is double above && number <= above) return $"{where} must be more than {Format(above)}";
        if (Limit(schema, "exclusiveMaximum") is double below && number >= below) return $"{where} must be less than {Format(below)}";
        return null;
    }

    private static string? CheckArray(JsonElement value, JsonElement schema, string where)
    {
        var count = value.GetArrayLength();
        if (Limit(schema, "minItems") is double minimum && count < minimum) return $"{where} needs at least {minimum} items";
        if (Limit(schema, "maxItems") is double maximum && count > maximum) return $"{where} allows at most {maximum} items";
        var index = 0;
        if (schema.TryGetProperty("items", out var items))
            foreach (var item in value.EnumerateArray())
                if (Check(item, items, $"{where}[{index++}]") is { } problem) return problem;
        if (schema.TryGetProperty("uniqueItems", out var unique) && unique.ValueKind == JsonValueKind.True)
        {
            var seen = value.EnumerateArray().ToArray();
            for (var i = 0; i < seen.Length; i++)
                for (var j = i + 1; j < seen.Length; j++)
                    if (JsonElement.DeepEquals(seen[i], seen[j])) return $"{where} must not repeat an item";
        }
        return null;
    }

    private static string? CheckObject(JsonElement value, JsonElement schema, string where)
    {
        var count = 0;
        foreach (var _ in value.EnumerateObject()) count++;
        if (Limit(schema, "minProperties") is double minimum && count < minimum) return $"{where} needs at least {minimum} properties";
        if (Limit(schema, "maxProperties") is double maximum && count > maximum) return $"{where} allows at most {maximum} properties";

        var properties = schema.TryGetProperty("properties", out var declared) && declared.ValueKind == JsonValueKind.Object ? declared : default;
        if (schema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array)
            foreach (var name in required.EnumerateArray())
                if (name.ValueKind == JsonValueKind.String && !value.TryGetProperty(name.GetString()!, out _))
                    return $"{Child(where, name.GetString()!)} is required";
        if (schema.TryGetProperty("dependentRequired", out var dependencies) && dependencies.ValueKind == JsonValueKind.Object)
            foreach (var dependency in dependencies.EnumerateObject())
                if (value.TryGetProperty(dependency.Name, out _) && dependency.Value.ValueKind == JsonValueKind.Array)
                    foreach (var name in dependency.Value.EnumerateArray())
                        if (name.ValueKind == JsonValueKind.String && !value.TryGetProperty(name.GetString()!, out _))
                            return $"{Child(where, dependency.Name)} requires {name.GetString()}";

        var additional = schema.TryGetProperty("additionalProperties", out var extra) ? extra : default;
        foreach (var property in value.EnumerateObject())
        {
            var path = Child(where, property.Name);
            if (properties.ValueKind == JsonValueKind.Object && properties.TryGetProperty(property.Name, out var propertySchema))
            {
                if (Check(property.Value, propertySchema, path) is { } problem) return problem;
            }
            else if (additional.ValueKind == JsonValueKind.False)
            {
                return $"{path} is not a known property";
            }
            else if (additional.ValueKind == JsonValueKind.Object && Check(property.Value, additional, path) is { } problem)
            {
                return problem;
            }
        }
        return null;
    }

    private static string Child(string where, string name) => where == "arguments" ? name : where + "." + name;

    private static double? Limit(JsonElement schema, string name) =>
        schema.TryGetProperty(name, out var limit) && limit.ValueKind == JsonValueKind.Number ? limit.GetDouble() : null;

    private static string Format(double number) => number.ToString(CultureInfo.InvariantCulture);
}
