using System;

namespace Miqat.Application.Interfaces
{
    /// <summary>
    /// The caller behind the current request.
    ///
    /// Services need this to answer "may you touch this row?". Reading it from an
    /// ambient accessor rather than threading a userId through every method keeps
    /// the change small and, more importantly, makes it impossible to *forget* to
    /// pass it — the previous signatures took no caller at all, which is why every
    /// task and project was editable and deletable by any authenticated user.
    /// </summary>
    public interface ICurrentUserService
    {
        /// <summary>Null for unauthenticated requests (e.g. login, register).</summary>
        Guid? UserId { get; }

        string? Role { get; }

        bool IsAuthenticated { get; }

        bool IsAdmin { get; }

        /// <summary>The caller's id, or a 401 if there is no authenticated caller.</summary>
        Guid RequireUserId();
    }
}
