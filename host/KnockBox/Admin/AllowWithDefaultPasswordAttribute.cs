namespace KnockBox.Admin
{
    /// <summary>
    /// Marks an admin endpoint as reachable even while the bootstrap/default
    /// admin password is still active. <see cref="DefaultPasswordRedirectMiddleware"/>
    /// reads this attribute from the resolved endpoint's metadata and skips the
    /// forced /admin/changepassword redirect for any endpoint that carries it.
    /// Apply to Login, Logout, and ChangePassword — anywhere an operator needs
    /// to reach before they can rotate the default password.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    internal sealed class AllowWithDefaultPasswordAttribute : Attribute
    {
    }
}
