namespace KnockBox.Services.Logic.Admin
{
    /// <summary>
    /// Pure validation rules for the admin password-change form. Extracted so the
    /// ordered checks (non-empty → not-current → matches confirm → min length)
    /// can be unit-tested without spinning up a Blazor renderer.
    /// </summary>
    internal static class AdminPasswordValidator
    {
        public const int MinPasswordLength = 8;

        public static string? Validate(
            string? newPassword,
            string? confirmPassword,
            Func<string, bool> isCurrentPassword)
        {
            if (string.IsNullOrWhiteSpace(newPassword))
                return "Password cannot be empty.";

            if (isCurrentPassword(newPassword))
                return "You cannot reuse your current password.";

            if (newPassword != confirmPassword)
                return "Passwords do not match.";

            if (newPassword.Length < MinPasswordLength)
                return $"Password must be at least {MinPasswordLength} characters long.";

            return null;
        }
    }
}
