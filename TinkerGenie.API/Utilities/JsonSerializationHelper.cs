using System.Text.Json;

namespace TinkerGenie.API.Utilities
{
    public static class JsonSerializationHelper
    {
        private static readonly JsonSerializerOptions DefaultOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public static T? Deserialize<T>(string json, JsonSerializerOptions? options = null)
        {
            return JsonSerializer.Deserialize<T>(json, options ?? DefaultOptions);
        }

        public static string Serialize<T>(T value, JsonSerializerOptions? options = null)
        {
            return JsonSerializer.Serialize(value, options ?? DefaultOptions);
        }
    }
}
