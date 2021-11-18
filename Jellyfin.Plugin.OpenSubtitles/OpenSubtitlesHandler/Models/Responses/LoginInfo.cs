using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.OpenSubtitles.OpenSubtitlesHandler.Models.Responses
{
    /// <summary>
    /// The login info.
    /// </summary>
    public class LoginInfo
    {
        private DateTime? _expirationDate;

        /// <summary>
        /// Gets or sets the user info.
        /// </summary>
        [JsonPropertyName("user")]
        public UserInfo? User { get; set; }

        /// <summary>
        /// Gets or sets the token.
        /// </summary>
        [JsonPropertyName("token")]
        public string? Token { get; set; }

        /// <summary>
        /// Gets the expiration date.
        /// </summary>
        [JsonIgnore]
        public DateTime ExpirationDate
        {
            get
            {
                if (_expirationDate.HasValue)
                {
                    return _expirationDate.Value;
                }

                if (string.IsNullOrWhiteSpace(Token))
                {
                    return DateTime.MinValue;
                }

                var part = Token.Split('.')[1];
                part = part.PadRight(part.Length + ((4 - (part.Length % 4)) % 4), '=');
                part = Encoding.UTF8.GetString(Convert.FromBase64String(part));

                var sec = JsonSerializer.Deserialize<JWTPayload>(part)?.Exp ?? 0;
                _expirationDate = DateTimeOffset.FromUnixTimeSeconds(sec).UtcDateTime;
                return _expirationDate.Value;
            }
        }
    }
}
