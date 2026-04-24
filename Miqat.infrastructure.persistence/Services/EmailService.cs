using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Miqat.Application.Common;
using Miqat.Application.Interfaces;
using sib_api_v3_sdk.Api;
using sib_api_v3_sdk.Client;
using sib_api_v3_sdk.Model;
using MimeKit;
using MailKit.Security;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Logging;
using Task = System.Threading.Tasks.Task;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Miqat.infrastructure.persistence.Services
{
    public class EmailService : IEmailService
    {
        private readonly EmailSettings _settings;
        private readonly string _primaryColor = "#4F46E5"; // Deep Indigo (Modern SaaS Look)
        private readonly string _backgroundColor = "#F9FAFB";
        private readonly ILogger<EmailService> _logger;

        public EmailService(IOptions<EmailSettings> settings, ILogger<EmailService> logger)
        {
            _settings = settings.Value;
            _logger = logger;

            // Configure Brevo API key only when provided.
            if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
            {
                Configuration.Default.ApiKey["api-key"] = _settings.ApiKey;
            }
        }

        public async Task SendOtpAsync(string toEmail, string fullName, string otp)
        {
            var subject = $"{otp} is your Miqat verification code";
            var contentHtml = $@"
                <p style='margin-bottom: 24px; font-size: 16px; color: #374151;'>To complete your sign-in, please use the verification code below:</p>
                <div style='background: #F3F4F6; border-radius: 12px; padding: 32px; text-align: center; margin: 24px 0;'>
                    <span style='font-family: monospace; font-size: 36px; font-weight: 700; letter-spacing: 12px; color: #111827;'>{otp}</span>
                </div>
                <p style='font-size: 14px; color: #6B7280;'>This code will expire in 10 minutes. If you didn't request this code, you can safely ignore this email.</p>";

            var html = GetBaseTemplate(fullName, contentHtml, "Your Miqat verification code is inside.");
            var text = GetPlainTextTemplate(fullName, $"Use this code to verify your account: {otp} (expires in 10 minutes)");

            await SendEmailAsync(toEmail, fullName, subject, html, text);
        }

        public async Task SendWelcomeAsync(string toEmail, string fullName)
        {
            var subject = "Welcome to Miqat! ✨";
            var contentHtml = $@"
                <p style='font-size: 16px; color: #374151;'>We're thrilled to have you here! Your account is now verified and ready to go.</p>
                <div style='text-align: center; margin: 32px 0;'>
                    <a href='https://miqat.vercel.app' style='background: {_primaryColor}; color: #ffffff; padding: 14px 28px; border-radius: 8px; text-decoration: none; font-weight: 600; display: inline-block;'>Go to Dashboard</a>
                </div>
                <p style='font-size: 16px; color: #374151;'>Start organizing your life with Miqat's smart calendar today.</p>";

            var html = GetBaseTemplate(fullName, contentHtml, "Welcome to Miqat — get started with your smart calendar.");
            var text = GetPlainTextTemplate(fullName, "Welcome to Miqat! Visit https://miqat.vercel.app to get started.");

            await SendEmailAsync(toEmail, fullName, subject, html, text);
        }

        public async Task SendPasswordResetOtpAsync(string toEmail, string fullName, string otp)
        {
            var subject = "Reset your Miqat password";
            var contentHtml = $@"
                <p style='font-size: 16px; color: #374151;'>We received a request to reset your password. Use this code to proceed:</p>
                <div style='background: #FEE2E2; border-radius: 12px; padding: 32px; text-align: center; margin: 24px 0;'>
                    <span style='font-family: monospace; font-size: 36px; font-weight: 700; letter-spacing: 12px; color: #991B1B;'>{otp}</span>
                </div>
                <p style='font-size: 14px; color: #6B7280;'>If you didn't request a password reset, please contact our support team immediately.</p>";

            var html = GetBaseTemplate(fullName, contentHtml, "Password reset instructions from Miqat.");
            var text = GetPlainTextTemplate(fullName, $"Use this code to reset your password: {otp} (valid for 10 minutes). If you did not request this, ignore this email.");

            await SendEmailAsync(toEmail, fullName, subject, html, text);
        }

        public async Task SendPasswordResetAsync(string toEmail, string fullName, string resetLink)
        {
            var subject = "Action Required: Reset your password";
            var contentHtml = $@"
                <p style='font-size: 16px; color: #374151;'>Please click the button below to reset your password. This link is valid for 30 minutes.</p>
                <div style='text-align: center; margin: 32px 0;'>
                    <a href='{resetLink}' style='background: #111827; color: #ffffff; padding: 14px 28px; border-radius: 8px; text-decoration: none; font-weight: 600; display: inline-block;'>Reset Password</a>
                </div>";

            var html = GetBaseTemplate(fullName, contentHtml, "Reset your Miqat password — link inside.");
            var text = GetPlainTextTemplate(fullName, $"Reset your password using this link: {resetLink} (valid for 30 minutes)");

            await SendEmailAsync(toEmail, fullName, subject, html, text);
        }

        // ─── Private Helper Methods ───

        private string GetBaseTemplate(string name, string content, string preheader = "")
        {
            // A cleaner, responsive HTML template with preheader and brand header
            return $@"
            <!doctype html>
            <html>
            <head>
              <meta name='viewport' content='width=device-width, initial-scale=1.0'/>
              <meta http-equiv='Content-Type' content='text/html; charset=UTF-8' />
            </head>
            <body style='background-color: {_backgroundColor}; margin:0; padding:0; -webkit-font-smoothing:antialiased; font-family: -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, Helvetica, Arial, sans-serif;'>
              <span style='display:none;max-height:0px;overflow:hidden;mso-hide:all;'>{preheader}</span>
              <table role='presentation' border='0' cellpadding='0' cellspacing='0' width='100%'>
                <tr>
                  <td align='center'>
                    <table role='presentation' border='0' cellpadding='0' cellspacing='0' width='600' style='width:100%;max-width:600px;margin:20px;'>
                      <tr>
                        <td style='background:#ffffff;border-radius:12px;padding:24px 28px;box-shadow:0 6px 18px rgba(15,23,42,0.06);'>
                          <table role='presentation' border='0' cellpadding='0' cellspacing='0' width='100%'>
                            <tr>
                              <td style='text-align:left;'>
                                <h2 style='color: {_primaryColor}; margin:0; font-size:22px;'>Miqat</h2>
                              </td>
                              <td style='text-align:right; vertical-align:middle;'>
                                <!-- Optional small logo or link -->
                              </td>
                            </tr>
                          </table>

                          <hr style='border:none;border-top:1px solid #EEF2FF;margin:18px 0;' />

                          <h1 style='font-size:20px;font-weight:700;color:#111827;margin:0 0 12px;'>Hi {name},</h1>
                          {content}

                          <div style='margin-top:28px;padding-top:20px;border-top:1px solid #E5E7EB;'>
                            <p style='font-size:14px;color:#6B7280;margin:0;'>Best regards,<br/>The Miqat Team</p>
                          </div>
                        </td>
                      </tr>
                      <tr>
                        <td style='text-align:center;padding:12px;font-size:12px;color:#9CA3AF;'>
                          © {DateTime.UtcNow.Year} Miqat Smart Calendar. Cairo, Egypt. • <a href='mailto:support@miqat.app' style='color:#9CA3AF;text-decoration:underline;'>Support</a>
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>";
        }

        private string GetPlainTextTemplate(string name, string body)
        {
            return $"Hi {name},\n\n{body}\n\nBest regards,\nThe Miqat Team";
        }

        private async Task SendEmailAsync(string toEmail, string toName, string subject, string htmlBody, string? textBody = null)
        {
            // Priority: if SMTP settings are provided, use MailKit/SMTP (more deployment-friendly).
            if (!string.IsNullOrWhiteSpace(_settings.SmtpHost))
            {
                var message = new MimeMessage();
                message.From.Add(new MailboxAddress(_settings.FromName, _settings.FromEmail));
                message.To.Add(new MailboxAddress(toName, toEmail));
                message.Subject = subject ?? string.Empty;

                var builder = new BodyBuilder();
                if (!string.IsNullOrEmpty(textBody)) builder.TextBody = textBody;
                if (!string.IsNullOrEmpty(htmlBody)) builder.HtmlBody = htmlBody;
                message.Body = builder.ToMessageBody();

                int attempts = 0;
                const int maxAttempts = 3;
                while (true)
                {
                    attempts++;
                    using var client = new MailKit.Net.Smtp.SmtpClient();
                    try
                    {
                        var secureSocket = _settings.SmtpUseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable;
                        await client.ConnectAsync(_settings.SmtpHost, _settings.SmtpPort, secureSocket);

                        if (!string.IsNullOrWhiteSpace(_settings.SmtpUsername))
                        {
                            await client.AuthenticateAsync(_settings.SmtpUsername, _settings.SmtpPassword);
                        }

                        await client.SendAsync(message);
                        await client.DisconnectAsync(true);
                        _logger.LogInformation("Email sent to {Email} via SMTP on attempt {Attempt}", toEmail, attempts);
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "SMTP send attempt {Attempt} failed for {Email}", attempts, toEmail);
                        if (attempts >= maxAttempts)
                        {
                            _logger.LogError(ex, "SMTP send failed after {Attempts} attempts for {Email}", attempts, toEmail);
                            throw;
                        }

                        // Exponential backoff
                        await Task.Delay(1000 * (int)Math.Pow(2, attempts));
                    }
                }

                return;
            }

            // Fallback to Brevo / Sendinblue API if API key provided
            if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
            {
                int attempts = 0;
                const int maxAttempts = 3;
                while (true)
                {
                    attempts++;
                    var apiInstance = new TransactionalEmailsApi();
                    var sender = new SendSmtpEmailSender(_settings.FromName, _settings.FromEmail);
                    var receiver = new SendSmtpEmailTo(toEmail, toName);
                    var sendSmtpEmail = new SendSmtpEmail(sender, new List<SendSmtpEmailTo> { receiver }, null, null, htmlBody, textBody, subject);

                    try
                    {
                        await apiInstance.SendTransacEmailAsync(sendSmtpEmail);
                        _logger.LogInformation("Email sent to {Email} via Brevo on attempt {Attempt}", toEmail, attempts);
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Brevo send attempt {Attempt} failed for {Email}", attempts, toEmail);
                        if (attempts >= maxAttempts)
                        {
                            _logger.LogError(ex, "Brevo send failed after {Attempts} attempts for {Email}", attempts, toEmail);
                            throw;
                        }

                        await Task.Delay(1000 * (int)Math.Pow(2, attempts));
                    }
                }

                return;
            }

            _logger.LogError("No email provider configured. Set SMTP settings or ApiKey in EmailSettings.");
            throw new InvalidOperationException("No email provider configured. Set SMTP settings or ApiKey in EmailSettings.");
        }
    }
}