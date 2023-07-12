using System.Text.Json.Serialization;

namespace WebApi
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum DocumentType
    {
        Excel,
        Csv
    }
}
