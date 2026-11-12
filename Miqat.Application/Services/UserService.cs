using Miqat.Application.Common;
using Miqat.Application.Interfaces;
using Miqat.Application.Modules;
using Miqat.Application.Specifications.Users;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;

namespace Miqat.Application.Services
{
    public class UserService : IUserService
    {
        private const int MaxSearchResults = 20;

        private readonly IUnitOfWork _unitOfWork;
        private readonly UserMapper _mapper;

        public UserService(IUnitOfWork unitOfWork, UserMapper mapper)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
        }

        public async Task<IEnumerable<UserDto>> GetAllUsers()
        {
            var users = await _unitOfWork.Repository<User>().FindAsync(u => !u.IsDeleted);
            return _mapper.MapUsersToDtos(users.OrderBy(u => u.FullName).ToList());
        }

        public async Task<UserDto?> GetUserById(Guid id, bool includePrivateFields = true)
        {
            // A plain lookup. The old spec joined the user's group memberships,
            // their groups and every notification they had ever received, on
            // each profile load, to map a DTO that uses none of them.
            var user = await FindLiveUserAsync(id);
            if (user == null) return null;

            var dto = _mapper.MapUserToDto(user);
            if (!includePrivateFields)
            {
                dto.PhoneNumber = null;
                dto.DateOfBirth = null;
            }

            return dto;
        }

        public async Task<UserDto> CreateAsync(UserDto dto)
        {
            EnumText.TryParseDefined<Gender>(dto.Gender, out var gender);

            var entity = new User(
                dto.FullName,
                dto.Email.Trim().ToLowerInvariant(),
                dto.DateOfBirth ?? DateTime.UtcNow,
                gender,
                dto.Country,
                dto.PhoneNumber,
                string.IsNullOrWhiteSpace(dto.TimeZone) ? "UTC" : dto.TimeZone
            );

            await _unitOfWork.Repository<User>().AddAsync(entity);
            await _unitOfWork.CompleteAsync();
            return _mapper.MapUserToDto(entity);
        }

        public async Task<bool> UpdateAsync(Guid id, UserDto dto)
        {
            var entity = await FindLiveUserAsync(id);
            if (entity == null) return false;

            // A blank email means "leave it"; a different one must be free. The
            // unique index would otherwise turn a duplicate into a 500.
            var newEmail = (dto.Email ?? string.Empty).Trim().ToLowerInvariant();
            if (newEmail.Length > 0 && !string.Equals(newEmail, entity.Email, StringComparison.OrdinalIgnoreCase))
            {
                var taken = await _unitOfWork.Repository<User>()
                    .FindAsync(u => u.Email.ToLower() == newEmail && u.Id != id);
                if (taken.Any())
                    throw new ApiException("This email is already in use.", 409);

                entity.Email = newEmail;
            }

            entity.FullName = dto.FullName.Trim();
            entity.Country = NullIfBlank(dto.Country);
            entity.DateOfBirth = dto.DateOfBirth;
            entity.PhoneNumber = NullIfBlank(dto.PhoneNumber);
            if (dto.Bio != null) entity.Bio = NullIfBlank(dto.Bio);

            // Only replaced when a value is sent, so an edit form that does not
            // carry the picture cannot wipe it. Removal has its own endpoint.
            if (!string.IsNullOrWhiteSpace(dto.ProfilePictureUrl))
                entity.ProfilePictureUrl = dto.ProfilePictureUrl.Trim();

            ApplyTimeZone(entity, dto.TimeZone);
            ApplyGender(entity, dto.Gender);

            entity.SetUpdated();
            await _unitOfWork.CompleteAsync();
            return true;
        }

        public async Task<bool> UpdateProfileAsync(Guid id, UpdateProfileDto dto)
        {
            var entity = await FindLiveUserAsync(id);
            if (entity == null) return false;

            entity.FullName = dto.FullName.Trim();
            entity.PhoneNumber = NullIfBlank(dto.PhoneNumber);
            entity.Country = NullIfBlank(dto.Country);
            entity.DateOfBirth = dto.DateOfBirth;

            // Unlike the fields above, a missing bio leaves the stored one alone:
            // forms written before the field existed do not send it, and must not
            // wipe it. An empty string clears it.
            if (dto.Bio != null) entity.Bio = NullIfBlank(dto.Bio);

            ApplyTimeZone(entity, dto.TimeZone);
            ApplyGender(entity, dto.Gender);

            // Email, Role and ProfilePictureUrl are deliberately not assignable
            // here — see UpdateProfileDto.

            entity.SetUpdated(id.ToString());
            await _unitOfWork.CompleteAsync();
            return true;
        }

        public async Task<UserPreferencesDto?> GetPreferencesAsync(Guid id)
        {
            var entity = await FindLiveUserAsync(id);
            return entity == null ? null : ToPreferences(entity);
        }

        public async Task<UserPreferencesDto?> UpdatePreferencesAsync(Guid id, UserPreferencesDto dto)
        {
            var entity = await FindLiveUserAsync(id);
            if (entity == null) return null;

            if (dto.PushNotifications.HasValue) entity.PushNotificationsEnabled = dto.PushNotifications.Value;
            if (dto.EmailNotifications.HasValue) entity.EmailNotificationsEnabled = dto.EmailNotifications.Value;
            if (dto.WeeklySummary.HasValue) entity.WeeklySummaryEnabled = dto.WeeklySummary.Value;

            entity.SetUpdated(id.ToString());
            await _unitOfWork.CompleteAsync();
            return ToPreferences(entity);
        }

