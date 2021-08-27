using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using OpenSubtitlesHandler.Models;
using OpenSubtitlesHandler.Models.Responses;

namespace OpenSubtitlesHandler
{
    public static class OpenSubtitles
    {
        public static async Task<ApiResponse<LoginInfo>> LogInAsync(string username, string password, string apiKey, CancellationToken cancellationToken)
        {
            var body = new { username, password };
            var response = await RequestHandler.SendRequestAsync("/login", HttpMethod.Post, body, null, apiKey, cancellationToken).ConfigureAwait(false);

            return new ApiResponse<LoginInfo>(response.response, response.statusCode);
        }

        public static async Task<bool> LogOutAsync(LoginInfo user, string apiKey, CancellationToken cancellationToken)
        {
            var headers = new Dictionary<string, string>
            {
                { "Authorization", user.Token }
            };

            var response = await RequestHandler.SendRequestAsync("/logout", HttpMethod.Delete, null, headers, apiKey, cancellationToken).ConfigureAwait(false);

            return new ApiResponse<object>(response.response, response.statusCode).Ok;
        }

        public static async Task<ApiResponse<EncapsulatedUserInfo>> GetUserInfo(LoginInfo user, string apiKey, CancellationToken cancellationToken)
        {
            var headers = new Dictionary<string, string> { { "Authorization", user.Token } };

            var response = await RequestHandler.SendRequestAsync("/infos/user", HttpMethod.Get, null, headers, apiKey, cancellationToken).ConfigureAwait(false);

            return new ApiResponse<EncapsulatedUserInfo>(response.response, response.statusCode);
        }

        public static async Task<ApiResponse<SubtitleDownloadInfo>> GetSubtitleLinkAsync(int file, LoginInfo user, string apiKey, CancellationToken cancellationToken)
        {
            var headers = new Dictionary<string, string> { { "Authorization", user.Token } };

            var body = new { file_id = file };
            var response = await RequestHandler.SendRequestAsync("/download", HttpMethod.Post, body, headers, apiKey, cancellationToken).ConfigureAwait(false);

            return new ApiResponse<SubtitleDownloadInfo>(response.response, response.statusCode);
        }

        public static async Task<ApiResponse<string>> DownloadSubtitleAsync(string url, CancellationToken cancellationToken)
        {
            var response = await RequestHandler.SendRequestAsync(url, HttpMethod.Get, null, null, null, cancellationToken).ConfigureAwait(false);

            return new ApiResponse<string>(response.response, response.statusCode);
        }

        public static async Task<ApiResponse<List<Data>>> SearchSubtitlesAsync(Dictionary<string, string> options, string apiKey, CancellationToken cancellationToken)
        {
            var opts = System.Web.HttpUtility.ParseQueryString(string.Empty);

            foreach (var (key, value) in options.OrderBy(x => x.Key))
            {
                opts.Add(key, value);
            }

            var max = -1;
            var current = 0;

            List<Data> final = new();
            ApiResponse<SearchResult> last;

            do
            {
                opts.Set("page", current.ToString());

                var response = await RequestHandler.SendRequestAsync($"/subtitles?{opts}", HttpMethod.Get, null, null, apiKey, cancellationToken).ConfigureAwait(false);

                last = new ApiResponse<SearchResult>(response.response, response.statusCode);

                if (!last.Ok)
                {
                    break;
                }

                if (last.Data.TotalPages == 0)
                {
                    return new ApiResponse<List<Data>>(final, response.statusCode);
                }

                if (max == -1)
                {
                    max = last.Data.TotalPages;
                }

                current = last.Data.Page + 1;

                final.AddRange(last.Data.Data);
            } while (current < max && last.Data.Data.Count == 100);

            return new ApiResponse<List<Data>>(final, last.Code);
        }
    }
}
