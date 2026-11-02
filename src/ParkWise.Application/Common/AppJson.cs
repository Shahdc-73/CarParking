using System.Text.Json;
using System.Text.Json.Serialization;

namespace ParkWise.Application.Common;

/// <summary>JSON used for data we store ourselves (receipt breakdowns, audit snapshots): camelCase, enums as strings.</summary>
public static class AppJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
