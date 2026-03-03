namespace KnockBox.Extensions.Returns
{
    public readonly record struct ResultError
    {
        /// <summary>
        /// The error for UI display.
        /// </summary>
        public readonly string PublicMessage;

        /// <summary>
        /// The error for internal display.
        /// </summary>
        public readonly string InternalMessage;

        /// <summary>
        /// Creates an error sharing the public and internal message.
        /// </summary>
        /// <param name="error"></param>
        public ResultError(string error)
        {
            PublicMessage = error;
            InternalMessage = error;
        }

        /// <summary>
        /// Creates an error with different public and internal messages.
        /// </summary>
        /// <param name="publicMessage"></param>
        /// <param name="internalMessage"></param>
        public ResultError(string publicMessage, string internalMessage)
        {
            InternalMessage = internalMessage;
            PublicMessage = publicMessage;
        }
    }
}
