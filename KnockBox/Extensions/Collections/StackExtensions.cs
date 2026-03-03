namespace KnockBox.Extensions.Collections
{
    public static class StackExtensions
    {
        public static void PushRange<TElement>(this Stack<TElement> stack, IEnumerable<TElement> range)
        {
            foreach (var element in range)
            {
                stack.Push(element);
            }
        }
    }
}
