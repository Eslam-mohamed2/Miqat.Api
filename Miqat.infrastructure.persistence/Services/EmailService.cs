using Microsoft.Extensions.Options;
using Miqat.Application.Common;
using Miqat.Application.Interfaces;
using Miqat.Application.Modules;
using sib_api_v3_sdk.Api;
using sib_api_v3_sdk.Client;
using sib_api_v3_sdk.Model;
using Microsoft.Extensions.Logging;
using Task = System.Threading.Tasks.Task;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;

namespace Miqat.infrastructure.persistence.Services
{
    public class EmailService : IEmailService
    {
        /// <summary>Where the buttons in the emails lead.</summary>
        private const string AppUrl = "https://mqiatsmartcalendar.vercel.app";

        private readonly EmailSettings _settings;
        private readonly string _primaryColor = "#4F46E5";
        private readonly string _backgroundColor = "#F9FAFB";
        private readonly ILogger<EmailService> _logger;

        public EmailService(IOptions<EmailSettings> settings, ILogger<EmailService> logger)
        {
            _settings = settings.Value;
            _logger = logger;

            if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
            {
                Configuration.Default.ApiKey["api-key"] = _settings.ApiKey;
            }
        }

        /// <summary>True when either Brevo (ApiKey) or SMTP (SmtpHost) is set up.</summary>
        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(_settings.ApiKey) || !string.IsNullOrWhiteSpace(_settings.SmtpHost);

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
                    <a href='{AppUrl}/dashboard' style='background: {_primaryColor}; color: #ffffff; padding: 14px 28px; border-radius: 8px; text-decoration: none; font-weight: 600; display: inline-block;'>Go to Dashboard</a>
                </div>
                <p style='font-size: 16px; color: #374151;'>Start organizing your life with Miqat's smart calendar today.</p>";

            var html = GetBaseTemplate(fullName, contentHtml, "Welcome to Miqat — get started with your smart calendar.");
            var text = GetPlainTextTemplate(fullName, $"Welcome to Miqat! Visit {AppUrl}/dashboard to get started.");

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

        public async Task SendEmailChangeOtpAsync(string toEmail, string fullName, string otp)
        {
            var subject = $"{otp} confirms your new Miqat email address";
            var contentHtml = $@"
                <p style='margin-bottom: 24px; font-size: 16px; color: #374151;'>You asked to sign in to Miqat with this email address from now on. Enter this code in the app to confirm it:</p>
                <div style='background: #F3F4F6; border-radius: 12px; padding: 32px; text-align: center; margin: 24px 0;'>
                    <span style='font-family: monospace; font-size: 36px; font-weight: 700; letter-spacing: 12px; color: #111827;'>{otp}</span>
                </div>
                <p style='font-size: 14px; color: #6B7280;'>The code expires in 10 minutes. If this was not you, ignore this email: nothing changes without the code.</p>";

            var html = GetBaseTemplate(fullName, contentHtml, "Confirm your new Miqat email address.");
            var text = GetPlainTextTemplate(fullName, $"Use this code to confirm your new email address: {otp} (expires in 10 minutes). If this was not you, ignore this email.");

            await SendEmailAsync(toEmail, fullName, subject, html, text);
        }

        public async Task SendNotificationAsync(string toEmail, string fullName, string title, string message)
        {
            // Titles and messages carry text people typed (task and event names),
            // so they are encoded before they go into the HTML.
            var contentHtml = $@"
                <p style='font-size: 16px; color: #374151;'>{WebUtility.HtmlEncode(message)}</p>
                {OpenAppButton("Open Miqat", "notifications")}
                <p style='font-size: 13px; color: #9CA3AF;'>You get this email because email notifications are on in Settings &gt; Notifications.</p>";

            var html = GetBaseTemplate(fullName, contentHtml, message);
            var text = GetPlainTextTemplate(fullName, $"{message}\n\nOpen Miqat: {AppUrl}/notifications");

            await SendEmailAsync(toEmail, fullName, $"{title} - Miqat", html, text);
        }

