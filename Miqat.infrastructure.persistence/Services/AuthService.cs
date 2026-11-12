using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Miqat.Application.Common;
using Miqat.Application.Interfaces;
using Miqat.Application.Modules;
using Miqat.Application.Specifications.Users;
using Miqat.Domain.Entities;
using System.Security.Cryptography;

namespace Miqat.infrastructure.persistence.Services
{
    public class AuthService : IAuthService
    {
        private const string EmailVerification = "EmailVerification";
        private const string PasswordReset = "PasswordReset";
        private const string EmailChange = "EmailChange";
        private const int OtpExpiryMinutes = 10;

        // One message for "no such user", "wrong code" and "used code", so the
        // OTP endpoints cannot be used to find out which emails are registered.
        private const string InvalidOtpMessage =
            "Invalid OTP. Please check the code or request a new one.";

        private readonly IUnitOfWork _unitOfWork;
        private readonly IJwtService _jwtService;
        private readonly JwtSettings _jwtSettings;
        private readonly IEmailService _emailService;
        private readonly IHostEnvironment _environment;
        private readonly string _googleClientId;

        public AuthService(
            IUnitOfWork unitOfWork,
            IJwtService jwtService,
            IOptions<JwtSettings> jwtSettings,
            IEmailService emailService,
            IConfiguration configuration,
            IHostEnvironment environment)
        {
            _unitOfWork = unitOfWork;
            _jwtService = jwtService;
            _jwtSettings = jwtSettings.Value;
            _emailService = emailService;
            _environment = environment;
            _googleClientId = configuration["GoogleAuthSettings:ClientId"] ?? string.Empty;
        }

        // ── Login ─────────────────────────────────────────────────────────────
        public async Task<AuthResponseDto> LoginAsync(
            LoginRequestDto request, string ipAddress)
        {
            var user = await FindUserByEmailAsync(request.Email);

            if (user == null || string.IsNullOrEmpty(user.PasswordHash) ||
                !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
                throw new UnauthorizedAccessException("Invalid email or password.");

            if (!user.IsVerified)
                throw new UnauthorizedAccessException(
                    "Account is not verified. Please check your email for the OTP.");

            if (!user.IsActive)
                throw new UnauthorizedAccessException("Account is disabled.");

            return await IssueTokensAsync(user, ipAddress);
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

            if (user == null || user.IsDeleted || !user.IsActive)
                throw new UnauthorizedAccessException("User not found or disabled.");

            tokenEntity.Revoke(ipAddress);

            return await IssueTokensAsync(user, ipAddress);
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
            await _unitOfWork.CompleteAsync();

            return true;
        }

        // ── Register ──────────────────────────────────────────────────────────
        public async Task<bool> RegisterAsync(RegisterRequestDto request)
        {
            var email = NormalizeEmail(request.Email);

            // Includes soft-deleted accounts: the unique index on Email covers them too.
            var existing = await _unitOfWork.Repository<User>()
                .FindAsync(u => u.Email.ToLower() == email);
            if (existing.Any())
                throw new ApiException("Email already registered.", 409);

            var user = new User(
                request.FullName.Trim(),
                email,
                DateTime.UtcNow.AddYears(-20),
                null,
                string.IsNullOrWhiteSpace(request.Country) ? null : request.Country.Trim(),
                string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim(),
                string.IsNullOrWhiteSpace(request.TimeZone) ? "UTC" : request.TimeZone.Trim()
            );

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
            user.IsVerified = false;
            user.IsActive = true;

            var otpCode = GenerateOtp();

            await _unitOfWork.Repository<User>().AddAsync(user);
            await _unitOfWork.Repository<OtpCode>()
                .AddAsync(new OtpCode(otpCode, user.Id, EmailVerification, OtpExpiryMinutes));
            await _unitOfWork.CompleteAsync();

            // The account now exists either way, so a delivery failure must be
            // reported rather than swallowed: telling the caller "check your
            // email" when nothing was sent leaves them stuck on a screen waiting
            // for a code that is never coming.
            if (!await TrySendOtpEmailAsync(user, EmailVerification, otpCode))
                throw new ApiException(
                    "Your account was created, but the verification email could not be sent. " +
                    "Use \"Resend code\" in a moment, or contact support.", 502);

            return true;
        }

        // ── Verify OTP (email verification) ───────────────────────────────────
        public async Task<AuthResponseDto> VerifyOtpAsync(
            VerifyOtpDto request, string ipAddress)
        {
            var user = await FindUserByEmailAsync(request.Email)
                ?? throw new ApiException(InvalidOtpMessage, 400);

            if (user.IsVerified)
                throw new ApiException("Account is already verified. Please login.", 400);

            if (!user.IsActive)
                throw new ApiException("Account is disabled.", 401);

            var otp = await FindUsableOtpAsync(user.Id, request.Code, EmailVerification);

            otp.MarkAsUsed();
            user.IsVerified = true;
            user.SetUpdated();
            await _unitOfWork.CompleteAsync();

            try
            {
                await _emailService.SendWelcomeAsync(user.Email, user.FullName);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Email] Welcome email failed: {ex.Message}");
            }

            return await IssueTokensAsync(user, ipAddress);
        }

