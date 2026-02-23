namespace KnockBox.Extensions.Events
{
    public interface ITypedThreadSafeEventManager
    {
        /// <summary>
        /// Subscribes to events of the specified group.
        /// </summary>
        /// <typeparam name="TType"></typeparam>
        /// <param name="group"></param>
        /// <param name="callback"></param>
        void Subscribe<TType>(string group, Func<TType, ValueTask> callback);


        /// <summary>
        /// Unsubscribes from events of the specified group.
        /// </summary>
        /// <typeparam name="TType"></typeparam>
        /// <param name="group"></param>
        /// <param name="callback"></param>
        void Unsubscribe<TType>(string group, Func<TType, ValueTask> callback);

        /// <summary>
        /// Notifies all subscribers of the group with the provided args.
        /// </summary>
        /// <typeparam name="TType"></typeparam>
        /// <param name="group"></param>
        /// <param name="args"></param>
        Task NotifyAsync<TType>(string group, TType args);
    }
}
