using Miqat.Application.Interfaces;
using Miqat.Application.Modules;
using Miqat.Application.Specifications.Events;
using Miqat.Application.Specifications.Tasks;
using Miqat.Domain.Entities;

namespace Miqat.Application.Services
{
    /// <summary>
    /// One search across the caller's tasks, projects and other people.
    /// Everything is filtered and limited in the database, and nothing outside
    /// what the caller can already open is ever returned.
    /// </summary>
    public class SearchService : ISearchService
    {
        private const int MaxPerSection = 25;

        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUser;
        private readonly IUserService _users;
        private readonly TaskDtoFactory _taskDtos;
        private readonly PeopleDirectory _people;

        public SearchService(
            IUnitOfWork unitOfWork,
            ICurrentUserService currentUser,
            IUserService users,
            TaskDtoFactory taskDtos,
            PeopleDirectory people)
        {
            _unitOfWork = unitOfWork;
            _currentUser = currentUser;
            _users = users;
            _taskDtos = taskDtos;
            _people = people;
        }

        public async Task<SearchResultsDto> SearchAsync(string query, int limitPerSection)
        {
            var userId = _currentUser.RequireUserId();
            var term = (query ?? string.Empty).Trim();
            var results = new SearchResultsDto { Query = term };
            if (term.Length == 0) return results;

            var lowerTerm = term.ToLower();
            var take = Math.Clamp(limitPerSection, 1, MaxPerSection);

            // The projects the caller owns or belongs to. They bound both the
            // project matches and which shared tasks may appear.
            var groupIds = (await _people.GetProjectIdsAsync(userId)).ToList();

            var tasks = await _unitOfWork.Repository<TaskItem>()
                .ListAsync(new TaskSearchSpec(userId, groupIds, lowerTerm, take));
            results.Tasks = await _taskDtos.ToDtosAsync(tasks);

            if (groupIds.Count > 0)
            {
                var projects = await _unitOfWork.Repository<Group>()
                    .FindAsync(g => groupIds.Contains(g.Id) && !g.IsDeleted &&
                                    (g.Name.ToLower().Contains(lowerTerm) ||
                                     (g.Description != null && g.Description.ToLower().Contains(lowerTerm))));

                results.Projects = projects
                    .OrderBy(g => g.Name)
                    .Take(take)
                    .Select(g => new SearchProjectDto
                    {
                        Id = g.Id,
                        Name = g.Name,
                        Description = g.Description,
                        Color = g.Color,
                        OwnerId = g.OwnerId
                    })
                    .ToList();
            }

            results.Users = (await _users.SearchAsync(term, userId)).Take(take).ToList();

            var events = await _unitOfWork.Repository<CalendarEvent>()
                .ListAsync(new EventSearchSpec(userId, groupIds, lowerTerm, take));
            results.Events = events
                .Select(e => new SearchEventDto
                {
                    Id = e.Id,
                    Title = e.Title,
                    Location = e.Location,
                    StartsAt = e.StartsAt,
                    EndsAt = e.EndsAt,
                    IsAllDay = e.IsAllDay,
                    IsRecurring = e.IsRecurring,
                    IsOwner = e.OwnerId == userId
                })
                .ToList();

            return results;
        }
    }
}
