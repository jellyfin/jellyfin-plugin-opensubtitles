using System.ComponentModel.DataAnnotations;

namespace OpenSubtitlesHandler.Models
{
    public class LoginInfoInput
    {
        [Required]
        public string Username { get; set; }
        [Required]
        public string Password { get; set; }
        [Required]
        public string ApiKey { get; set; }
    }
}
