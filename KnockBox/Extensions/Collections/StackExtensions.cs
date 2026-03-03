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

        public static TElement[] PopRange<TElement>(this Stack<TElement> stack, int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            if (count > stack.Count) 
                throw new ArgumentOutOfRangeException(
                    $"{nameof(count)} [{count}] cannot be greater than the count of {nameof(stack)} [{stack.Count}].");

            TElement[] elements = new TElement[count];
            for (int i = 0; i < count; i++)
            {
                elements[i] = stack.Pop();
            }
            return elements;
        }

        /// <summary>
        /// Pops the elements in the stack onto the output list.
        /// </summary>
        /// <typeparam name="TElement"></typeparam>
        /// <param name="stack"></param>
        /// <param name="count"></param>
        /// <param name="outputList"></param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public static void PopRange<TElement>(this Stack<TElement> stack, int count, ref List<TElement> outputList)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            if (count > stack.Count)
                throw new ArgumentOutOfRangeException(
                    $"{nameof(count)} [{count}] cannot be greater than the count of {nameof(stack)} [{stack.Count}].");

            outputList.EnsureCapacity(outputList.Count + count);
            while (count-- > 0)
            {
                outputList.Add(stack.Pop());
            }
        }
    }
}
