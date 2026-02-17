using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RomMbox.Models.Romm
{
    /// <summary>
    /// Converts JSON string or numeric tokens into a string representation.
    /// RomM sometimes returns numeric IDs that the client models store as strings.
    /// </summary>
    internal sealed class FlexibleStringJsonConverter : JsonConverter<string>
    {
        /// <summary>
        /// Reads a JSON token and returns a string value when possible.
        /// </summary>
        /// <param name="reader">Reader positioned at the token to parse.</param>
        /// <param name="typeToConvert">The target type (string).</param>
        /// <param name="options">Serialization options.</param>
        /// <returns>String representation of the token, or <c>null</c> for JSON null.</returns>
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                return reader.GetString();
            }

            if (reader.TokenType == JsonTokenType.Number)
            {
                if (reader.TryGetInt64(out var longValue))
                {
                    return longValue.ToString();
                }

                if (reader.TryGetDouble(out var doubleValue))
                {
                    return doubleValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
            }

            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            throw new JsonException("Unsupported token for string conversion.");
        }

        /// <summary>
        /// Writes the string value back as a JSON string token.
        /// </summary>
        /// <param name="writer">Writer used to emit JSON.</param>
        /// <param name="value">The string value to write.</param>
        /// <param name="options">Serialization options.</param>
        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value);
        }
    }
}