        private static UserPreferencesDto ToPreferences(User user) => new()
        {
            PushNotifications = user.PushNotificationsEnabled,
            EmailNotifications = user.EmailNotificationsEnabled,
            WeeklySummary = user.WeeklySummaryEnabled
        };

        public async Task<bool> SetProfilePictureAsync(Guid id, string? imageUrl)
        {
            var entity = await FindLiveUserAsync(id);
            if (entity == null) return false;

            entity.ProfilePictureUrl = NullIfBlank(imageUrl);
            entity.SetUpdated(id.ToString());
            await _unitOfWork.CompleteAsync();
            return true;
        }

        public async Task<bool> SetAccessAsync(Guid id, UserAccessDto dto, Guid actingUserId)
        {
            var entity = await FindLiveUserAsync(id);
            if (entity == null) return false;

            UserRole? newRole = null;
            if (!string.IsNullOrWhiteSpace(dto.Role))
            {
                if (!EnumText.TryParseDefined<UserRole>(dto.Role, out var parsed))
                    throw new ApiException("Role must be Admin or User.", 400);
                newRole = parsed;
            }

            // An admin locking themselves out, or removing their own admin role,
            // can leave the system with nobody able to undo it.
            if (id == actingUserId &&
                (dto.IsActive == false || (newRole.HasValue && newRole.Value != UserRole.Admin)))
                throw new ApiException("You cannot deactivate or demote your own account.", 400);

            if (newRole.HasValue) entity.Role = newRole.Value;

            if (dto.IsActive.HasValue)
            {
                entity.IsActive = dto.IsActive.Value;
                if (!entity.IsActive) await RevokeSessionsAsync(id);
            }

            entity.SetUpdated(actingUserId.ToString());
            await _unitOfWork.CompleteAsync();
            return true;
        }

        public async Task<bool> DeleteAsync(Guid id, Guid actingUserId)
        {
            if (id == actingUserId)
                throw new ApiException("You cannot delete your own account from here.", 400);

            var entity = await FindLiveUserAsync(id);
            if (entity == null) return false;

            entity.SoftDelete(actingUserId.ToString());
            await RevokeSessionsAsync(id);
            await EndFriendshipsAsync(id, actingUserId);

            await _unitOfWork.CompleteAsync();
            return true;
        }

        /// <summary>
        /// A deleted account is nobody's friend: its friendships and open
        /// requests end with it, so it leaves the friend lists of other people.
        /// Blocks are kept: who someone blocked is their own record.
        /// </summary>
        private async Task EndFriendshipsAsync(Guid userId, Guid actingUserId)
        {
            var friendships = await _unitOfWork.Repository<Friendship>()
                .FindAsync(f => (f.SenderId == userId || f.ReceiverId == userId) &&
                                !f.IsDeleted && f.Status != FriendshipStatus.Blocked);

            foreach (var friendship in friendships)
                friendship.SoftDelete(actingUserId.ToString());
        }

        public async Task<IEnumerable<UserDto>> SearchAsync(string query, Guid excludeUserId)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new List<UserDto>();

            // Filtered and limited in the database. This used to load the whole
            // Users table into memory on every keystroke.
            var spec = new UserSearchSpec(query.Trim().ToLower(), excludeUserId, MaxSearchResults);
            var users = await _unitOfWork.Repository<User>().ListAsync(spec);

            return users.Select(u => new UserDto
            {
                Id = u.Id,
                FullName = u.FullName,
                Email = u.Email,
                Country = u.Country,
                ProfilePictureUrl = u.ProfilePictureUrl
            }).ToList();
        }

        // ── helpers ───────────────────────────────────────────────────────────

        private async Task<User?> FindLiveUserAsync(Guid id)
        {
            var user = await _unitOfWork.Repository<User>().GetByIdAsync(id);
            return user == null || user.IsDeleted ? null : user;
        }

        /// <summary>Ends the user's sessions (their refresh tokens stop working).</summary>
        private async Task RevokeSessionsAsync(Guid userId)
        {
            var tokens = await _unitOfWork.Repository<RefreshToken>()
                .FindAsync(rt => rt.UserId == userId && !rt.IsRevoked);
            foreach (var token in tokens)
                token.Revoke();
        }

        /// <summary>
        /// Users.TimeZone is NOT NULL, so "clear this field" is not expressible:
        /// a blank value keeps the existing zone, falling back to UTC only if
        /// there is nothing to keep.
        /// </summary>
        private static void ApplyTimeZone(User entity, string? timeZone)
        {
            if (!string.IsNullOrWhiteSpace(timeZone))
                entity.TimeZone = timeZone.Trim();
            else if (string.IsNullOrWhiteSpace(entity.TimeZone))
                entity.TimeZone = "UTC";
        }

        private static void ApplyGender(User entity, string? gender)
        {
            if (EnumText.TryParseDefined<Gender>(gender, out var parsed))
                entity.Gender = parsed;
        }

        private static string? NullIfBlank(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
