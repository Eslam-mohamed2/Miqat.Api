using Miqat.Domain.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Miqat.Domain.Entities
{
    public class PasswordResetToken : BaseEntity
    {
        public string Token { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public bool IsUsed { get; set; } = false;

        public Guid UserId { get; set; }
        public virtual User User { get; set; } = null!;

        public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
        public bool IsValid => !IsUsed && !IsExpired;

        public PasswordResetToken(string token, Guid userId, int expiryMinutes = 30)
        {
            Token = token;
            UserId = userId;
            ExpiresAt = DateTime.UtcNow.AddMinutes(expiryMinutes);
        }

        public void MarkAsUsed()
        {
            IsUsed = true;
            SetUpdated();
        }

        private PasswordResetToken() { }
    }
}
