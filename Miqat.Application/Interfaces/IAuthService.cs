using Miqat.Application.Modules;

namespace Miqat.Application.Interfaces
{
    public interface IAuthService
    {
        Task<AuthResponseDto> LoginAsync(LoginRequestDto request, string ipAddress);
        Task<AuthResponseDto> RefreshTokenAsync(string refreshToken, string ipAddress);
        Task<bool> LogoutAsync(string refreshToken, string ipAddress);
        Task<bool> RegisterAsync(RegisterRequestDto request);
        Task<AuthResponseDto> VerifyOtpAsync(VerifyOtpDto request, string ipAddress);

        /// <summary>
        /// Checks a password-reset code without consuming it and returns the
        /// token the client must send to reset-password.
        /// </summary>
        Task<string> VerifyPasswordResetOtpAsync(VerifyOtpDto request);

        Task<bool> ForgotPasswordAsync(string email);
        Task<bool> ResetPasswordAsync(ResetPasswordDto request);
        Task<AuthResponseDto> GoogleLoginAsync(string googleToken, string ipAddress);
        Task<bool> ResendOtpAsync(ResendOtpDto request);
        Task<bool> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword);

        /// <summary>Step 1 of changing the sign-in address: sends a code to the new address.</summary>
        Task RequestEmailChangeAsync(Guid userId, string newEmail, string currentPassword);

        /// <summary>Step 2: confirms with that code. Returns the new sign-in address.</summary>
        Task<string> ConfirmEmailChangeAsync(Guid userId, string code);
    }
}
