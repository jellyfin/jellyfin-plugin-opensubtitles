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

        public static void SetVersion(string version) => Util.SetVersion(version);

        public static async Task<APIResponse<ResponseObjects.LoginInfo>> LogInAsync(string username, string password, CancellationToken cancellationToken)
        {
            var body = Util.Serialize(new { username, password });
            var response = await RequestHandler.SendRequestAsync("/login", HttpMethod.Post, body, null, cancellationToken).ConfigureAwait(false);

            return new APIResponse<ResponseObjects.LoginInfo>(response);
        }

        public static async Task<bool> LogOutAsync(ResponseObjects.LoginInfo user, CancellationToken cancellationToken)
        {
            var headers = new Dictionary<string, string>
            {
                { "Authorization", user.token }
            };

            var response = await RequestHandler.SendRequestAsync("/logout", HttpMethod.Delete, null, headers, cancellationToken).ConfigureAwait(false);
            
            return new APIResponse<object>(response).IsOK();
        }

        public static async Task<APIResponse<ResponseObjects.EncapsulatedUserInfo>> GetUserInfo(ResponseObjects.LoginInfo user, CancellationToken cancellationToken)
        {
            var headers = new Dictionary<string, string>
            {
                { "Authorization", user.token }
            };

            var response = await RequestHandler.SendRequestAsync("/infos/user", HttpMethod.Get, null, headers, cancellationToken).ConfigureAwait(false);
            
            return new APIResponse<ResponseObjects.EncapsulatedUserInfo>(response);
        }

        public static async Task<APIResponse<string>> DownloadSubtitleAsync(int file_id, ResponseObjects.LoginInfo user, CancellationToken cancellationToken)
        {
            var headers = new Dictionary<string, string>
            {
                { "Authorization", user.token }
            };

            var body = Util.Serialize(new { file_id });
            var response = await RequestHandler.SendRequestAsync("/download", HttpMethod.Post, body, headers, cancellationToken).ConfigureAwait(false);

            var temp = new APIResponse<ResponseObjects.SubtitleDownloadInfo>(response);
            if (!temp.IsOK())
            {
                return null;
            }

            var info = Util.Deserialize<ResponseObjects.SubtitleDownloadInfo>(response.Item1);
            var url = info.link;

            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            var download = await RequestHandler.SendRequestAsync(url, HttpMethod.Get, null, null, cancellationToken).ConfigureAwait(true);

            return new APIResponse<string>(download);
        }

        public static async Task<APIResponse<ResponseObjects.SearchResult>> SearchSubtitlesAsync(Dictionary<string, string> options, CancellationToken cancellationToken)
        {
            var opts = System.Web.HttpUtility.ParseQueryString(string.Empty);

            foreach (var item in options)
            {
                opts.Add(item.Key, item.Value);
            }

            var url = "/subtitles?" + opts.ToString();

            var response = await RequestHandler.SendRequestAsync(url, HttpMethod.Get, null, null, cancellationToken).ConfigureAwait(false);

            return new APIResponse<ResponseObjects.SearchResult>(response);
        }
    }
}