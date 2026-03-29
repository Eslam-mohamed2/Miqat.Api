using Miqat.Domain.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Miqat.Domain.Entities
{
    public class RefreshToken : BaseEntity
    {
        public string Token { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public bool IsRevoked { get; set; } = false;
        public DateTime? RevokedAt { get; set; }

        // Which device/browser created this token
        public string? CreatedByIp { get; set; }
        public string? RevokedByIp { get; set; }

        // Owner
        public Guid UserId { get; set; }
        public virtual User User { get; set; } = null!;

        // Computed — not stored
        public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
        public bool IsActive => !IsRevoked && !IsExpired;

        public RefreshToken(string token, Guid userId,
                            DateTime expiresAt, string? createdByIp = null)
        {
            Token = token;
            UserId = userId;
            ExpiresAt = expiresAt;
            CreatedByIp = createdByIp;
        }

        public void Revoke(string? revokedByIp = null)
        {
            IsRevoked = true;
            RevokedAt = DateTime.UtcNow;
            RevokedByIp = revokedByIp;
        }

        private RefreshToken() { }
    }
}
