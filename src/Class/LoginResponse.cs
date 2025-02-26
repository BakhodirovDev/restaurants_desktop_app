using Restaurants.CustomConverter;
using System.Text.Json.Serialization;

namespace Restaurants.Class;

public class LoginResponse
{
    public string Token { get; set; }
    public string AccessToken { get; set; }

    [JsonConverter(typeof(DateTimeOffsetConverter))]
    public DateTimeOffset AccessTokenExpireAt { get; set; }

    public string RefreshToken { get; set; }

    [JsonConverter(typeof(DateTimeOffsetConverter))]
    public DateTimeOffset RefreshTokenExpireAt { get; set; }

    public UserInfo UserInfo { get; set; }
}
