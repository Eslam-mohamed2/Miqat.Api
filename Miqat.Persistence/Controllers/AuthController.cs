using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Miqat.Application.Common;
using Miqat.Application.Interfaces;
using Miqat.Application.Modules;
using System.Security.Claims;

namespace Miqat.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private const int MaxIpLength = 50; // RefreshToken.CreatedByIp / RevokedByIp column size

        private readonly IAuthService _authService;

        public AuthController(IAuthService authService)
        {
            _authService = authService;
        }

        /// <summary>
        /// Client IP for the refresh-token audit columns. Behind a proxy this is
        /// already the forwarded address (see UseForwardedHeaders in Program.cs).
        /// The raw X-Forwarded-For header is not read here: it is client-supplied
        /// and a long chain used to overflow the 50-character column, turning a
        /// login into a 500.
        /// </summary>
        private string GetIpAddress()
        {
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            return ip.Length > MaxIpLength ? ip[..MaxIpLength] : ip;
        }

        [HttpPost("login")]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> Login([FromBody] LoginRequestDto request)
        {
            try
            {
                var result = await _authService.LoginAsync(request, GetIpAddress());
                return Ok(result);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (ApiException ex)
            {
                return StatusCode(ex.StatusCode, new { message = ex.Message });
            }
        }

        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh(
            [FromBody] RefreshTokenRequestDto request)
        {
            try
            {
                var result = await _authService
                    .RefreshTokenAsync(request.RefreshToken, GetIpAddress());
                return Ok(result);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (ApiException ex)
            {
                return StatusCode(ex.StatusCode, new { message = ex.Message });
            }
        }

        [HttpPost("logout")]
        public async Task<IActionResult> Logout(
            [FromBody] RefreshTokenRequestDto request)
        {
            try
            {
                var result = await _authService
                    .LogoutAsync(request.RefreshToken, GetIpAddress());

                return result
                    ? Ok(new { message = "Logged out successfully." })
                    : BadRequest(new { message = "Invalid token." });
            }
            catch (ApiException ex)
            {
                return StatusCode(ex.StatusCode, new { message = ex.Message });
            }
        }

        [HttpPost("register")]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> Register([FromBody] RegisterRequestDto request)
        {
            try
            {
                await _authService.RegisterAsync(request);
                return Ok(new { message = "Registration successful! Please check your email for the OTP." });
            }
            catch (ApiException ex)
            {
                return StatusCode(ex.StatusCode, new { message = ex.Message });
            }
        }

        /// <summary>
        /// purpose = EmailVerification (default): verifies the account and signs the user in.
        /// purpose = PasswordReset: only checks the code. The client then sends that
        /// same code to reset-password as "token", together with the new password.
        /// A reset code never signs anyone in.
        /// </summary>
        /// <remarks>
        /// The reset response deliberately has no field called "token" or
        /// "accessToken": the web client stores any such field as the session
        /// token, and a 6-digit code is not one.
        /// </remarks>
        [HttpPost("verify-otp")]
        [EnableRateLimiting("otp")]
        public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpDto request)
        {
            try
            {
                if (string.Equals(request.Purpose, "PasswordReset", StringComparison.OrdinalIgnoreCase))
                {
                    var resetToken = await _authService.VerifyPasswordResetOtpAsync(request);
                    return Ok(new
                    {
                        verified = true,
                        message = "Code verified. You can now set a new password.",
                        resetToken
                    });
                }

                if (!string.IsNullOrWhiteSpace(request.Purpose) &&
                    !string.Equals(request.Purpose, "EmailVerification", StringComparison.OrdinalIgnoreCase))
                    return BadRequest(new { message = "Invalid purpose. Use EmailVerification or PasswordReset." });

                var result = await _authService.VerifyOtpAsync(request, GetIpAddress());
                return Ok(result);
            }
            catch (ApiException ex)
            {
                return StatusCode(ex.StatusCode, new { message = ex.Message });
            }
        }

        [HttpPost("forgot-password")]
        [EnableRateLimiting("otp")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto request)
        {
            try
            {
                await _authService.ForgotPasswordAsync(request.Email);
                return Ok(new { message = "If this email exists, a reset code has been sent." });
            }
            catch (ApiException ex)
            {
                return StatusCode(ex.StatusCode, new { message = ex.Message });
            }
        }

        [HttpPost("reset-password")]
        [EnableRateLimiting("otp")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto request)
        {
            try
            {
                await _authService.ResetPasswordAsync(request);
                return Ok(new { message = "Password reset successfully." });
            }
            catch (ApiException ex)
            {
                return StatusCode(ex.StatusCode, new { message = ex.Message });
            }
        }

        [HttpPost("google")]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> GoogleLogin([FromBody] GoogleLoginDto request)
        {
            try
            {
                var result = await _authService.GoogleLoginAsync(request.Token, GetIpAddress());
                return Ok(result);
            }
            catch (ApiException ex)
            {
                return StatusCode(ex.StatusCode, new { message = ex.Message });
            }
        }

        [HttpPost("resend-otp")]
        [EnableRateLimiting("otp")]
        public async Task<IActionResult> ResendOtp([FromBody] ResendOtpDto request)
        {
            try
            {
                await _authService.ResendOtpAsync(request);
                return Ok(new { message = "New OTP sent! Please check your email." });
            }
            catch (ApiException ex)
            {
                return StatusCode(ex.StatusCode, new { message = ex.Message });
            }
        }

        [HttpPost("change-password")]
        [EnableRateLimiting("otp")]
        [Authorize]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto request)
        {
            try
            {
                var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

                await _authService.ChangePasswordAsync(
                    userId,
                    request.CurrentPassword,
                    request.NewPassword);

                return Ok(new { message = "Password changed successfully." });
            }
            catch (ApiException ex)
            {
                return StatusCode(ex.StatusCode, new { message = ex.Message });
            }
        }

        /// <summary>
        /// Changing the sign-in address, step 1: checks the password and sends a
        /// code to the new address. Nothing changes until that code is confirmed.
        /// </summary>
        [HttpPost("change-email")]
        [EnableRateLimiting("otp")]
        [Authorize]
        public async Task<IActionResult> ChangeEmail([FromBody] ChangeEmailDto request)
        {
            try
            {
                await _authService.RequestEmailChangeAsync(CurrentUserId(), request.NewEmail, request.CurrentPassword);
                return Ok(new { message = "We sent a code to your new email address." });
            }
            catch (ApiException ex)
            {
                return StatusCode(ex.StatusCode, new { message = ex.Message });
            }
        }

        /// <summary>Step 2: the code that arrived at the new address makes it the sign-in address.</summary>
        [HttpPost("change-email/confirm")]
        [EnableRateLimiting("otp")]
        [Authorize]
        public async Task<IActionResult> ConfirmEmailChange([FromBody] ConfirmEmailChangeDto request)
        {
            try
            {
                var email = await _authService.ConfirmEmailChangeAsync(CurrentUserId(), request.Code);
                return Ok(new { message = "Your email address was changed.", email });
            }
            catch (ApiException ex)
            {
                return StatusCode(ex.StatusCode, new { message = ex.Message });
            }
        }

        private Guid CurrentUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    }
}
