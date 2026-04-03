using Miqat.Domain.Common;
using Miqat.Domain.Enumerations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Miqat.Domain.Entities
{
    public class User : Common.BaseEntity
    {
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public string? ProfilePictureUrl { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public Gender? Gender { get; set; }
        public string? Country { get; set; }
        public string TimeZone { get; set; } = "UTC";
        public UserRole Role { get; set; } = UserRole.User;
        public bool IsActive { get; set; } = true;
        public bool IsVerified { get; set; } = false;
        public string? GoogleId { get; set; }
        public bool IsGoogleAccount { get; set; } = false;

        // Google OAuth
        public virtual ICollection<OtpCode> OtpCodes { get; set; } = new List<OtpCode>();
        public virtual ICollection<PasswordResetToken> PasswordResetTokens { get; set; } = new List<PasswordResetToken>();

        // Navigation
        public virtual ICollection<TaskItem> Tasks { get; set; } = new List<TaskItem>();
        public virtual ICollection<GroupMember> GroupMembers { get; set; } = new List<GroupMember>();
        public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();
        public virtual ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();

        public User(string fullName, string email, DateTime dateOfBirth,
                    Gender? gender, string? country,
                    string? phoneNumber = null, string timeZone = "UTC")
        {
            if (string.IsNullOrWhiteSpace(fullName))
                throw new ArgumentException("Full name cannot be empty.", nameof(fullName));
            if (string.IsNullOrWhiteSpace(email))
                throw new ArgumentException("Email cannot be empty.", nameof(email));

            FullName = fullName;
            Email = email;
            DateOfBirth = dateOfBirth;
            Gender = gender;
            Country = country;
            PhoneNumber = phoneNumber;
            TimeZone = timeZone;
        }

        private User() { }
    }
}
