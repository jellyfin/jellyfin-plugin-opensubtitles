using System;
using System.Net;
using System.Text.Json;

namespace OpenSubtitlesHandler.Models
{
    public class ApiResponse<T>
    {
        public HttpStatusCode Code { get; }
        public string Body { get; } = string.Empty;
        public T Data { get; }

        public ApiResponse(T response, HttpStatusCode statusCode)
        {
            Code = statusCode;
            Data = response;
        }

        public ApiResponse(string response, HttpStatusCode statusCode)
        {
            Code = statusCode;
            Body = response;

            if (typeof(T) == typeof(string))
            {
                Data = (T)(object)Body;
                return;
            }

            if (!Ok)
            {
                // don't bother parsing json if HTTP status code is bad
                return;
            }

            try
            {
                Data = JsonSerializer.Deserialize<T>(Body);
            }
            catch (Exception e)
            {
                throw new Exception($"Failed to parse JSON: {e.Message}\n\n{Body}");
            }
        }

        public bool Ok
        {
            get
            {
                return (int) Code < 400;
            }
        }
    }

}
