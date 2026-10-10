using System.Text.Json;
using System.Text.Json.Serialization;

namespace Woad.Core.Content;

/// <summary>
/// JSON settings for content files. Uses reflection rather than source generation because the
/// source generator resets init-only properties missing from the JSON to default, losing
/// defaults such as <see cref="CardDefinition.AttackRange"/> = 1.
/// </summary>
internal static class ContentJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}
