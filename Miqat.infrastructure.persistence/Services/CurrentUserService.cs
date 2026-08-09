using Microsoft.AspNetCore.Http;
using Miqat.Application.Common;
using Miqat.Application.Interfaces;
using System;
using System.Linq;
using System.Security.Claims;

namespace Miqat.infrastructure.persistence.Services
{
    /// <inheritdoc />
    public class CurrentUserService : ICurrentUserService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public CurrentUserService(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        private ClaimsPrincipal? Principal => _httpContextAccessor.HttpContext?.User;

        public Guid? UserId
        {
            get
            {
                // The token is issued from ClaimTypes.NameIdentifier, which
                // serialises to the long schema URI. The short "nameid"/"sub"
                // forms are accepted too so this keeps working if the token
                // format is ever normalised.
                var raw = Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? Principal?.FindFirstValue("nameid")
                          ?? Principal?.FindFirstValue("sub");

                return Guid.TryParse(raw, out var id) ? id : null;
            }
        }

        public string? Role =>
            Principal?.FindFirstValue(ClaimTypes.Role) ?? Principal?.FindFirstValue("role");

        public bool IsAuthenticated => UserId.HasValue;

        public bool IsAdmin =>
            string.Equals(Role, "Admin", StringComparison.OrdinalIgnoreCase);

        public Guid RequireUserId() =>
            UserId ?? throw new ApiException("You must be signed in to do that.", 401);
    }
}