        // ── Verify OTP (password reset, step 2 of 3) ──────────────────────────
        public async Task<string> VerifyPasswordResetOtpAsync(VerifyOtpDto request)
        {
            var user = await FindUserByEmailAsync(request.Email)
                ?? throw new ApiException(InvalidOtpMessage, 400);

            // Only checked here. The code stays valid and is consumed by
            // reset-password, which receives it back as "token".
            var otp = await FindUsableOtpAsync(user.Id, request.Code, PasswordReset);
            return otp.Code;
        }

        // ── Forgot Password ───────────────────────────────────────────────────
        public async Task<bool> ForgotPasswordAsync(string email)
        {
            var user = await FindUserByEmailAsync(email);

            // Always "succeeds", so the endpoint does not reveal who is registered.
            if (user == null || !user.IsActive) return true;

            var otpCode = await ReplaceOtpAsync(user, PasswordReset);

            // Trade-off, made deliberately: a transport failure is reported even
            // though the "user not found" path above returns success, so while
            // the mail relay is down a real address can be told from a fake one.
            // That window only exists while the feature is already broken, and it
            // beats showing a success screen for an email that was never sent.
            if (!await TrySendOtpEmailAsync(user, PasswordReset, otpCode))
                throw new ApiException(
                    "We could not send the reset email right now. Please try again shortly.", 502);

            return true;
        }

        // ── Reset Password ────────────────────────────────────────────────────
        public async Task<bool> ResetPasswordAsync(ResetPasswordDto request)
        {
            var user = await FindUserByEmailAsync(request.Email)
                ?? throw new ApiException(InvalidOtpMessage, 400);

            if (!user.IsActive)
                throw new ApiException(InvalidOtpMessage, 400);

            var otp = await FindUsableOtpAsync(user.Id, request.Token, PasswordReset);

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
            user.SetUpdated();
            otp.MarkAsUsed();

            // Whoever knew the old password must not keep a session.
            await RevokeAllRefreshTokensAsync(user.Id);

            await _unitOfWork.CompleteAsync();
            return true;
        }

        // ── Resend OTP ────────────────────────────────────────────────────────
        public async Task<bool> ResendOtpAsync(ResendOtpDto request)
        {
            if (request.Purpose != EmailVerification && request.Purpose != PasswordReset)
                throw new ApiException(
                    "Invalid purpose. Use EmailVerification or PasswordReset.", 400);

            var user = await FindUserByEmailAsync(request.Email);

            // Same reply whether or not the email is registered.
            if (user == null || !user.IsActive) return true;

            if (request.Purpose == EmailVerification && user.IsVerified)
                throw new ApiException("Account is already verified.", 400);

            var otpCode = await ReplaceOtpAsync(user, request.Purpose);

            if (!await TrySendOtpEmailAsync(user, request.Purpose, otpCode))
                throw new ApiException(
                    "We could not send the code right now. Please try again shortly.", 502);

            return true;
        }

        // ── Change Password ───────────────────────────────────────────────────
        public async Task<bool> ChangePasswordAsync(
            Guid userId, string currentPassword, string newPassword)
        {
            var user = await _unitOfWork.Repository<User>().GetByIdAsync(userId);
            if (user == null || user.IsDeleted)
                throw new ApiException("User not found.", 404);

            if (string.IsNullOrEmpty(user.PasswordHash))
                throw new ApiException(
                    "Cannot change password for Google accounts.", 400);

            if (!BCrypt.Net.BCrypt.Verify(currentPassword, user.PasswordHash))
                throw new ApiException("Current password is incorrect.", 400);

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
            user.SetUpdated();
            await _unitOfWork.CompleteAsync();

            return true;
        }

