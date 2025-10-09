using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;

namespace TinkerGenie.API.Services
{
    public interface IEmailService
    {
        Task<bool> SendPasswordSetupEmailAsync(string toEmail, string firstName, string setupToken);
        Task<bool> SendPasswordResetEmailAsync(string toEmail, string firstName, string resetToken);
    }

    public class EmailService : IEmailService
    {
        private readonly ILogger<EmailService> _logger;
        private readonly IConfiguration _configuration;

        public EmailService(ILogger<EmailService> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        public async Task<bool> SendPasswordSetupEmailAsync(string toEmail, string firstName, string setupToken)
        {
            try
            {
                var smtpHost = _configuration["Email:SmtpHost"] ?? "smtp.gmail.com";
                var smtpPort = int.Parse(_configuration["Email:SmtpPort"] ?? "587");
                var smtpUsername = _configuration["Email:Username"] ?? "";
                var smtpPassword = _configuration["Email:Password"] ?? "";
                var fromEmail = _configuration["Email:FromEmail"] ?? smtpUsername;

                using var client = new SmtpClient(smtpHost, smtpPort)
                {
                    EnableSsl = true,
                    Credentials = new NetworkCredential(smtpUsername, smtpPassword)
                };

                var setupUrl = $"https://tinker.twobrain.ai/password-setup?token={setupToken}";
                
                var subject = "Welcome to TinkerGenie - Set Up Your Password";
                var body = $@"
<!DOCTYPE html>
<html>
<head>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 30px; text-align: center; border-radius: 10px 10px 0 0; }}
        .content {{ background: #f9f9f9; padding: 30px; border-radius: 0 0 10px 10px; }}
        .button {{ display: inline-block; background: #667eea; color: white; padding: 15px 30px; text-decoration: none; border-radius: 5px; margin: 20px 0; }}
        .footer {{ text-align: center; margin-top: 30px; color: #666; font-size: 14px; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>🧞‍♂️ Welcome to TinkerGenie</h1>
            <p>Your AI Leadership Mentor</p>
        </div>
        <div class='content'>
            <h2>Hello {firstName ?? "there"}!</h2>
            <p>Welcome to TinkerGenie, your personal AI leadership development platform. To get started, you'll need to set up your password.</p>
            
            <p>Click the button below to create your secure password:</p>
            
            <a href='{setupUrl}' class='button'>Set Up My Password</a>
            
            <p>Or copy and paste this link into your browser:</p>
            <p style='background: #eee; padding: 10px; border-radius: 5px; word-break: break-all;'>{setupUrl}</p>
            
            <p><strong>This link will expire in 24 hours for security reasons.</strong></p>
            
            <p>Once you've set up your password, you'll have access to:</p>
            <ul>
                <li>📚 Daily leadership prompts and reflections</li>
                <li>🔥 Burning Fires - urgent leadership challenges</li>
                <li>🎯 Tinker Level - strategic problem solving</li>
                <li>💬 Personalized AI mentoring conversations</li>
            </ul>
            
            <p>If you didn't request this account, please ignore this email.</p>
        </div>
        <div class='footer'>
            <p>© 2025 TinkerGenie - Empowering Leaders Through AI</p>
            <p>Need help? Contact us at support@tinkergenie.com</p>
        </div>
    </div>
</body>
</html>";

                var message = new MailMessage(fromEmail, toEmail, subject, body)
                {
                    IsBodyHtml = true
                };

                await client.SendMailAsync(message);
                
                _logger.LogInformation("Password setup email sent successfully to {Email}", toEmail);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send password setup email to {Email}", toEmail);
                return false;
            }
        }

        public async Task<bool> SendPasswordResetEmailAsync(string toEmail, string firstName, string resetToken)
        {
            try
            {
                var smtpHost = _configuration["Email:SmtpHost"] ?? "smtp.gmail.com";
                var smtpPort = int.Parse(_configuration["Email:SmtpPort"] ?? "587");
                var smtpUsername = _configuration["Email:Username"] ?? "";
                var smtpPassword = _configuration["Email:Password"] ?? "";
                var fromEmail = _configuration["Email:FromEmail"] ?? smtpUsername;

                using var client = new SmtpClient(smtpHost, smtpPort)
                {
                    EnableSsl = true,
                    Credentials = new NetworkCredential(smtpUsername, smtpPassword)
                };

                var resetUrl = $"https://tinker.twobrain.ai/reset-password?token={resetToken}";
                
                var subject = "Reset Your TinkerGenie Password";
                var body = $@"
<!DOCTYPE html>
<html>
<head>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 30px; text-align: center; border-radius: 10px 10px 0 0; }}
        .content {{ background: #f9f9f9; padding: 30px; border-radius: 0 0 10px 10px; }}
        .button {{ display: inline-block; background: #667eea; color: white; padding: 15px 30px; text-decoration: none; border-radius: 5px; margin: 20px 0; }}
        .warning {{ background: #fff3cd; border-left: 4px solid #ffc107; padding: 15px; margin: 20px 0; }}
        .footer {{ text-align: center; margin-top: 30px; color: #666; font-size: 14px; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>🧞‍♂️ Password Reset Request</h1>
            <p>TinkerGenie</p>
        </div>
        <div class='content'>
            <h2>Hello {firstName ?? "there"}!</h2>
            <p>We received a request to reset your password for your TinkerGenie account.</p>
            
            <p>Click the button below to reset your password:</p>
            
            <a href='{resetUrl}' class='button'>Reset My Password</a>
            
            <p>Or copy and paste this link into your browser:</p>
            <p style='background: #eee; padding: 10px; border-radius: 5px; word-break: break-all;'>{resetUrl}</p>
            
            <div class='warning'>
                <strong>⚠️ Security Notice:</strong>
                <ul style='margin: 10px 0 0 0;'>
                    <li>This link will expire in 1 hour for security reasons.</li>
                    <li>If you didn't request this password reset, please ignore this email.</li>
                    <li>Your password will not change until you click the link and set a new one.</li>
                </ul>
            </div>
            
            <p>For your security, never share this link with anyone.</p>
        </div>
        <div class='footer'>
            <p>© 2025 TinkerGenie - Empowering Leaders Through AI</p>
            <p>Need help? Contact us at support@tinkergenie.com</p>
        </div>
    </div>
</body>
</html>";

                var message = new MailMessage(fromEmail, toEmail, subject, body)
                {
                    IsBodyHtml = true
                };

                await client.SendMailAsync(message);
                
                _logger.LogInformation("Password reset email sent successfully to {Email}", toEmail);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send password reset email to {Email}", toEmail);
                return false;
            }
        }
    }
}



