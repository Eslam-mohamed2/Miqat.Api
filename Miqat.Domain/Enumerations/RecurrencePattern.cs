using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Miqat.Domain.Enumerations
{
    public enum RecurrencePattern
    {
        None,
        Daily,
        Weekly,
        Monthly,

        // Stored as text, so adding a member needs no migration.
        Yearly
    }
}
