using Microsoft.Extensions.Options;
using Miqat.Application.Common;
using Miqat.Application.Interfaces;
using Miqat.Application.Modules;
using Miqat.Application.Specifications.Users;
using Miqat.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Miqat.infrastructure.persistence.Services
{
    public class AuthService : IAuthService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IJwtService _jwtService;
        private readonly JwtSettings _jwtSettings;

        public AuthService(
            IUnitOfWork unitOfWork,
            IJwtService jwtService,
            IOptions<JwtSettings> jwtSettings)
        {
            _unitOfWork = unitOfWork;
            _jwtService = jwtService;
            _jwtSettings = jwtSettings.Value;
        }

        public async Task<AuthResponseDto> LoginAsync(
            LoginRequestDto request, string ipAddress)
        {
            // 1. Find user by email
            var spec = new UserByEmailSpec(request.Email);
            var user = await _unitOfWork.Repository<User>()
                .GetEntityWithSpec(spec);

            if (user == null)
                throw new UnauthorizedAccessException("Invalid email or password.");

            // 2. Verify password
            if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
                throw new UnauthorizedAccessException("Invalid email or password.");

            // 3. Check account is active
            if (!user.IsActive)
                throw new UnauthorizedAccessException("Account is disabled.");

            // 4. Generate tokens
            var accessToken = _jwtService.GenerateAccessToken(user);
            var refreshToken = _jwtService.GenerateRefreshToken();

            // 5. Save refresh token to DB
            var refreshTokenEntity = new RefreshToken(
                refreshToken,
                user.Id,
                DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpiryDays),
                ipAddress
            );

            await _unitOfWork.Repository<RefreshToken>()
                .AddAsync(refreshTokenEntity);
            await _unitOfWork.CompleteAsync();

            return new AuthResponseDto
            {
                UserId = user.Id,
                FullName = user.FullName,
                Email = user.Email,
                Role = user.Role.ToString(),
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                AccessTokenExpiry = DateTime.UtcNow.AddMinutes(
                    _jwtSettings.AccessTokenExpiryMinutes)
            };
        }

        public async Task<AuthResponseDto> RefreshTokenAsync(
            string refreshToken, string ipAddress)
        {
            // 1. Find the refresh token in DB
            var tokens = await _unitOfWork.Repository<RefreshToken>()
                .FindAsync(rt => rt.Token == refreshToken);

            var tokenEntity = tokens.FirstOrDefault();

            if (tokenEntity == null || !tokenEntity.IsActive)
                throw new UnauthorizedAccessException("Invalid or expired refresh token.");

            // 2. Get the user
            var user = await _unitOfWork.Repository<User>()
                .GetByIdAsync(tokenEntity.UserId);

            if (user == null || !user.IsActive)
                throw new UnauthorizedAccessException("User not found or disabled.");

            // 3. Revoke old refresh token
            tokenEntity.Revoke(ipAddress);
            _unitOfWork.Repository<RefreshToken>().Update(tokenEntity);

            // 4. Generate new tokens
            var newAccessToken = _jwtService.GenerateAccessToken(user);
            var newRefreshToken = _jwtService.GenerateRefreshToken();

            // 5. Save new refresh token
            var newTokenEntity = new RefreshToken(
                newRefreshToken,
                user.Id,
                DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpiryDays),
                ipAddress
            );

            await _unitOfWork.Repository<RefreshToken>()
                .AddAsync(newTokenEntity);
            await _unitOfWork.CompleteAsync();

            return new AuthResponseDto
            {
                UserId = user.Id,
                FullName = user.FullName,
                Email = user.Email,
                Role = user.Role.ToString(),
                AccessToken = newAccessToken,
                RefreshToken = newRefreshToken,
                AccessTokenExpiry = DateTime.UtcNow.AddMinutes(
                    _jwtSettings.AccessTokenExpiryMinutes)
            };
        }

        public async Task<bool> LogoutAsync(
            string refreshToken, string ipAddress)
        {
            var tokens = await _unitOfWork.Repository<RefreshToken>()
                .FindAsync(rt => rt.Token == refreshToken);

            var tokenEntity = tokens.FirstOrDefault();

            if (tokenEntity == null || !tokenEntity.IsActive)
                return false;

            tokenEntity.Revoke(ipAddress);
            _unitOfWork.Repository<RefreshToken>().Update(tokenEntity);
            await _unitOfWork.CompleteAsync();

            return true;
        }
    }

}
