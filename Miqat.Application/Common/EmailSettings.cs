using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Miqat.Application.Common
{
    public class EmailSettings
    {
        public string FromEmail { get; set; } = string.Empty;
        public string FromName { get; set; } = string.Empty;
        // Brevo / Sendinblue API key (optional). If SMTP settings are provided,
        // SMTP will be used instead for better deployment compatibility.
        public string ApiKey { get; set; } = string.Empty;

        // SMTP settings (recommended for deployment)
        public string SmtpHost { get; set; } = string.Empty;
        public int SmtpPort { get; set; } = 587;
        public bool SmtpUseSsl { get; set; } = true;
        public string SmtpUsername { get; set; } = string.Empty;
        public string SmtpPassword { get; set; } = string.Empty;
    }
}
