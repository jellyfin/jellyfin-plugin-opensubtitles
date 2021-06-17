using System;
using System.Collections.Generic;
using System.Net;
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
            
            return new APIResponse<object>(response).OK;
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

        public static async Task<APIResponse<ResponseObjects.SubtitleDownloadInfo>> GetubtitleLinkAsync(int file_id, ResponseObjects.LoginInfo user, CancellationToken cancellationToken)
        {
            var headers = new Dictionary<string, string>
            {
                { "Authorization", user.token }
            };

            var body = Util.Serialize(new { file_id });
            var response = await RequestHandler.SendRequestAsync("/download", HttpMethod.Post, body, headers, cancellationToken).ConfigureAwait(false);

            return new APIResponse<ResponseObjects.SubtitleDownloadInfo>(response);
        }

        public static async Task<APIResponse<string>> DownloadSubtitleAsync(string url, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            var download = await RequestHandler.SendRequestAsync(url, HttpMethod.Get, null, null, cancellationToken).ConfigureAwait(true);

            return new APIResponse<string>(download);
        }

        public static async Task<APIResponse<List<ResponseObjects.Data>>> SearchSubtitlesAsync(Dictionary<string, string> options, CancellationToken cancellationToken)
        {
            var opts = System.Web.HttpUtility.ParseQueryString(string.Empty);

            foreach (var item in options)
            {
                opts.Add(item.Key, item.Value);
            }

            var max = -1;
            var current = 0;

            List<ResponseObjects.Data> final = new();
            APIResponse<ResponseObjects.SearchResult> last;

            do {
                opts.Set("page", current.ToString());
                
                var temp = await RequestHandler.SendRequestAsync("/subtitles?" + opts.ToString(), HttpMethod.Get, null, null, cancellationToken).ConfigureAwait(false);
                
                last = new APIResponse<ResponseObjects.SearchResult>(temp);

                if (last.data.total_pages == 0)
                {
                    return new APIResponse<List<ResponseObjects.Data>>((final, temp.Item2, temp.Item3, temp.Item4));
                }

                if (max == -1)
                {
                    max = last.data.total_pages;
                }

                current = int.Parse(last.data.page) + 1;

                final.AddRange(last.data.data);
            }
            while (current < max && last.data.data.Count == 100);

            return new APIResponse<List<ResponseObjects.Data>>((final, (last.remaining, last.reset), last.headers, (HttpStatusCode)last.code));
        }
    }
}