        public async Task SendWeeklySummaryAsync(string toEmail, string fullName, WeeklySummaryDto summary)
        {
            var counts = new (string Label, int Value)[]
            {
                ("Finished last week", summary.CompletedLastWeek),
                ("Due this week", summary.DueThisWeek),
                ("Overdue", summary.Overdue),
                ("Events this week", summary.EventsThisWeek)
            };

            var rows = string.Concat(counts.Select(c =>
                $"<tr><td style='padding:8px 0;font-size:15px;color:#374151;'>{c.Label}</td>" +
                $"<td style='padding:8px 0;font-size:15px;font-weight:700;color:#111827;text-align:right;'>{c.Value}</td></tr>"));

            var comingUp = summary.ComingUp.Count == 0
                ? "<p style='font-size:15px;color:#6B7280;'>Nothing is planned for the coming days.</p>"
                : "<ul style='padding-left:18px;margin:8px 0;'>" +
                  string.Concat(summary.ComingUp.Select(line =>
                      $"<li style='font-size:15px;color:#374151;padding:3px 0;'>{WebUtility.HtmlEncode(line)}</li>")) +
                  "</ul>";

            var contentHtml = $@"
                <p style='font-size: 16px; color: #374151;'>Here is your week at a glance.</p>
                <table role='presentation' width='100%' cellpadding='0' cellspacing='0' style='margin:12px 0 20px;'>{rows}</table>
                <h3 style='font-size:16px;color:#111827;margin:0 0 4px;'>Coming up</h3>
                {comingUp}
                {OpenAppButton("Open my calendar", "calendar")}";

            var plain = string.Join("\n", counts.Select(c => $"{c.Label}: {c.Value}")) +
                        "\n\nComing up:\n" +
                        (summary.ComingUp.Count == 0 ? "Nothing is planned for the coming days." : string.Join("\n", summary.ComingUp)) +
                        $"\n\nOpen Miqat: {AppUrl}/calendar";

            var html = GetBaseTemplate(fullName, contentHtml, "Your week on Miqat.");
            await SendEmailAsync(toEmail, fullName, "Your week on Miqat", html, GetPlainTextTemplate(fullName, plain));
        }

        private string OpenAppButton(string label, string page) => $@"
                <div style='text-align: center; margin: 28px 0;'>
                    <a href='{AppUrl}/{page}' style='background: {_primaryColor}; color: #ffffff; padding: 14px 28px; border-radius: 8px; text-decoration: none; font-weight: 600; display: inline-block;'>{label}</a>
                </div>";

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

        private string GetBaseTemplate(string name, string content, string preheader = "")
        {
            return $@"
            <!doctype html>
            <html>
            <head>
              <meta name='viewport' content='width=device-width, initial-scale=1.0'/>
              <meta http-equiv='Content-Type' content='text/html; charset=UTF-8' />
            </head>
            <body style='background-color: {_backgroundColor}; margin:0; padding:0; -webkit-font-smoothing:antialiased; font-family: -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, Helvetica, Arial, sans-serif;'>
              <span style='display:none;max-height:0px;overflow:hidden;mso-hide:all;'>{WebUtility.HtmlEncode(preheader)}</span>
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
                                <!-- Optional logo -->
                              </td>
                            </tr>
                          </table>

                          <hr style='border:none;border-top:1px solid #EEF2FF;margin:18px 0;' />

                          <h1 style='font-size:20px;font-weight:700;color:#111827;margin:0 0 12px;'>Hi {WebUtility.HtmlEncode(name)},</h1>
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

            // SMTP. EmailSettings has always documented this as the recommended
            // path ("If SMTP settings are provided, SMTP will be used instead"),
            // but it was never implemented — only the API-key branch above existed.
            // A deployment that set SmtpHost/SmtpUsername/SmtpPassword and left
            // ApiKey empty therefore sent nothing at all, while the calling
            // endpoints swallowed the failure and still answered 200.
            if (!string.IsNullOrWhiteSpace(_settings.SmtpHost))
            {
                using var message = new MailMessage
                {
                    From = new MailAddress(_settings.FromEmail, _settings.FromName),
                    Subject = subject,
                    Body = htmlBody,
                    IsBodyHtml = true
                };
                message.To.Add(new MailAddress(toEmail, toName));

                if (!string.IsNullOrWhiteSpace(textBody))
                {
                    // A plain-text alternative keeps the message out of spam
                    // filters that penalise HTML-only mail.
                    message.AlternateViews.Add(
                        AlternateView.CreateAlternateViewFromString(textBody, null, "text/plain"));
                    message.AlternateViews.Add(
                        AlternateView.CreateAlternateViewFromString(htmlBody, null, "text/html"));
                }

                using var client = new System.Net.Mail.SmtpClient(_settings.SmtpHost, _settings.SmtpPort);

                // Order matters: assigning UseDefaultCredentials *after* Credentials
                // resets Credentials to null, so the client connects unauthenticated
                // and Brevo answers "5.7.0 Please authenticate first".
                client.UseDefaultCredentials = false;
                client.Credentials = new NetworkCredential(_settings.SmtpUsername, _settings.SmtpPassword);
                client.DeliveryMethod = SmtpDeliveryMethod.Network;

                // Brevo's relay needs STARTTLS on 587 and implicit TLS on 465.
                // SmtpUseSsl is commonly left false for 587 because it reads as
                // "not implicit SSL", so the port decides rather than the flag.
                client.EnableSsl = _settings.SmtpUseSsl
                    || _settings.SmtpPort == 587
                    || _settings.SmtpPort == 465;
                client.Timeout = 20_000;

                await client.SendMailAsync(message);
                _logger.LogInformation("Email sent to {Email} via SMTP ({Host})", toEmail, _settings.SmtpHost);
                return;
            }

            _logger.LogError("No email provider configured. Set EmailSettings:ApiKey or EmailSettings:SmtpHost.");
            throw new InvalidOperationException(
                "No email provider configured. Set EmailSettings:ApiKey or EmailSettings:SmtpHost.");
        }
    }
}
