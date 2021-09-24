using System;
using System.Net;
using System.Text.Json;

namespace OpenSubtitlesHandler.Models
{
    /// <summary>
    /// The api response.
    /// </summary>
    /// <typeparam name="T">The type of response.</typeparam>
    public class ApiResponse<T>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ApiResponse{T}"/> class.
        /// </summary>
        /// <param name="response">The response.</param>
        /// <param name="statusCode">The status code.</param>
        public ApiResponse(T response, HttpStatusCode statusCode)
        {
            Code = statusCode;
            Data = response;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ApiResponse{T}"/> class.
        /// </summary>
        /// <param name="response">The response string.</param>
        /// <param name="statusCode">The status code.</param>
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
                Data = JsonSerializer.Deserialize<T>(Body) ?? default;
            }
            catch (Exception ex)
            {
                throw new JsonException($"Failed to parse JSON: \n{(string.IsNullOrWhiteSpace(Body) ? @"""" : Body)}", ex);
            }
        }

        /// <summary>
        /// Gets the status code.
        /// </summary>
        public HttpStatusCode Code { get; }

        /// <summary>
        /// Gets the response body.
        /// </summary>
        public string Body { get; } = string.Empty;

        /// <summary>
        /// Gets the deserialized data.
        /// </summary>
        public T? Data { get; }

        /// <summary>
        /// Gets a value indicating whether the request was successful.
        /// </summary>
        public bool Ok => (int)Code < 400;
    }
}
