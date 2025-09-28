using System.ComponentModel.DataAnnotations;

namespace TinkerGenie.API.Presentation.Models
{
    /// <summary>
    /// Clean DTOs - NO dependencies on anything else
    /// </summary>
    public class LoginRequest
    {
        // Support both frontend formats: Email/email and Username/username
        public string? Email { get; set; }
        public string? Username { get; set; }

        [Required]
        [MinLength(8)]
        public string Password { get; set; } = "";
        // Remember Me functionality
        public bool RememberMe { get; set; } = false;

        // Helper property to get the email/username regardless of format
        public string GetEmailOrUsername()
        {
            return Email ?? Username ?? "";
        }

        // Validation method
        public bool IsValid()
        {
            var emailOrUsername = GetEmailOrUsername();
            return !string.IsNullOrEmpty(emailOrUsername) && 
                   !string.IsNullOrEmpty(Password) && 
                   Password.Length >= 8;
        }
    }

    public class LoginResponse
    {
        public bool Success { get; set; }
        public string? Token { get; set; }
        public UserDto? User { get; set; }
        public string? Message { get; set; }
        public bool RequiresPasswordSetup { get; set; }
        public string? Email { get; set; }
    }

    public class UserDto
    {
        public Guid Id { get; set; }
        public string Email { get; set; } = "";
        public string? Username { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string Role { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}

