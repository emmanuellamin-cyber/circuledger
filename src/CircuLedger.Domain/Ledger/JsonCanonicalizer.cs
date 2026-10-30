using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CircuLedger.Domain.Ledger;

/// <summary>
/// Produces a stable JSON representation (ordinal-sorted keys, no whitespace, normalised numbers).
/// PostgreSQL jsonb re-orders keys and re-formats whitespace, so both the writer and the verifier hash
/// the canonical form, not the raw text that comes back from the database.
/// </summary>
public static class JsonCanonicalizer
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = false
    };

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public static string Serialize<T>(T value) => Canonicalize(JsonSerializer.Serialize(value, SerializerOptions));

    public static string Canonicalize(string json)
    {
        var node = JsonNode.Parse(json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            Write(node, writer);
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void Write(JsonNode? node, Utf8JsonWriter writer)
    {
        switch (node)
        {
            case null:
                writer.WriteNullValue();
                break;
            case JsonObject obj:
                writer.WriteStartObject();
                foreach (var (key, value) in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(key);
                    Write(value, writer);
                }
                writer.WriteEndObject();
                break;
            case JsonArray array:
                writer.WriteStartArray();
                foreach (var item in array) Write(item, writer);
                writer.WriteEndArray();
                break;
            case JsonValue value:
                var element = value.GetValue<JsonElement>();
                if (element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out var number))
                    writer.WriteRawValue(number.ToString(CultureInfo.InvariantCulture), skipInputValidation: true);
                else
                    element.WriteTo(writer);
                break;
        }
    }
}
