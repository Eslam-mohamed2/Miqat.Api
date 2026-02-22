using Miqat.Domain.Common;
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
        public DateTime? DateOfBirth { get; set; }
        public Enumerations.Gender? Gender { get; set; }
        public string? Country { get; set; }
        public virtual ICollection<TaskItem> Tasks { get; set; } = new List<TaskItem>();

        public User(string FullName, string Email, DateTime DateOfBirth, Enumerations.Gender? Gender,string? Country)
        {
            if (string.IsNullOrWhiteSpace(FullName))
                throw new ArgumentException("Username cannot be empty.", nameof(FullName));  
            if(this.DateOfBirth > DateOfBirth)
                throw new ArgumentException("Birth date cannot be in the future.");

            this.FullName = FullName;
            this.Email = Email;
            this.DateOfBirth = DateOfBirth;
            this.Gender = Gender;
            this.Country = Country;
        }

        private User() {}
    }
}
