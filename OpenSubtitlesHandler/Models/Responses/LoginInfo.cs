using System;
using System.Text;
using System.Text.Json.Serialization;

namespace OpenSubtitlesHandler.Models.Responses
{
    public class LoginInfo
    {
        private DateTime? _expirationDate = null;

        [JsonPropertyName("user")]
        public UserInfo User { get; set; }
        [JsonPropertyName("token")]
        public string Token { get; set; }

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
                part = part.PadRight(part.Length + (4 - part.Length % 4) % 4, '=');
                part = Encoding.UTF8.GetString(Convert.FromBase64String(part));

                var sec = Util.Deserialize<JWTPayload>(part).exp;

                _expirationDate = DateTimeOffset.FromUnixTimeSeconds(sec).UtcDateTime;

                return _expirationDate.Value;
            }
        }
    }
}
