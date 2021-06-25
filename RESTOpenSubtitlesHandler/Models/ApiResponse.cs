using System;
using System.Collections.Generic;
using System.Net;

namespace RESTOpenSubtitlesHandler.Models
{
    public class ApiResponse<T>
    {
        public HttpStatusCode Code { get; }
        public string Body { get; }= string.Empty;
        public int Remaining { get; }
        public int Reset { get; }
        public Dictionary<string, string> Headers { get; }
        public T Data { get; }

        public ApiResponse((T response, (int remaining, int reset) limits, Dictionary<string, string> headers, HttpStatusCode statusCode) input)
        {
            Headers = input.headers;
            Remaining = input.limits.remaining;
            Reset = input.limits.reset;
            Code = input.statusCode;
            Data = input.response;
        }

        public ApiResponse((string response, (int remaining, int reset) limits, Dictionary<string, string> headers, HttpStatusCode statusCode) input)
        {
            Body = input.response;
            Headers = input.headers;
            Remaining = input.limits.remaining;
            Reset = input.limits.reset;
            Code = input.statusCode;

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
                Data = Util.Deserialize<T>(Body);
            }
            catch (Exception e)
            {
                throw new Exception("Failed to parse JSON: " + e.Message + "\n\n" + Body);
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