        // ── Change email ──────────────────────────────────────────────────────

        /// <summary>
        /// Step 1: checks the password and sends a code to the new address.
        /// The sign-in address itself does not change until that code comes
        /// back, so a typo cannot lock anyone out of their account.
        /// </summary>
        public async Task RequestEmailChangeAsync(Guid userId, string newEmail, string currentPassword)
        {
            var user = await RequireAccountAsync(userId);

            if (string.IsNullOrEmpty(user.PasswordHash))
                throw new ApiException("This account signs in with Google, so its email cannot be changed here.", 400);

            if (!BCrypt.Net.BCrypt.Verify(currentPassword, user.PasswordHash))
                throw new ApiException("Current password is incorrect.", 400);

            var email = NormalizeEmail(newEmail);
            if (email == NormalizeEmail(user.Email))
                throw new ApiException("That is already your email address.", 400);

            await EnsureEmailIsFreeAsync(email);

            user.PendingEmail = email;
            var otpCode = await ReplaceOtpAsync(user, EmailChange);

            if (!await TrySendOtpEmailAsync(user, EmailChange, otpCode, toEmail: email))
                throw new ApiException("We could not send a code to that address. Please try again later.", 503);
        }

        /// <summary>Step 2: the code from the new address makes it the sign-in address.</summary>
        public async Task<string> ConfirmEmailChangeAsync(Guid userId, string code)
        {
            var user = await RequireAccountAsync(userId);

            if (string.IsNullOrEmpty(user.PendingEmail))
                throw new ApiException("There is no email change waiting to be confirmed.", 400);

            var otp = await FindUsableOtpAsync(user.Id, code, EmailChange);

            // Someone else may have registered the address in the meantime.
            await EnsureEmailIsFreeAsync(user.PendingEmail);

            otp.MarkAsUsed();
            user.Email = user.PendingEmail;
            user.PendingEmail = null;
            user.SetUpdated();
            await _unitOfWork.CompleteAsync();

            return user.Email;
        }

        private async Task<User> RequireAccountAsync(Guid userId)
        {
            var user = await _unitOfWork.Repository<User>().GetByIdAsync(userId);
            return user == null || user.IsDeleted ? throw new ApiException("User not found.", 404) : user;
        }

