using System;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OpenSubtitlesHandler;

namespace Jellyfin.Plugin.KodiSyncQueue.API
{
    /// <summary>
    /// The open subtitles plugin controller.
    /// </summary>
    [ApiController]
    [Produces(MediaTypeNames.Application.Json)]
    public class OpenSubtitlesController : ControllerBase
    {
        /// <summary>
        /// Validates login info.
        /// </summary>
        /// <remarks>
        /// Accepts plugin configuration as JSON body.
        /// </remarks>
        /// <response code="200">Login info valid.</response>
        /// <response code="400">Login info is missing data.</response>
        /// <response code="401">Login info not valid.</response>
        /// <param name="body">The request body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>
        /// An <see cref="NoContentResult"/> if the login info is valid, a <see cref="BadRequestResult"/> if the request body missing is data
        /// or <see cref="UnauthorizedResult"/> if the login info is not valid.
        /// </returns>
        [HttpPost("Jellyfin.Plugin.OpenSubtitles/ValidateLoginInfo")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult> ValidateLoginInfo([FromBody] LoginInfo body, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(body.Username))
            {
                return BadRequest(new { Message = "No Username in body" });
            }

            if (string.IsNullOrWhiteSpace(body.Password))
            {
                return BadRequest(new { Message = "No Password in body" });
            }

            if (string.IsNullOrWhiteSpace(body.ApiKey))
            {
                return BadRequest(new { Message = "No ApiKey in body" });
            }

            var response = await OpenSubtitlesHandler.OpenSubtitles.LogInAsync(body.Username, body.Password, body.ApiKey, token).ConfigureAwait(false);

            if (!response.OK)
            {
                var msg = response.code + " - " + (response.body.Length < 150 ? response.body : string.Empty);

                if (response.body.Contains("message\":", StringComparison.OrdinalIgnoreCase))
                {
                    var err = Util.Deserialize<ErrorResponse>(response.body);
                    msg = err.message == "You cannot consume this service" ? "Invalid API key provided" : err.message;
                }

                return Unauthorized(new { Message = msg });
            }

            await OpenSubtitlesHandler.OpenSubtitles.LogOutAsync(response.data, body.ApiKey, token).ConfigureAwait(false);

            return Ok(new { Downloads = response.data.user.allowed_downloads });
        }
    }
}