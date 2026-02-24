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
        /// Notifies all subscribers of the group with the provided args. Waits for all subscribers to recieve the message.
        /// </summary>
        /// <typeparam name="TType"></typeparam>
        /// <param name="group"></param>
        /// <param name="args"></param>
        Task NotifyAsync<TType>(string group, TType args);

        /// <summary>
        /// Notifies all subscribers of the group with the provided args. Doesn't wait for all subscribers to recieve the message.
        /// </summary>
        /// <typeparam name="TType"></typeparam>
        /// <param name="group"></param>
        /// <param name="args"></param>
        void Notify<TType>(string group, TType args);
    }
}
