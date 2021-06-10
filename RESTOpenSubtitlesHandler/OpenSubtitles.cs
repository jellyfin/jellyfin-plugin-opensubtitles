using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace RESTOpenSubtitlesHandler {
    public static class OpenSubtitles
    {
        public static void SetToken(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Missing param", nameof(key));
            }
            RequestHandler.SetApiKey(key);
        }

        public static async Task<LoginInfo> LogInAsync(string username, string password, string language, CancellationToken cancellationToken)
        {
            var body = Util.Serialize(new { username, password });
            var response = await RequestHandler.SendRequestAsync("/login", HttpMethod.Post, body, null, cancellationToken).ConfigureAwait(false);

            if (!Util.IsOKCode(response.Item2))
            {
                return null;
            }

            return Util.Deserialize<LoginInfo>(response.Item1);
        }

        public static async Task<bool> LogOutAsync(LoginInfo user, CancellationToken cancellationToken)
        {
            var headers = new Dictionary<string, string>
            {
                { "Authorization", user.token }
            };

            var response = await RequestHandler.SendRequestAsync("/logout", HttpMethod.Delete, null, headers, cancellationToken).ConfigureAwait(false);

            return Util.IsOKCode(response.Item2);
        }

        public static async Task<string> DownloadSubtitle(int file_id, LoginInfo user, CancellationToken cancellationToken)
        {
            var headers = new Dictionary<string, string>
            {
                { "Authorization", user.token }
            };

            var body = Util.Serialize(new { file_id });
            var response = await RequestHandler.SendRequestAsync("/download", HttpMethod.Post, body, headers, cancellationToken).ConfigureAwait(false);

            if (!Util.IsOKCode(response.Item2))
            {
                return null;
            }

            var info = Util.Deserialize<SubtitleDownloadInfo>(response.Item1);
            var url = info.link;

            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            var download = await Util.SendRequestAsync(url, HttpMethod.Get, null, null, cancellationToken).ConfigureAwait(false);

            return download.Item1;
        }

        public static async Task<SearchResult> SearchSubtitlesAsync(Dictionary<string, string> options, CancellationToken cancellationToken)
        {
            var opts = System.Web.HttpUtility.ParseQueryString(string.Empty);

            foreach (var item in options)
            {
                opts.Add(item.Key, item.Value);
            }

            var url = "/subtitles?" + opts.ToString();

            var response = await RequestHandler.SendRequestAsync(url, HttpMethod.Get, null, null, cancellationToken).ConfigureAwait(false);

            if (!Util.IsOKCode(response.Item2))
            {
                return null;
            }

            return Util.Deserialize<SearchResult>(response.Item1);
        }
    }
}