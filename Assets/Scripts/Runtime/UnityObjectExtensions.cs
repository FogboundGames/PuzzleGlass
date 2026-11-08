namespace PuzzleGlass
{
    public static class UnityObjectExtensions
    {
        /// <summary>
        /// Returns a real null for destroyed or missing objects so ?? and ?. work as expected.
        /// In the Editor a failed GetComponent returns a placeholder that those operators treat as non-null.
        /// </summary>
        public static T OrNull<T>(this T obj) where T : UnityEngine.Object
        {
            return obj != null ? obj : null;
        }
    }
}
