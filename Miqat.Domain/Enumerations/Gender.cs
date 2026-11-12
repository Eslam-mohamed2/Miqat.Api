using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Miqat.Domain.Enumerations
{
    public enum Gender
    {
        Male,
        Female,

        // Stored as text (see UserConfiguration), so adding a member needs no
        // migration. The profile form offers this option.
        Other
    }
}
