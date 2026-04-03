using Miqat.Domain.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Miqat.Domain.Entities
{
    public class OtpCode : BaseEntity
    {
        public string Code { get; set; } = string.Empty;
        public string Purpose { get; set; } = string.Empty; // "EmailVerification" | "PasswordReset"
        public DateTime ExpiresAt { get; set; }
        public bool IsUsed { get; set; } = false;

        public Guid UserId { get; set; }
        public virtual User User { get; set; } = null!;

        // Computed
        public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
        public bool IsValid => !IsUsed && !IsExpired;

        public OtpCode(string code, Guid userId, string purpose, int expiryMinutes = 10)
        {
            Code = code;
            UserId = userId;
            Purpose = purpose;
            ExpiresAt = DateTime.UtcNow.AddMinutes(expiryMinutes);
        }

        public void MarkAsUsed()
        {
            IsUsed = true;
            SetUpdated();
        }

        private OtpCode() { }
    }
}
