using Miqat.Application.Common;
using Miqat.Application.Interfaces;
using Miqat.Application.Modules;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;


namespace Miqat.Application.Services
{
    public class UserService : IUserService
    {
        readonly  IUnitOfWork _unitOfWork;
        readonly  UserMapper _mapper;

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
        public async Task<UserDto> GetUserById(Guid id)
        {
            var userEnitiy = await _unitOfWork.Repository<User>().GetByIdAsync(id);
            return _mapper.MapUserToDto(userEnitiy);
        }

        public async Task<UserDto> CreateAsync(UserDto user)
        {
            Enum.TryParse<Gender>(user.gender, out var genderValue);

            var userEntity = new User(
                user.FullName,
                user.Email,
                user.DateOfBirth ?? DateTime.Now,
                genderValue,  
                user.Country
            );
            await _unitOfWork.Repository<User>().AddAsync(userEntity);
            await _unitOfWork.CompleteAsync();

            return _mapper.MapUserToDto(userEntity);
        }
        public async Task<bool> UpdateAsync(Guid id, UserDto userDto)
        {
            var userEntity = await _unitOfWork.Repository<User>().GetByIdAsync(id);
            if (userEntity == null) return false;
            userEntity.FullName = userDto.FullName;
            userEntity.Email = userDto.Email;
            userEntity.Country = userDto.Country;
            userEntity.DateOfBirth = userDto.DateOfBirth;
            if (Enum.TryParse<Gender>(userDto.gender, out var genderEnum))
            {
                userEntity.Gender = genderEnum;
            }
            _unitOfWork.Repository<User>().Update(userEntity);
            return await _unitOfWork.CompleteAsync() > 0;
        }
        public async Task<bool> DeleteAsync(Guid id)
        {
            var userEnitiy = _unitOfWork.Repository<User>().GetByIdAsync(id);
            if (userEnitiy == null) return false;
            _unitOfWork.Repository<User>().Delete(userEnitiy.Result);
            return await _unitOfWork.CompleteAsync() > 0;
        }
    }
}
