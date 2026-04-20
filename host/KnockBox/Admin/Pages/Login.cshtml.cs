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
        private const int MinimumPasswordLength = 8;

        private readonly AdminOptions _options;
        private readonly IAdminSettingsService _settings;
        private readonly ILogger<LoginModel> _logger;

        public LoginModel(
            IOptions<AdminOptions> options,
            IAdminSettingsService settings,
            ILogger<LoginModel> logger)
        {
            _options = options.Value;
            _settings = settings;
            _logger = logger;
        }

        [BindProperty]
        public string Username { get; set; } = string.Empty;

        [BindProperty]
        public string Password { get; set; } = string.Empty;

        [BindProperty]
        public string ConfirmPassword { get; set; } = string.Empty;

        [BindProperty(SupportsGet = true)]
        public string? ReturnUrl { get; set; }

        public string? Error { get; private set; }

        /// <summary>
        /// True when the page is rendering the first-time initialization form
        /// (no active admin password yet). Computed per request so a second
        /// operator who hits the page after the first has already initialized
        /// sees the regular login form.
        /// </summary>
        public bool IsInitMode => !_settings.IsAdminPasswordSet();

        /// <summary>Display-only username shown on the init form.</summary>
        public string ConfiguredUsername => _options.Username;

        /// <summary>Minimum password length surfaced to the init-mode view.</summary>
        public int MinPasswordLength => MinimumPasswordLength;

        public void OnGet()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                Response.Redirect(ResolveReturnUrl());
            }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            // Re-check at submit time so a race between two operators (both
            // seeing the init form, both submitting) collapses deterministically:
            // whoever persists first wins; the other falls through to login.
            if (!_settings.IsAdminPasswordSet())
                return await HandleInitAsync();

            return await HandleLoginAsync();
        }

        private async Task<IActionResult> HandleInitAsync()
        {
            var password = Password ?? string.Empty;
            var confirm = ConfirmPassword ?? string.Empty;

            if (password.Length < MinimumPasswordLength)
            {
                Error = $"Password must be at least {MinimumPasswordLength} characters.";
                Password = string.Empty;
                ConfirmPassword = string.Empty;
                return Page();
            }

            if (!string.Equals(password, confirm, StringComparison.Ordinal))
            {
                Error = "Passwords do not match.";
                Password = string.Empty;
                ConfirmPassword = string.Empty;
                return Page();
            }

            await _settings.SetAdminPasswordAsync(password);

            _logger.LogInformation(
                "Admin account initialized from {RemoteIp}.",
                HttpContext.Connection.RemoteIpAddress);

            await SignInAsync();
            return Redirect(ResolveReturnUrl());
        }

        private async Task<IActionResult> HandleLoginAsync()
        {
            var usernameMatches = PasswordHash.FixedTimeEquals(
                (Username ?? string.Empty).Trim(),
                _options.Username);
            var passwordMatches = _settings.VerifyAdminPassword(Password ?? string.Empty);

            if (!usernameMatches || !passwordMatches)
            {
                _logger.LogWarning(
                    "Admin login failed for an account attempt from {RemoteIp}.",
                    HttpContext.Connection.RemoteIpAddress);
                Error = "Invalid username or password.";
                Password = string.Empty;
                return Page();
            }

            await SignInAsync();

            _logger.LogInformation(
                "Admin login succeeded for user [{Username}] from {RemoteIp}.",
                _options.Username,
                HttpContext.Connection.RemoteIpAddress);

            return Redirect(ResolveReturnUrl());
        }

        private async Task SignInAsync()
        {
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
        }

        private string ResolveReturnUrl()
        {
            if (!string.IsNullOrWhiteSpace(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
                return ReturnUrl;
            return "/admin";
        }
    }
}
