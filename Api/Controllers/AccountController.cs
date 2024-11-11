﻿using Api.Data;
using Api.DTOs.Account;
using Api.Models;
using Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AccountController : ControllerBase
    {
        private readonly JWTService _jwtService;
        private readonly SignInManager<User> _signInManager;
        private readonly UserManager<User> _userManager;
        //private readonly EmailService _emailService;
        private readonly Context _context;
        private readonly IConfiguration _config;
        private readonly HttpClient _facebookHttpClient;

        public AccountController(JWTService jwtService,
            SignInManager<User> signInManager,
            UserManager<User> userManager,
            //EmailService emailService,
            Context context,
            IConfiguration config)
        {
            _jwtService = jwtService;
            _signInManager = signInManager;
            _userManager = userManager;
            //_emailService = emailService;
            _context = context;
            _config = config;
            _facebookHttpClient = new HttpClient
            {
                BaseAddress = new Uri("https://graph.facebook.com")
            };
        }

        [Authorize]
        [HttpGet("refresh-user-token")]
        public async Task<ActionResult<UserDto>> RefreshUserToken()
        {
            // var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            //var email = User.FindFirst(ClaimTypes.Email)?.Value;
            var user = await _userManager.FindByEmailAsync(User.FindFirst(ClaimTypes.Email)?.Value);
            //var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            //var user = await _userManager.FindByIdAsync("f4fd7d1e-89d1-4427-aee8-09269ad6e874");
            //var user = await _userManager.FindByNameAsync(User.FindFirst(ClaimTypes.Email)?.Value);
            return await CreateApplicationUserDto(user);
        }

        [Authorize]
        [HttpPost("refresh-token")]
        public async Task<ActionResult<UserDto>> RefereshToken()
        {
            var token = Request.Cookies["identityAppRefreshToken"];

            // JWTService - CreateJWT new Claim(ClaimTypes.NameIdentifier, user.Id),
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;      

            if (IsValidRefreshTokenAsync(userId, token).GetAwaiter().GetResult())
            {
                var user = await _userManager.FindByIdAsync(userId);
                if (user == null) return Unauthorized("Invalid or expired token, please try to login");
                return await CreateApplicationUserDto(user);
            }

            return Unauthorized("Invalid or expired token, please try to login");
        }

        [HttpPost("login")]
        public async Task<ActionResult<UserDto>> Login(LoginDto model)
        {
            var user = await _userManager.FindByNameAsync(model.UserName);
            if (user == null) return Unauthorized("Invalid username or password");

            if (user.EmailConfirmed == false) return Unauthorized("Please confirm your email.");

            var result = await _signInManager.CheckPasswordSignInAsync(user, model.Password, false);

            if (result.IsLockedOut)
            {
                return Unauthorized(string.Format("Your account has been locked. You should wait until {0} (UTC time) to be able to login", user.LockoutEnd));
            }

            if (!result.Succeeded)
            {
                //// User has input an invalid password
                //if (!user.UserName.Equals(SD.AdminUserName))
                //{
                //    // Increamenting AccessFailedCount of the AspNetUser by 1
                //    await _userManager.AccessFailedAsync(user);
                //}

                //if (user.AccessFailedCount >= SD.MaximumLoginAttempts)
                //{
                //    // Lock the user for one day
                //    await _userManager.SetLockoutEndDateAsync(user, DateTime.UtcNow.AddDays(1));
                //    return Unauthorized(string.Format("Your account has been locked. You should wait until {0} (UTC time) to be able to login", user.LockoutEnd));
                //}

                return Unauthorized("Invalid username or password");
            }

            //await _userManager.ResetAccessFailedCountAsync(user);
            //await _userManager.SetLockoutEndDateAsync(user, null);

            return await CreateApplicationUserDto(user);
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register(RegisterDto model)
        {
            if (await CheckEmailExistsAsync(model.Email))
            {
                return BadRequest($"An existing account is using {model.Email}, email addres. Please try with another email address");
            }

            var userToAdd = new User
            {
                FirstName = model.FirstName.ToLower(),
                LastName = model.LastName.ToLower(),
                MiddleName = model.MiddleName == null ? model.MiddleName : model.MiddleName.ToLower(),
                UserName = model.Email.ToLower(),
                Email = model.Email.ToLower(),
            };

            // creates a user inside our AspNetUsers table inside our database
            var result = await _userManager.CreateAsync(userToAdd, model.Password);

            if (!result.Succeeded) return BadRequest(result.Errors);
            await _userManager.AddToRoleAsync(userToAdd, SD.PlayerRole);

            //return Ok("Your account has been created, you can login");
            return Ok(new JsonResult(new { title = "Account Created", message = "Your account has been created, you can login" }));

            //try
            //{
            //    if (await SendConfirmEMailAsync(userToAdd))
            //    {
            //        return Ok(new JsonResult(new { title = "Account Created", message = "Your account has been created, please confrim your email address" }));
            //    }

            //    return BadRequest("Failed to send email. Please contact admin");
            //}
            //catch (Exception)
            //{
            //    return BadRequest("Failed to send email. Please contact admin");
            //}

        }

        #region Private Helper Methods

        private async Task<UserDto> CreateApplicationUserDto(User user)
        {
            //await SaveRefreshTokenAsync(user);
            return new UserDto
            {
                FirstName = user.FirstName,
                LastName = user.LastName,
                JWT = await _jwtService.CreateJWT(user),
            };
        }
        
        private async Task<bool> CheckEmailExistsAsync(string email)
        {
            return await _userManager.Users.AnyAsync(x => x.Email == email.ToLower());
        }
        /*
        private async Task<bool> SendConfirmEMailAsync(User user)
        {
            var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            token = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
            var url = $"{_config["JWT:ClientUrl"]}/{_config["Email:ConfirmEmailPath"]}?token={token}&email={user.Email}";

            var body = $"<p>Hello: {user.FirstName} {user.LastName}</p>" +
                "<p>Please confirm your email address by clicking on the following link.</p>" +
                $"<p><a href=\"{url}\">Click here</a></p>" +
                "<p>Thank you,</p>" +
                $"<br>{_config["Email:ApplicationName"]}";

            var emailSend = new EmailSendDto(user.Email, "Confirm your email", body);

            return await _emailService.SendEmailAsync(emailSend);
        }

        private async Task<bool> SendForgotUsernameOrPasswordEmail(User user)
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            token = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
            var url = $"{_config["JWT:ClientUrl"]}/{_config["Email:ResetPasswordPath"]}?token={token}&email={user.Email}";

            var body = $"<p>Hello: {user.FirstName} {user.LastName}</p>" +
               $"<p>Username: {user.UserName}.</p>" +
               "<p>In order to reset your password, please click on the following link.</p>" +
               $"<p><a href=\"{url}\">Click here</a></p>" +
               "<p>Thank you,</p>" +
               $"<br>{_config["Email:ApplicationName"]}";

            var emailSend = new EmailSendDto(user.Email, "Forgot username or password", body);

            return await _emailService.SendEmailAsync(emailSend);
        }

        private async Task<bool> FacebookValidatedAsync(string accessToken, string userId)
        {
            var facebookKeys = _config["Facebook:AppId"] + "|" + _config["Facebook:AppSecret"];
            var fbResult = await _facebookHttpClient.GetFromJsonAsync<FacebookResultDto>($"debug_token?input_token={accessToken}&access_token={facebookKeys}");

            if (fbResult == null || fbResult.Data.Is_Valid == false || !fbResult.Data.User_Id.Equals(userId))
            {
                return false;
            }

            return true;
        }

        private async Task<bool> GoogleValidatedAsync(string accessToken, string userId)
        {
            var payload = await GoogleJsonWebSignature.ValidateAsync(accessToken);

            if (!payload.Audience.Equals(_config["Google:ClientId"]))
            {
                return false;
            }

            if (!payload.Issuer.Equals("accounts.google.com") && !payload.Issuer.Equals("https://accounts.google.com"))
            {
                return false;
            }

            if (payload.ExpirationTimeSeconds == null)
            {
                return false;
            }

            DateTime now = DateTime.Now.ToUniversalTime();
            DateTime expiration = DateTimeOffset.FromUnixTimeSeconds((long)payload.ExpirationTimeSeconds).DateTime;
            if (now > expiration)
            {
                return false;
            }

            if (!payload.Subject.Equals(userId))
            {
                return false;
            }

            return true;
        }

        private async Task SaveRefreshTokenAsync(User user)
        {
            var refreshToken = _jwtService.CreateRefreshToken(user);

            var existingRefreshToken = await _context.RefreshTokens.SingleOrDefaultAsync(x => x.UserId == user.Id);
            if (existingRefreshToken != null)
            {
                existingRefreshToken.Token = refreshToken.Token;
                existingRefreshToken.DateCreatedUtc = refreshToken.DateCreatedUtc;
                existingRefreshToken.DateExpiresUtc = refreshToken.DateExpiresUtc;
            }
            else
            {
                user.RefreshTokens.Add(refreshToken);
            }

            await _context.SaveChangesAsync();

            var cookieOptions = new CookieOptions
            {
                Expires = refreshToken.DateExpiresUtc,
                IsEssential = true,
                HttpOnly = true,
            };

            Response.Cookies.Append("identityAppRefreshToken", refreshToken.Token, cookieOptions);
        }
        */

        private async Task<bool> IsValidRefreshTokenAsync(string userId, string token)
        {
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(token)) return false;

            var fetchedRefreshToken = await _context.RefreshTokens
                .FirstOrDefaultAsync(x => x.UserId == userId && x.Token == token);
            if (fetchedRefreshToken == null) return false;
            if (fetchedRefreshToken.IsExpired) return false;

            return true;
        }
        
        #endregion

    }
}
