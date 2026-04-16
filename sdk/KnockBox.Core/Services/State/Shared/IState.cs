using KnockBox.Core.Extensions.Exceptions;

namespace KnockBox.Core.Services.State.Shared
{
    #region Enums

    public enum PropertyState
    {
        Uninitialized,
        Errored,
        Canceled,
        Updating,
        Ready
    }

    public enum PropertyUpdateResult
    {
        Errored,
        Canceled,
        Succeeded,
    }

    #endregion

    #region Dtos

    /// <summary>
    /// An exception that indicates there is no exception.
    /// </summary>
    public class NothingException : Exception
    {
        public static readonly NothingException SharedInstance = new();
    }

    /// <summary>
    /// The results of updating the property.
    /// </summary>
    public readonly record struct UpdateResult
    {
        public readonly string PropertyName;
        public readonly PropertyUpdateResult Status;
        public readonly Exception Exception;

        /// <summary>
        /// Creates an update result with a success status.
        /// </summary>
        /// <param name="propertyName"></param>
        public UpdateResult(string propertyName)
        {
            PropertyName = propertyName;
            Status = PropertyUpdateResult.Succeeded;
            Exception = NothingException.SharedInstance;
        }

        /// <summary>
        /// Creates an update with an operation canceled or errored status. Priority goes to cancel exceptions.
        /// </summary>
        /// <param name="propertyName"></param>
        /// <param name="exception"></param>
        public UpdateResult(string propertyName, Exception exception)
        {
            PropertyName = propertyName;
            Exception = exception.GetCancellationException() ?? exception; // Ensures aggregate exceptions with cancellation nested are treated as cancellation exceptions.
            Status = Exception is OperationCanceledException ? PropertyUpdateResult.Canceled : PropertyUpdateResult.Errored;
        }

        public UpdateResult(string propertyName, Exception exception, PropertyUpdateResult status)
        {
            PropertyName = propertyName;
            Exception = exception;
            Status = status;
        }
    }

    /// <summary>
    /// Provides data for an event that is raised when the state of a property changes.
    /// </summary>
    /// <param name="PropertyName">The name of the property whose state has changed.</param>
    /// <param name="State">The new state of the property after the change.</param>
    public record class PropertyStateChangedArgs(string PropertyName, PropertyState State);

    #endregion

    public interface IState<TSelf> : IDisposable
        where TSelf : class
    {
        public delegate void PropertyStateChangedDelegate(IState<TSelf> source, PropertyStateChangedArgs args);

        /// <summary>
        /// Invoked when the state of a property has changed.
        /// </summary>
        event PropertyStateChangedDelegate? PropertyStateChanged;

        /// <summary>
        /// Gets the status of the provided property.
        /// </summary>
        /// <param name="propertyName"></param>
        /// <returns></returns>
        PropertyState GetPropertyState(string propertyName);

        /// <summary>
        /// Updates the provided properties.
        /// </summary>
        /// <param name="ct"></param>
        /// <param name="propertiesToUpdate"></param>
        /// <returns></returns>
        Task<List<UpdateResult>> UpdatePropertiesAsync(CancellationToken ct = default, params string[] propertiesToUpdate);

        /// <summary>
        /// Updates the provided properties.
        /// </summary>
        /// <param name="maxParallelUpdates"></param>
        /// <param name="ct"></param>
        /// <param name="propertiesToUpdate"></param>
        /// <returns></returns>
        Task<List<UpdateResult>> UpdatePropertiesAsync(int maxParallelUpdates = 8, CancellationToken ct = default, params string[] propertiesToUpdate);
    }
}
