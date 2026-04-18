using Microsoft.AspNetCore.Authentication;
using KnockBox.Services.Logic.Admin;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace KnockBox.Admin.Pages
{
    public sealed class LoginModel : PageModel
    {
        private readonly AdminOptions _options;
        private readonly IAdminSettingsService _settingsService;
        private readonly ILogger<LoginModel> _logger;

        public LoginModel(
            IOptions<AdminOptions> options,
            IAdminSettingsService settingsService,
            ILogger<LoginModel> logger)
        {
            _options = options.Value;
            _settingsService = settingsService;
            _logger = logger;
        }

        [BindProperty]
        public string Username { get; set; } = string.Empty;

        [BindProperty]
        public string Password { get; set; } = string.Empty;

        [BindProperty(SupportsGet = true)]
        public string? ReturnUrl { get; set; }

        public string? Error { get; private set; }

        public void OnGet()
        {
            // If already signed in, bounce straight to the dashboard so the
            // login page isn't a dead-end once authenticated.
            if (User.Identity?.IsAuthenticated == true)
            {
                Response.Redirect(ResolveReturnUrl());
            }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            // Constant-time compare so failing creds don't leak length/prefix
            // via response timing. Trim username only (preserves the exact
            // password bytes configured).
            var usernameMatches = FixedTimeEquals(
                (Username ?? string.Empty).Trim(),
                _options.Username);
            var passwordMatches = _settingsService.VerifyPassword(Password ?? string.Empty);

            if (!usernameMatches || !passwordMatches)
            {
                _logger.LogWarning(
                    "Admin login failed for an account attempt from {RemoteIp}.",
                    HttpContext.Connection.RemoteIpAddress);
                Error = "Invalid username or password.";
                Password = string.Empty;
                return Page();
            }

            var claims = new[]
            {
                new Claim(ClaimTypes.Name, _options.Username),
                new Claim(ClaimTypes.Role, "Admin"),
            };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                new AuthenticationProperties
                {
                    IsPersistent = false,
                    AllowRefresh = true,
                });

            _logger.LogInformation(
                "Admin login succeeded for user [{Username}] from {RemoteIp}.",
                _options.Username,
                HttpContext.Connection.RemoteIpAddress);

            if (_settingsService.IsPasswordDefault())
            {
                return Redirect("/admin/changepassword");
            }

            return Redirect(ResolveReturnUrl());
        }

        private string ResolveReturnUrl()
        {
            // Only honour local return URLs so the login page can't be
            // weaponised as an open redirect.
            if (!string.IsNullOrWhiteSpace(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
                return ReturnUrl;
            return "/admin";
        }

        private static bool FixedTimeEquals(string left, string right)
        {
            var l = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(left));
            var r = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(right));
            return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(l, r);
        }
    }
}
