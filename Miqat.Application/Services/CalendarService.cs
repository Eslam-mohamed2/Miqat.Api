using Miqat.Application.Common;
using Miqat.Application.Interfaces;
using Miqat.Application.Modules;
using Miqat.Domain.Entities;

namespace Miqat.Application.Services
{
    /// <inheritdoc />
    /// <remarks>
    /// Calendars and calendar preferences are private to their owner, so every
    /// read and write here filters on the id of the caller. A calendar that is
    /// not yours does not exist as far as this service is concerned.
    /// </remarks>
    public class CalendarService : ICalendarService
    {
        private const int MaxCalendars = 25;

        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUser;

        public CalendarService(IUnitOfWork unitOfWork, ICurrentUserService currentUser)
        {
            _unitOfWork = unitOfWork;
            _currentUser = currentUser;
        }

        // ── Calendars ─────────────────────────────────────────────────────────

        public async Task<List<CalendarDto>> GetMineAsync()
        {
            var userId = _currentUser.RequireUserId();
            var calendars = await LoadAsync(userId);

            var eventCounts = await _unitOfWork.Repository<CalendarEvent>()
                .CountGroupedAsync(e => e.OwnerId == userId && !e.IsDeleted, e => e.CalendarId);

            return calendars
                .OrderByDescending(c => c.IsDefault)
                .ThenBy(c => c.Name)
                .Select(c => ToDto(c, eventCounts.GetValueOrDefault(c.Id)))
                .ToList();
        }

        public async Task<CalendarDto> CreateAsync(SaveCalendarDto dto)
        {
            var userId = _currentUser.RequireUserId();

            if (string.IsNullOrWhiteSpace(dto.Name))
                throw new ApiException("A calendar needs a name.", 400);
            if ((await LoadAsync(userId)).Count >= MaxCalendars)
                throw new ApiException($"You can have up to {MaxCalendars} calendars.", 400);

            var calendar = new UserCalendar(dto.Name, dto.Color, userId) { IsVisible = dto.IsVisible ?? true };

            await _unitOfWork.Repository<UserCalendar>().AddAsync(calendar);
            await _unitOfWork.CompleteAsync();
            return ToDto(calendar, eventCount: 0);
        }

        public async Task<CalendarDto?> UpdateAsync(Guid id, SaveCalendarDto dto)
        {
            var userId = _currentUser.RequireUserId();
            var calendar = await FindMineAsync(userId, id);
            if (calendar == null) return null;

            calendar.Update(dto.Name, dto.Color);
            if (dto.IsVisible.HasValue) calendar.IsVisible = dto.IsVisible.Value;
            calendar.SetUpdated(userId.ToString());
            await _unitOfWork.CompleteAsync();

            var eventCount = await _unitOfWork.Repository<CalendarEvent>()
                .CountAsync(e => e.CalendarId == id && !e.IsDeleted);
            return ToDto(calendar, eventCount);
        }

        public async Task<bool> DeleteAsync(Guid id)
        {
            var userId = _currentUser.RequireUserId();
            var calendar = await FindMineAsync(userId, id);
            if (calendar == null) return false;

            if (calendar.IsDefault)
                throw new ApiException("The default calendar cannot be deleted.", 400);

            // Deleting a calendar must not delete what is planned in it.
            var fallback = await ResolveAsync(userId, calendarId: null);
            var events = await _unitOfWork.Repository<CalendarEvent>().FindAsync(e => e.CalendarId == id);
            foreach (var calendarEvent in events) calendarEvent.CalendarId = fallback.Id;

            _unitOfWork.Repository<UserCalendar>().Delete(calendar);
            await _unitOfWork.CompleteAsync();
            return true;
        }

        public async Task<UserCalendar> ResolveAsync(Guid userId, Guid? calendarId)
        {
            var calendars = await LoadAsync(userId);

            return calendarId.HasValue
                ? calendars.FirstOrDefault(c => c.Id == calendarId.Value)
                  ?? throw new ApiException("That calendar was not found.", 400)
                : calendars.First(c => c.IsDefault);
        }

