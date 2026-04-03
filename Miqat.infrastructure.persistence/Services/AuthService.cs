using Microsoft.Extensions.Options;
using Miqat.Application.Common;
using Miqat.Application.Interfaces;
using Miqat.Application.Modules;
using Miqat.Application.Specifications.Users;
using Miqat.Domain.Entities;

namespace Miqat.infrastructure.persistence.Services
{
    public class AuthService : IAuthService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IJwtService _jwtService;
        private readonly JwtSettings _jwtSettings;
        private readonly IEmailService _emailService;

        public AuthService(
            IUnitOfWork unitOfWork,
            IJwtService jwtService,
            IOptions<JwtSettings> jwtSettings,
            IEmailService emailService)
        {
            _unitOfWork = unitOfWork;
            _jwtService = jwtService;
            _jwtSettings = jwtSettings.Value;
            _emailService = emailService;
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

            // ✅ NEW: Guard against null/empty PasswordHash before BCrypt.Verify
            if (string.IsNullOrEmpty(user.PasswordHash))
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

        public async Task<bool> RegisterAsync(RegisterRequestDto request)
        {
            // 1. Check if email already exists
            var existing = await _unitOfWork.Repository<User>()
                .FindAsync(u => u.Email == request.Email);
            if (existing.Any())
                throw new ApiException("Email already registered.", 409);

            // 2. Create user (not verified yet)
            var user = new User(
                request.FullName,
                request.Email,
                DateTime.UtcNow.AddYears(-20),
                null,
                request.Country,
                request.PhoneNumber,
                request.TimeZone
            );
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
            user.IsVerified = false;
            user.IsActive = true;

            await _unitOfWork.Repository<User>().AddAsync(user);
            await _unitOfWork.CompleteAsync();

            // 3. Generate OTP
            var otpCode = GenerateOtp();
            var otp = new OtpCode(otpCode, user.Id, "EmailVerification", 10);
            await _unitOfWork.Repository<OtpCode>().AddAsync(otp);
            await _unitOfWork.CompleteAsync();

            // 4. Send OTP email — won't block registration if email fails
            try
            {
                await _emailService.SendOtpAsync(user.Email, user.FullName, otpCode);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Email] Failed to send OTP: {ex.Message}");
            }

            return true;
        }

        public async Task<AuthResponseDto> VerifyOtpAsync(
            VerifyOtpDto request, string ipAddress)
        {
            // 1. Find user
            var users = await _unitOfWork.Repository<User>()
                .FindAsync(u => u.Email == request.Email);
            var user = users.FirstOrDefault()
                ?? throw new ApiException("User not found.", 404);

            // 2. Find valid OTP
            var otps = await _unitOfWork.Repository<OtpCode>()
                .FindAsync(o =>
                    o.UserId == user.Id &&
                    o.Code == request.Code &&
                    o.Purpose == request.Purpose &&
                    !o.IsUsed);

            var otp = otps.FirstOrDefault()
                ?? throw new ApiException("Invalid or expired OTP.", 400);

            if (!otp.IsValid)
                throw new ApiException("OTP has expired.", 400);

            // 3. Mark OTP as used
            otp.MarkAsUsed();
            _unitOfWork.Repository<OtpCode>().Update(otp);

            // 4. Verify user
            user.IsVerified = true;
            user.SetUpdated();
            _unitOfWork.Repository<User>().Update(user);
            await _unitOfWork.CompleteAsync();

            // 5. Send welcome email
            await _emailService.SendWelcomeAsync(user.Email, user.FullName);

            // 6. Return tokens
            var accessToken = _jwtService.GenerateAccessToken(user);
            var refreshToken = _jwtService.GenerateRefreshToken();

            var tokenEntity = new RefreshToken(
                refreshToken, user.Id,
                DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpiryDays),
                ipAddress);

            await _unitOfWork.Repository<RefreshToken>().AddAsync(tokenEntity);
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

        public async Task<bool> ForgotPasswordAsync(string email)
        {
            var users = await _unitOfWork.Repository<User>()
                .FindAsync(u => u.Email == email);
            var user = users.FirstOrDefault();

            if (user == null) return true;

            var tokenValue = Convert.ToBase64String(
                System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));

            var resetToken = new PasswordResetToken(tokenValue, user.Id, 30);
            await _unitOfWork.Repository<PasswordResetToken>().AddAsync(resetToken);
            await _unitOfWork.CompleteAsync();

            var resetLink = $"https://yourfrontend.com/reset-password?token={Uri.EscapeDataString(tokenValue)}";
            await _emailService.SendPasswordResetAsync(user.Email, user.FullName, resetLink);

            return true;
        }

        public async Task<bool> ResetPasswordAsync(ResetPasswordDto request)
        {
            var tokens = await _unitOfWork.Repository<PasswordResetToken>()
                .FindAsync(t => t.Token == request.Token && !t.IsUsed);

            var resetToken = tokens.FirstOrDefault()
                ?? throw new ApiException("Invalid or expired reset token.", 400);

            if (!resetToken.IsValid)
                throw new ApiException("Reset token has expired.", 400);

            var user = await _unitOfWork.Repository<User>()
                .GetByIdAsync(resetToken.UserId)
                ?? throw new ApiException("User not found.", 404);

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
            user.SetUpdated();
            _unitOfWork.Repository<User>().Update(user);

            resetToken.MarkAsUsed();
            _unitOfWork.Repository<PasswordResetToken>().Update(resetToken);

            await _unitOfWork.CompleteAsync();
            return true;
        }

        public async Task<AuthResponseDto> GoogleLoginAsync(
            string googleToken, string ipAddress)
        {
            var payload = await VerifyGoogleTokenAsync(googleToken)
                ?? throw new ApiException("Invalid Google token.", 401);

            var users = await _unitOfWork.Repository<User>()
                .FindAsync(u => u.Email == payload.Email);
            var user = users.FirstOrDefault();

            if (user == null)
            {
                user = new User(
                    payload.Name ?? payload.Email,
                    payload.Email,
                    DateTime.UtcNow.AddYears(-20),
                    null, null, null, "UTC"
                );
                user.GoogleId = payload.Subject;
                user.IsGoogleAccount = true;
                user.IsVerified = true;
                user.IsActive = true;
                user.ProfilePictureUrl = payload.Picture;
                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(
                    Guid.NewGuid().ToString());

                await _unitOfWork.Repository<User>().AddAsync(user);
                await _unitOfWork.CompleteAsync();
            }

            if (!user.IsActive)
                throw new ApiException("Account is disabled.", 401);

            var accessToken = _jwtService.GenerateAccessToken(user);
            var refreshToken = _jwtService.GenerateRefreshToken();

            var tokenEntity = new RefreshToken(
                refreshToken, user.Id,
                DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpiryDays),
                ipAddress);

            await _unitOfWork.Repository<RefreshToken>().AddAsync(tokenEntity);
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

        private static string GenerateOtp()
        {
            var random = new Random();
            return random.Next(100000, 999999).ToString();
        }

        private static async Task<Google.Apis.Auth.GoogleJsonWebSignature.Payload?> VerifyGoogleTokenAsync(string token)
        {
            try
            {
                return await Google.Apis.Auth.GoogleJsonWebSignature.ValidateAsync(token);
            }
            catch
            {
                return null;
            }
        }
    }
}