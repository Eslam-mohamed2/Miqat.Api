using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using Miqat.Application.Common;
using Miqat.Application.Interfaces;

namespace Miqat.infrastructure.persistence.Services
{
    public class EmailService : IEmailService
    {
        private readonly EmailSettings _settings;

        public EmailService(IOptions<EmailSettings> settings)
        {
            _settings = settings.Value;
        }

        public async Task SendOtpAsync(string toEmail, string fullName, string otp)
        {
            var subject = "Your Miqat Verification Code";
            var body = $@"
                <h2>Hello {fullName}!</h2>
                <p>Your verification code is:</p>
                <h1 style='color:#4F46E5;letter-spacing:8px'>{otp}</h1>
                <p>This code expires in <strong>10 minutes</strong>.</p>
                <p>If you didn't request this, ignore this email.</p>
                <br/>
                <p>Miqat Smart Calendar Team</p>";

            await SendEmailAsync(toEmail, fullName, subject, body);
        }

        public async Task SendPasswordResetAsync(
            string toEmail, string fullName, string resetLink)
        {
            var subject = "Reset Your Miqat Password";
            var body = $@"
                <h2>Hello {fullName}!</h2>
                <p>You requested to reset your password.</p>
                <p>Click the button below to reset it:</p>
                <a href='{resetLink}' 
                   style='background:#4F46E5;color:white;padding:12px 24px;
                          border-radius:6px;text-decoration:none;display:inline-block'>
                   Reset Password
                </a>
                <p>This link expires in <strong>30 minutes</strong>.</p>
                <p>If you didn't request this, ignore this email.</p>
                <br/>
                <p>Miqat Smart Calendar Team</p>";

            await SendEmailAsync(toEmail, fullName, subject, body);
        }

        public async Task SendWelcomeAsync(string toEmail, string fullName)
        {
            var subject = "Welcome to Miqat Smart Calendar!";
            var body = $@"
                <h2>Welcome to Miqat, {fullName}!</h2>
                <p>Your account has been verified successfully.</p>
                <p>You can now log in and start managing your tasks smartly.</p>
                <br/>
                <p>Miqat Smart Calendar Team</p>";

            await SendEmailAsync(toEmail, fullName, subject, body);
        }

        private async Task SendEmailAsync(
            string toEmail, string toName, string subject, string htmlBody)
        {
            var message = new MimeMessage();

            message.From.Add(new MailboxAddress(
                _settings.FromName, _settings.FromEmail));
            message.To.Add(new MailboxAddress(toName, toEmail));
            message.Subject = subject;

            var builder = new BodyBuilder { HtmlBody = htmlBody };
            message.Body = builder.ToMessageBody();

            using var client = new SmtpClient();
            await client.ConnectAsync(
                _settings.SmtpHost,
                _settings.SmtpPort,
                SecureSocketOptions.StartTls);

            await client.AuthenticateAsync(
                _settings.SmtpUser,
                _settings.SmtpPassword);

            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }
    }
}