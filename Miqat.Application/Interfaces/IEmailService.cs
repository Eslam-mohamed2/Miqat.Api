using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Miqat.Application.Interfaces
{
    public interface IEmailService
    {
        Task SendOtpAsync(string toEmail, string fullName, string otp);
        Task SendPasswordResetAsync(string toEmail, string fullName, string resetLink);
        Task SendWelcomeAsync(string toEmail, string fullName);
    }
}
