using System;
using System.Text;

namespace OpenSubtitlesHandler.Models.Responses
{
    public class LoginInfo
    {
        public UserInfo User;
        public string Token;
        public int Status;
        private DateTime? _expirationDate = null;

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
