using Miqat.Application.Modules;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Miqat.Application.Interfaces
{
    public interface IEmailService
    {
        /// <summary>
        /// True when a mail provider (API key or SMTP host) is set up. Lets callers
        /// tell "nothing is configured" (local development) from "the provider failed".
        /// </summary>
        bool IsConfigured { get; }

        Task SendOtpAsync(string toEmail, string fullName, string otp);
        Task SendPasswordResetAsync(string toEmail, string fullName, string resetLink);
        Task SendWelcomeAsync(string toEmail, string fullName);
        Task SendPasswordResetOtpAsync(string toEmail, string fullName, string otp);

        /// <summary>The code that confirms a new sign-in address, sent to that new address.</summary>
        Task SendEmailChangeOtpAsync(string toEmail, string fullName, string otp);

        /// <summary>A notification from the app (task assigned, invitation, reminder, ...) as an email.</summary>
        Task SendNotificationAsync(string toEmail, string fullName, string title, string message);

        Task SendWeeklySummaryAsync(string toEmail, string fullName, WeeklySummaryDto summary);
    }
}