        /// <summary>
        /// The calendars of a user. Everyone has a default calendar; it is
        /// created here the first time anything asks for their calendars.
        /// </summary>
        private async Task<List<UserCalendar>> LoadAsync(Guid userId)
        {
            var calendars = (await _unitOfWork.Repository<UserCalendar>()
                .FindAsync(c => c.OwnerId == userId)).ToList();

            if (calendars.All(c => !c.IsDefault))
            {
                var defaultCalendar = UserCalendar.CreateDefault(userId);
                await _unitOfWork.Repository<UserCalendar>().AddAsync(defaultCalendar);
                await _unitOfWork.CompleteAsync();
                calendars.Add(defaultCalendar);
            }

            return calendars;
        }

        private async Task<UserCalendar?> FindMineAsync(Guid userId, Guid id) =>
            (await _unitOfWork.Repository<UserCalendar>()
                .FindAsync(c => c.Id == id && c.OwnerId == userId)).FirstOrDefault();

        private static CalendarDto ToDto(UserCalendar calendar, int eventCount) => new()
        {
            Id = calendar.Id,
            Name = calendar.Name,
            Color = calendar.Color,
            IsDefault = calendar.IsDefault,
            IsVisible = calendar.IsVisible,
            EventCount = eventCount,
            CreatedAt = calendar.CreatedAt,
            UpdatedAt = calendar.UpdatedAt
        };

        // ── Preferences ───────────────────────────────────────────────────────

        public async Task<CalendarSettingsDto> GetSettingsAsync()
        {
            var userId = _currentUser.RequireUserId();
            return await ToDtoAsync(await GetSettingsOfAsync(userId));
        }

        public async Task<CalendarSettingsDto> SaveSettingsAsync(CalendarSettingsDto dto)
        {
            var userId = _currentUser.RequireUserId();

            var settings = await FindSettingsAsync(userId);
            if (settings == null)
            {
                settings = new CalendarSettings(userId);
                await _unitOfWork.Repository<CalendarSettings>().AddAsync(settings);
            }

            settings.WeekStartsOn = dto.WeekStartsOn;
            settings.DefaultView = dto.DefaultView;
            settings.Use24HourClock = dto.Use24HourClock;
            settings.DefaultEventMinutes = dto.DefaultEventMinutes;
            settings.DefaultReminderMinutes = dto.DefaultReminderMinutes;
            settings.SetWorkDays(dto.WorkDays);

            try
            {
                settings.SetWorkingHours(
                    ClockTime.ToMinutes(dto.WorkDayStart) ?? -1, ClockTime.ToMinutes(dto.WorkDayEnd) ?? -1);
            }
            catch (ArgumentException ex)
            {
                throw new ApiException(ex.Message, 400);
            }

            settings.SetUpdated(userId.ToString());
            await _unitOfWork.CompleteAsync();
            return await ToDtoAsync(settings);
        }

        public async Task<CalendarSettings> GetSettingsOfAsync(Guid userId) =>
            await FindSettingsAsync(userId) ?? new CalendarSettings(userId);

        private async Task<CalendarSettings?> FindSettingsAsync(Guid userId) =>
            (await _unitOfWork.Repository<CalendarSettings>().FindAsync(s => s.UserId == userId)).FirstOrDefault();

        private async Task<CalendarSettingsDto> ToDtoAsync(CalendarSettings settings)
        {
            var user = await _unitOfWork.Repository<User>().GetByIdAsync(settings.UserId);

            return new CalendarSettingsDto
            {
                WeekStartsOn = settings.WeekStartsOn,
                DefaultView = settings.DefaultView,
                Use24HourClock = settings.Use24HourClock,
                WorkDayStart = ClockTime.FromMinutes(settings.WorkDayStartMinutes),
                WorkDayEnd = ClockTime.FromMinutes(settings.WorkDayEndMinutes),
                WorkDays = settings.WorkDays.ToList(),
                DefaultEventMinutes = settings.DefaultEventMinutes,
                DefaultReminderMinutes = settings.DefaultReminderMinutes,
                TimeZone = TimeZones.Resolve(user?.TimeZone).Id
            };
        }
    }
}
