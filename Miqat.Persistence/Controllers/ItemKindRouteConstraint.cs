using Miqat.Application.Common;
using Miqat.Domain.Enumerations;

namespace Miqat.API.Controllers
{
    /// <summary>
    /// Lets one action serve both /api/Task/{id}/... and /api/Event/{id}/...:
    /// the route segment has to be the name of a <see cref="CalendarItemKind"/>.
    /// Used as {itemKind:itemKind}; registered in Program.cs.
    /// </summary>
    public class ItemKindRouteConstraint : IRouteConstraint
    {
        public const string Name = "itemKind";

        public bool Match(
            HttpContext? httpContext, IRouter? route, string routeKey,
            RouteValueDictionary values, RouteDirection routeDirection) =>
            values.TryGetValue(routeKey, out var value) &&
            EnumText.TryParseDefined<CalendarItemKind>(value?.ToString(), out _);
    }
}
