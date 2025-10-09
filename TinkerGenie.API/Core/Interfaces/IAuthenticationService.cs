using TinkerGenie.API.Core.Entities;

namespace TinkerGenie.API.Core.Interfaces
{
    public interface IAuthenticationService
    {
        Task<AuthenticationResult> AuthenticateAsync(string email, string password);
        Task<AuthenticationResult> AuthenticateAsync(string email, string password, bool rememberMe);
        string GenerateJwtToken(User user);
        string GenerateJwtToken(User user, bool rememberMe);
        bool VerifyPassword(string password, string hash);
        string HashPassword(string password);
    }

    public class AuthenticationResult
    {
        public bool Success { get; set; }
        public string? Token { get; set; }
        public User? User { get; set; }
        public string? ErrorMessage { get; set; }
        public bool RequiresPasswordSetup { get; set; }
    }
}
