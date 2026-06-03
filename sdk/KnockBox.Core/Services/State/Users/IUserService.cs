using KnockBox.Core.Primitives.Returns;

namespace KnockBox.Core.Services.State.Users
{
    public record class UserNameChangedArgs(string PreviousName, string NewName);

    /// <summary>
    /// A player's public identity: a <see cref="Name"/> chosen by the user and a
    /// stable <see cref="Id"/> assigned by the platform. Construction is
    /// restricted to the platform — consumers resolve the current circuit's user
    /// via <see cref="IUserService.CurrentUser"/>, and test code builds fixtures
    /// via <see cref="UserFactory"/>. This keeps name-trimming, event firing,
    /// and persistence in a single place (<see cref="IUserService"/>) rather
    /// than leaking across every call site that could mutate <see cref="Name"/>.
    /// </summary>
    public class User
    {
        internal User(string name, string id)
        {
            Name = name;
            Id = id;
        }

        /// <summary>
        /// The user's display name. Capped to 12 characters by
        /// <see cref="IUserService.SetCurrentUserName"/>. External code mutates
        /// this through that method; the setter itself is reserved for the
        /// platform (e.g. the name-disambiguation pass in
        /// <c>AbstractGameState.RegisterPlayer</c>).
        /// </summary>
        public string Name { get; internal set; }

        /// <summary>
        /// The unique id of the user. Immutable once the user is constructed.
        /// </summary>
        public string Id { get; }
    }

    /// <summary>
    /// Factory for building <see cref="User"/> instances outside the platform's
    /// normal lifecycle. Intended for plugin test code; production consumers
    /// should resolve <see cref="IUserService.CurrentUser"/>.
    /// </summary>
    public static class UserFactory
    {
        internal const int MaxNameLength = 12;

        /// <summary>
        /// Constructs a <see cref="User"/> with the supplied name and id,
        /// applying the same normalization as
        /// <see cref="IUserService.SetCurrentUserName"/>: trim leading/trailing
        /// whitespace and cap at 12 characters. Test fixtures should prefer
        /// this overload so their <see cref="User"/> instances match production
        /// shape; reach for <see cref="CreateUnchecked"/> only when the test
        /// explicitly needs an un-normalized value.
        /// </summary>
        public static User Create(string name, string id)
        {
            name = (name ?? string.Empty).Trim();
            if (name.Length > MaxNameLength) name = name[..MaxNameLength];
            return new User(name, id);
        }

        /// <summary>
        /// Constructs a <see cref="User"/> without trimming or capping the
        /// name. Intended for tests that intentionally exercise pre-normalization
        /// paths (e.g. verifying that upstream layers do reject a 13-character
        /// input). Production code should never call this.
        /// </summary>
        public static User CreateUnchecked(string name, string id) => new(name, id);
    }

    public interface IUserService
    {
        /// <summary>
        /// The user information for this circuit. Null if not initialized.
        /// </summary>
        User? CurrentUser { get; }

        /// <summary>
        /// Raised once when <see cref="InitializeCurrentUserAsync"/> completes and
        /// <see cref="CurrentUser"/> becomes non-null. Subscribers can call
        /// <c>StateHasChanged()</c> to refresh UI that depends on the current user.
        /// </summary>
        event Action? UserInitialized;

        /// <summary>
        /// Raised after <see cref="SetCurrentUserName"/> applies a name change to
        /// <see cref="CurrentUser"/>. Subscribers that throw are isolated — one
        /// bad handler does not short-circuit the invocation list.
        /// </summary>
        event Action<UserNameChangedArgs>? UserNameChanged;

        /// <summary>
        /// Initializes the current user. A <see cref="User"/> is always produced (a
        /// fallback identity is used if the session token can't be obtained); the
        /// returned result is a failure in that degraded case so callers may surface it.
        /// </summary>
        /// <param name="ct"></param>
        /// <returns>Success when a real session was established; failure on the degraded
        /// fallback path; cancellation is reported as a failure result.</returns>
        Task<Result> InitializeCurrentUserAsync(CancellationToken ct = default);

        /// <summary>
        /// Resets the user's identity, generating a new unique id.
        /// </summary>
        /// <param name="ct"></param>
        /// <returns>The result of re-initializing the current user.</returns>
        Task<Result> ResetIdentityAsync(CancellationToken ct = default);

        /// <summary>
        /// Updates <see cref="CurrentUser"/>'s name. Trims whitespace and caps at
        /// 12 characters. No-op when <see cref="CurrentUser"/> is null or the
        /// trimmed+capped value equals the current name. Persists the new name
        /// asynchronously and raises <see cref="UserNameChanged"/>.
        /// </summary>
        void SetCurrentUserName(string name);
    }
}
