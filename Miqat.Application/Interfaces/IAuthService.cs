using Miqat.Application.Modules;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Miqat.Application.Interfaces
{
    public interface IAuthService
    {
        Task<AuthResponseDto> LoginAsync(LoginRequestDto request, string ipAddress);
        Task<AuthResponseDto> RefreshTokenAsync(string refreshToken, string ipAddress);
        Task<bool> LogoutAsync(string refreshToken, string ipAddress);
        Task<bool> RegisterAsync(RegisterRequestDto request);
        Task<AuthResponseDto> VerifyOtpAsync(VerifyOtpDto request, string ipAddress);
        Task<bool> ForgotPasswordAsync(string email);
        Task<bool> ResetPasswordAsync(ResetPasswordDto request);
        Task<AuthResponseDto> GoogleLoginAsync(string googleToken, string ipAddress);
    }
}