        private async Task EnsureEmailIsFreeAsync(string normalizedEmail)
        {
            if (await FindUserByEmailAsync(normalizedEmail) != null)
                throw new ApiException("That email address is already in use.", 400);
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

            var email = NormalizeEmail(payload.Email);

            // Deleted accounts are included on purpose: they still own the email.
            var users = await _unitOfWork.Repository<User>()
                .FindAsync(u => u.Email.ToLower() == email);
            var user = users.FirstOrDefault();

            if (user == null)
            {
                // Create a new user for this Google account. Do not set a usable password.
                user = new User(
                    string.IsNullOrWhiteSpace(payload.Name) ? email : payload.Name,
                    email,
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
                if (user.IsDeleted || !user.IsActive)
                    throw new ApiException("Account is disabled.", 401);

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

                // Only fill in a missing picture; never overwrite one the user uploaded.
                if (string.IsNullOrEmpty(user.ProfilePictureUrl) && !string.IsNullOrEmpty(payload.Picture))
                {
                    user.ProfilePictureUrl = payload.Picture;
                    updated = true;
                }

                if (updated)
                    user.SetUpdated();
            }

            return await IssueTokensAsync(user, ipAddress);
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private static string NormalizeEmail(string? email)
            => (email ?? string.Empty).Trim().ToLowerInvariant();

        /// <summary>Case-insensitive lookup of a non-deleted user.</summary>
        private async Task<User?> FindUserByEmailAsync(string? email)
        {
            var normalized = NormalizeEmail(email);
            if (normalized.Length == 0) return null;

            return await _unitOfWork.Repository<User>()
                .GetEntityWithSpec(new UserByEmailSpec(normalized));
        }

        /// <summary>Finds the unused, unexpired code or throws a 400.</summary>
        private async Task<OtpCode> FindUsableOtpAsync(Guid userId, string? code, string purpose)
        {
            var trimmed = (code ?? string.Empty).Trim();

            var otps = await _unitOfWork.Repository<OtpCode>()
                .FindAsync(o =>
                    o.UserId == userId &&
                    o.Code == trimmed &&
                    o.Purpose == purpose &&
                    !o.IsUsed);

            var otp = otps.OrderByDescending(o => o.CreatedAt).FirstOrDefault()
                ?? throw new ApiException(InvalidOtpMessage, 400);

            if (otp.IsExpired)
                throw new ApiException("OTP has expired. Please request a new code.", 400);

            return otp;
        }

        /// <summary>Invalidates the user's open codes for this purpose and stores a new one.</summary>
        private async Task<string> ReplaceOtpAsync(User user, string purpose)
        {
            var existingOtps = await _unitOfWork.Repository<OtpCode>()
                .FindAsync(o =>
                    o.UserId == user.Id &&
                    o.Purpose == purpose &&
                    !o.IsUsed);

            foreach (var existing in existingOtps)
                existing.MarkAsUsed();

            var otpCode = GenerateOtp();
            await _unitOfWork.Repository<OtpCode>()
                .AddAsync(new OtpCode(otpCode, user.Id, purpose, OtpExpiryMinutes));
            await _unitOfWork.CompleteAsync();

            return otpCode;
        }

        /// <summary>
        /// Sends an OTP and reports whether the user can actually get the code.
        ///
        /// Callers used to log a failure and carry on, so a dead mail relay
        /// produced a 200 and a green "code sent" banner while nothing was
        /// delivered. Now the caller is told, and decides what the user sees.
        ///
        /// One exception: on a developer machine with no mail provider configured
        /// at all, the code is printed to the log ("[Dev OTP] ...") and that
        /// counts as delivered, so sign-up and password reset work locally
        /// without an email account. Outside Development the code is never
        /// written to the log.
        /// </summary>
        /// <param name="toEmail">
        /// Where the code goes when that is not the account's own address: a
        /// change of email is confirmed from the new address.
        /// </param>
        private async Task<bool> TrySendOtpEmailAsync(User user, string purpose, string otpCode, string? toEmail = null)
        {
            var email = toEmail ?? user.Email;

            try
            {
                var send = purpose switch
                {
                    EmailVerification => _emailService.SendOtpAsync(email, user.FullName, otpCode),
                    EmailChange => _emailService.SendEmailChangeOtpAsync(email, user.FullName, otpCode),
                    _ => _emailService.SendPasswordResetOtpAsync(email, user.FullName, otpCode)
                };
                await send;

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Email] Failed: {ex.Message}");

                if (!_environment.IsDevelopment())
                    return false;

                Console.WriteLine($"[Dev OTP] {email} | {purpose} | Code: {otpCode}");
                return !_emailService.IsConfigured;
            }
        }

        private async Task RevokeAllRefreshTokensAsync(Guid userId)
        {
            var tokens = await _unitOfWork.Repository<RefreshToken>()
                .FindAsync(rt => rt.UserId == userId && !rt.IsRevoked);

            foreach (var token in tokens)
                token.Revoke();
        }

        /// <summary>Stores a new refresh token (plus any pending changes) and builds the response.</summary>
        private async Task<AuthResponseDto> IssueTokensAsync(User user, string ipAddress)
        {
            var accessToken = _jwtService.GenerateAccessToken(user);
            var refreshToken = _jwtService.GenerateRefreshToken();

            await _unitOfWork.Repository<RefreshToken>().AddAsync(new RefreshToken(
                refreshToken,
                user.Id,
                DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpiryDays),
                ipAddress));
            await _unitOfWork.CompleteAsync();

            return new AuthResponseDto
            {
                UserId = user.Id,
                FullName = user.FullName,
                Email = user.Email,
                Role = user.Role.ToString(),
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                AccessTokenExpiry = DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpiryMinutes)
            };
        }

        private static string GenerateOtp()
            => RandomNumberGenerator.GetInt32(100000, 1000000).ToString();

        private async Task<Google.Apis.Auth.GoogleJsonWebSignature.Payload?>
            VerifyGoogleTokenAsync(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return null;

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
