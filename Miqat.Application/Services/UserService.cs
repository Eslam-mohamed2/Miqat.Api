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
        private readonly IUnitOfWork _unitOfWork;
        private readonly UserMapper _mapper;

        public UserService(IUnitOfWork unitOfWork, UserMapper mapper)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
        }

        public async Task<IEnumerable<UserDto>> GetAllUsers()
        {
            var users = await _unitOfWork.Repository<User>().GetAllAsync();
            return _mapper.MapUsersToDtos(users);
        }

        public async Task<UserDto?> GetUserById(Guid id)
        {
            var spec = new UserByIdWithDetailsSpec(id);
            var user = await _unitOfWork.Repository<User>()
                .GetEntityWithSpec(spec);
            return user != null ? _mapper.MapUserToDto(user) : null;
        }

        public async Task<UserDto> CreateAsync(UserDto dto)
        {
            Enum.TryParse<Gender>(dto.Gender, out var gender);

            var entity = new User(
                dto.FullName,
                dto.Email,
                dto.DateOfBirth ?? DateTime.UtcNow,
                gender,
                dto.Country,
                dto.PhoneNumber,
                dto.TimeZone
            );

            await _unitOfWork.Repository<User>().AddAsync(entity);
            await _unitOfWork.CompleteAsync();
            return _mapper.MapUserToDto(entity);
        }

        public async Task<bool> UpdateAsync(Guid id, UserDto dto)
        {
            var entity = await _unitOfWork.Repository<User>().GetByIdAsync(id);
            if (entity == null) return false;

            entity.FullName = dto.FullName;
            entity.Email = dto.Email;
            entity.Country = dto.Country;
            entity.DateOfBirth = dto.DateOfBirth;
            entity.PhoneNumber = dto.PhoneNumber;
            entity.ProfilePictureUrl = dto.ProfilePictureUrl;
            entity.TimeZone = dto.TimeZone;

            if (Enum.TryParse<Gender>(dto.Gender, out var gender))
                entity.Gender = gender;

            entity.SetUpdated();
            _unitOfWork.Repository<User>().Update(entity);
            return await _unitOfWork.CompleteAsync() > 0;
        }

        public async Task<bool> DeleteAsync(Guid id)
        {
            var entity = await _unitOfWork.Repository<User>().GetByIdAsync(id);
            if (entity == null) return false;
            entity.SoftDelete();
            _unitOfWork.Repository<User>().Update(entity);
            return await _unitOfWork.CompleteAsync() > 0;
        }

        public async Task<IEnumerable<UserDto>> SearchAsync(string query, Guid excludeUserId)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new List<UserDto>();

            var lowerQuery = query.ToLower();
            var allUsers = await _unitOfWork.Repository<User>().GetAllAsync();

            var matchedUsers = allUsers
                .Where(u => u.Id != excludeUserId &&
                           !u.IsDeleted &&
                           (u.FullName.ToLower().Contains(lowerQuery) ||
                            u.Email.ToLower().Contains(lowerQuery)))
                .Take(20)
                .ToList();

            var dtos = matchedUsers.Select(u => new UserDto
            {
                Id = u.Id,
                FullName = u.FullName,
                Email = u.Email,
                Country = u.Country,
                ProfilePictureUrl = u.ProfilePictureUrl
            }).ToList();

            return dtos;
        }
    }
}
