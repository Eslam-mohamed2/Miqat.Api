using Microsoft.Extensions.Configuration;
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
        private readonly string _googleClientId;

        public AuthService(
            IUnitOfWork unitOfWork,
            IJwtService jwtService,
            IOptions<JwtSettings> jwtSettings,
            IEmailService emailService,
            IConfiguration configuration)
        {
            _unitOfWork = unitOfWork;
            _jwtService = jwtService;
            _jwtSettings = jwtSettings.Value;
            _emailService = emailService;
            _googleClientId = configuration["GoogleAuthSettings:ClientId"] ?? string.Empty;
        }

        // ── Login ─────────────────────────────────────────────────────────────
        public async Task<AuthResponseDto> LoginAsync(
            LoginRequestDto request, string ipAddress)
        {
            var spec = new UserByEmailSpec(request.Email);
            var user = await _unitOfWork.Repository<User>()
                .GetEntityWithSpec(spec);

            if (user == null)
                throw new UnauthorizedAccessException("Invalid email or password.");

            if (string.IsNullOrEmpty(user.PasswordHash))
                throw new UnauthorizedAccessException("Invalid email or password.");

            if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
                throw new UnauthorizedAccessException("Invalid email or password.");

            // ✅ ADD THIS
            if (!user.IsVerified)
                throw new UnauthorizedAccessException(
                    "Account is not verified. Please check your email for the OTP.");

            if (!user.IsActive)
                throw new UnauthorizedAccessException("Account is disabled.");

            var accessToken = _jwtService.GenerateAccessToken(user);
            var refreshToken = _jwtService.GenerateRefreshToken();

            var refreshTokenEntity = new RefreshToken(
                refreshToken,
                user.Id,
                DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpiryDays),
                ipAddress);

            await _unitOfWork.Repository<RefreshToken>().AddAsync(refreshTokenEntity);
            await _unitOfWork.CompleteAsync();

            return BuildAuthResponse(user, accessToken, refreshToken);
        }

        // ── Refresh Token ─────────────────────────────────────────────────────
        public async Task<AuthResponseDto> RefreshTokenAsync(
            string refreshToken, string ipAddress)
        {
            var tokens = await _unitOfWork.Repository<RefreshToken>()
                .FindAsync(rt => rt.Token == refreshToken);

            var tokenEntity = tokens.FirstOrDefault();

            if (tokenEntity == null || !tokenEntity.IsActive)
                throw new UnauthorizedAccessException("Invalid or expired refresh token.");

            var user = await _unitOfWork.Repository<User>()
                .GetByIdAsync(tokenEntity.UserId);

            if (user == null || !user.IsActive)
                throw new UnauthorizedAccessException("User not found or disabled.");

            tokenEntity.Revoke(ipAddress);
            _unitOfWork.Repository<RefreshToken>().Update(tokenEntity);

            var newAccessToken = _jwtService.GenerateAccessToken(user);
            var newRefreshToken = _jwtService.GenerateRefreshToken();

            var newTokenEntity = new RefreshToken(
                newRefreshToken,
                user.Id,
                DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpiryDays),
                ipAddress);

            await _unitOfWork.Repository<RefreshToken>().AddAsync(newTokenEntity);
            await _unitOfWork.CompleteAsync();

            return BuildAuthResponse(user, newAccessToken, newRefreshToken);
        }

        // ── Logout ────────────────────────────────────────────────────────────
        public async Task<bool> LogoutAsync(string refreshToken, string ipAddress)
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

        // ── Register ──────────────────────────────────────────────────────────
        public async Task<bool> RegisterAsync(RegisterRequestDto request)
        {
            var existing = await _unitOfWork.Repository<User>()
                .FindAsync(u => u.Email == request.Email);
            if (existing.Any())
                throw new ApiException("Email already registered.", 409);

            var user = new User(
                request.FullName,
                request.Email,
                DateTime.UtcNow.AddYears(-20),
                null,
                request.Country,
                request.PhoneNumber,
                request.TimeZone ?? "UTC"
            );

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
            user.IsVerified = false;
            user.IsActive = true;

            await _unitOfWork.Repository<User>().AddAsync(user);
            await _unitOfWork.CompleteAsync();

            var otpCode = GenerateOtp();
            var otp = new OtpCode(otpCode, user.Id, "EmailVerification", 10);
            await _unitOfWork.Repository<OtpCode>().AddAsync(otp);
            await _unitOfWork.CompleteAsync();

            try
            {
                await _emailService.SendOtpAsync(user.Email, user.FullName, otpCode);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Email] Failed: {ex.Message}");
                Console.WriteLine($"[Dev OTP] {user.Email} | Code: {otpCode}");
            }
            return true;
        }

        // ── Verify OTP ────────────────────────────────────────────────────────
        public async Task<AuthResponseDto> VerifyOtpAsync(
            VerifyOtpDto request, string ipAddress)
            {
                // 1. Find user
                var users = await _unitOfWork.Repository<User>()
                    .FindAsync(u => u.Email == request.Email);
                var user = users.FirstOrDefault()
                    ?? throw new ApiException("User not found.", 404);

                // 2. Default purpose if not provided
                var purpose = string.IsNullOrWhiteSpace(request.Purpose)
                    ? "EmailVerification"
                    : request.Purpose;

                // 3. Only block re-verification for EmailVerification purpose
                if (purpose == "EmailVerification" && user.IsVerified)
                    throw new ApiException("Account is already verified. Please login.", 400);

                // 4. Find a valid (unused) OTP matching email + code + purpose
                var otps = await _unitOfWork.Repository<OtpCode>()
                    .FindAsync(o =>
                        o.UserId == user.Id &&
                        o.Code == request.Code &&
                        o.Purpose == purpose &&
                        !o.IsUsed);

                var otp = otps.FirstOrDefault();

                // 5. Null check first, then expiry check (order matters for clear error messages)
                if (otp == null)
                    throw new ApiException(
                        "Invalid OTP. Please check the code or request a new one.", 400);

                if (otp.IsExpired)
                    throw new ApiException(
                        "OTP has expired. Please request a new code.", 400);

                // 6. Mark OTP as used
                otp.MarkAsUsed();
                _unitOfWork.Repository<OtpCode>().Update(otp);

                // 7. Only mark user as verified for EmailVerification purpose
                if (purpose == "EmailVerification")
                {
                    user.IsVerified = true;
                    user.SetUpdated();
                    _unitOfWork.Repository<User>().Update(user);
                }

                await _unitOfWork.CompleteAsync();

                // 8. Send welcome email only for EmailVerification
                if (purpose == "EmailVerification")
                {
                    try
                    {
                        await _emailService.SendWelcomeAsync(user.Email, user.FullName);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Email] Welcome email failed: {ex.Message}");
                    }
                }

                // 9. Generate tokens and return auth response
                var accessToken = _jwtService.GenerateAccessToken(user);
                var refreshToken = _jwtService.GenerateRefreshToken();

                var tokenEntity = new RefreshToken(
                    refreshToken, user.Id,
                    DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpiryDays),
                    ipAddress);

                await _unitOfWork.Repository<RefreshToken>().AddAsync(tokenEntity);
                await _unitOfWork.CompleteAsync();

                return BuildAuthResponse(user, accessToken, refreshToken);
        }
        // ── Forgot Password ───────────────────────────────────────────────────
        public async Task<bool> ForgotPasswordAsync(string email)
        {
            var users = await _unitOfWork.Repository<User>()
                .FindAsync(u => u.Email == email);
            var user = users.FirstOrDefault();

            if (user == null) return true;

            // Invalidate old password reset OTPs
            var existingOtps = await _unitOfWork.Repository<OtpCode>()
                .FindAsync(o =>
                    o.UserId == user.Id &&
                    o.Purpose == "PasswordReset" &&
                    !o.IsUsed);

            foreach (var existing in existingOtps)
            {
                existing.MarkAsUsed();
                _unitOfWork.Repository<OtpCode>().Update(existing);
            }
            await _unitOfWork.CompleteAsync();

            var otpCode = GenerateOtp();
            var otp = new OtpCode(otpCode, user.Id, "PasswordReset", 10);
            await _unitOfWork.Repository<OtpCode>().AddAsync(otp);
            await _unitOfWork.CompleteAsync();

            try
            {
                await _emailService.SendPasswordResetOtpAsync(
                    user.Email, user.FullName, otpCode);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Email] Failed: {ex.Message}");
                Console.WriteLine(
                    $"[Dev OTP] Email: {user.Email} | PasswordReset | Code: {otpCode}");
            }

            return true;
        }

        // ── Reset Password ────────────────────────────────────────────────────
        public async Task<bool> ResetPasswordAsync(ResetPasswordDto request)
        {
            var users = await _unitOfWork.Repository<User>()
                .FindAsync(u => u.Email == request.Email);
            var user = users.FirstOrDefault()
                ?? throw new ApiException("User not found.", 404);

            var otps = await _unitOfWork.Repository<OtpCode>()
                .FindAsync(o =>
                    o.UserId == user.Id &&
                    o.Code == request.Token &&
                    o.Purpose == "PasswordReset" &&
                    !o.IsUsed);

            var otp = otps.FirstOrDefault();

            if (otp == null)
                throw new ApiException(
                    "Invalid OTP. Please check the code or request a new one.", 400);

            if (otp.IsExpired)
                throw new ApiException(
                    "OTP has expired. Please request a new code.", 400);

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
            user.SetUpdated();
            _unitOfWork.Repository<User>().Update(user);

            otp.MarkAsUsed();
            _unitOfWork.Repository<OtpCode>().Update(otp);

            await _unitOfWork.CompleteAsync();
            return true;
        }

        // ── Resend OTP ────────────────────────────────────────────────────────
        public async Task<bool> ResendOtpAsync(ResendOtpDto request)
        {
            var users = await _unitOfWork.Repository<User>()
                .FindAsync(u => u.Email == request.Email);
            var user = users.FirstOrDefault()
                ?? throw new ApiException("User not found.", 404);

            if (request.Purpose != "EmailVerification" &&
                request.Purpose != "PasswordReset")
                throw new ApiException(
                    "Invalid purpose. Use EmailVerification or PasswordReset.", 400);

            if (request.Purpose == "EmailVerification" && user.IsVerified)
                throw new ApiException("Account is already verified.", 400);

            // Invalidate old OTPs
            var existingOtps = await _unitOfWork.Repository<OtpCode>()
                .FindAsync(o =>
                    o.UserId == user.Id &&
                    o.Purpose == request.Purpose &&
                    !o.IsUsed);

            foreach (var existing in existingOtps)
            {
                existing.MarkAsUsed();
                _unitOfWork.Repository<OtpCode>().Update(existing);
            }
            await _unitOfWork.CompleteAsync();

            var otpCode = GenerateOtp();
            var otp = new OtpCode(otpCode, user.Id, request.Purpose, 10);
            await _unitOfWork.Repository<OtpCode>().AddAsync(otp);
            await _unitOfWork.CompleteAsync();

            try
            {
                if (request.Purpose == "EmailVerification")
                    await _emailService.SendOtpAsync(
                        user.Email, user.FullName, otpCode);
                else
                    await _emailService.SendPasswordResetOtpAsync(
                        user.Email, user.FullName, otpCode);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Email] Failed: {ex.Message}");
                Console.WriteLine(
                    $"[Dev OTP] {user.Email} | {request.Purpose} | Code: {otpCode}");
            }

            return true;
        }

        // ── Change Password ───────────────────────────────────────────────────
        public async Task<bool> ChangePasswordAsync(
            Guid userId, string currentPassword, string newPassword)
        {
            var user = await _unitOfWork.Repository<User>().GetByIdAsync(userId)
                ?? throw new ApiException("User not found.", 404);

            if (string.IsNullOrEmpty(user.PasswordHash))
                throw new ApiException(
                    "Cannot change password for Google accounts.", 400);

            if (!BCrypt.Net.BCrypt.Verify(currentPassword, user.PasswordHash))
                throw new ApiException("Current password is incorrect.", 400);

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
            user.SetUpdated();
            _unitOfWork.Repository<User>().Update(user);
            await _unitOfWork.CompleteAsync();

            return true;
        }

        // ── Google Login ──────────────────────────────────────────────────────
        public async Task<AuthResponseDto> GoogleLoginAsync(
            string googleToken, string ipAddress)
        {
            var payload = await VerifyGoogleTokenAsync(googleToken)
                ?? throw new ApiException("Invalid Google token.", 401);

            // Make sure Google confirms the email ownership
            if (payload.EmailVerified != true)
                throw new ApiException("Google account email is not verified.", 401);

            var users = await _unitOfWork.Repository<User>()
                .FindAsync(u => u.Email == payload.Email);
            var user = users.FirstOrDefault();

            if (user == null)
            {
                // Create a new user for this Google account. Do not set a usable password.
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

                await _unitOfWork.Repository<User>().AddAsync(user);
            }
            else
            {
                // If an existing user logs in with Google, link their account
                // to the Google identity and ensure the account is verified.
                var updated = false;
                if (!user.IsGoogleAccount)
                {
                    user.IsGoogleAccount = true;
                    updated = true;
                }

                if (string.IsNullOrEmpty(user.GoogleId) && !string.IsNullOrEmpty(payload.Subject))
                {
                    user.GoogleId = payload.Subject;
                    updated = true;
                }

                if (!user.IsVerified)
                {
                    user.IsVerified = true;
                    updated = true;
                }

                if (!string.IsNullOrEmpty(payload.Picture) && user.ProfilePictureUrl != payload.Picture)
                {
                    user.ProfilePictureUrl = payload.Picture;
                    updated = true;
                }

                if (updated)
                {
                    user.SetUpdated();
                    _unitOfWork.Repository<User>().Update(user);
                }
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

            return BuildAuthResponse(user, accessToken, refreshToken);
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private static string GenerateOtp()
        {
            var bytes = new byte[4];
            System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
            var value = Math.Abs(BitConverter.ToInt32(bytes, 0)) % 900000 + 100000;
            return value.ToString();
        }

        private AuthResponseDto BuildAuthResponse(
            User user, string accessToken, string refreshToken) => new()
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

        private async Task<Google.Apis.Auth.GoogleJsonWebSignature.Payload?>
            VerifyGoogleTokenAsync(string token)
        {
            try
            {
                var settings =
                    new Google.Apis.Auth.GoogleJsonWebSignature.ValidationSettings
                    {
                        Audience = string.IsNullOrEmpty(_googleClientId)
                            ? null
                            : new[] { _googleClientId }
                    };
                return await Google.Apis.Auth.GoogleJsonWebSignature
                    .ValidateAsync(token, settings);
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[Google Auth] Token validation failed: {ex.Message}");
                return null;
            }
        }
    }
}