using System; // Keep for .NET 4.6
using System.Collections;

namespace BcToolsC
{
    /// <summary>
    /// Provides methods to throw an exception if an assertion is wrong.
    /// </summary>
    internal static class Assert
    {
        /// <summary>
        /// Throws ArgumentNullException if the object is null.
        /// </summary>
        /// <param name="obj">The instance to which the assertion applies.</param>
        /// <param name="paramName">Name of the parameter.</param>
        /// <exception cref="ArgumentNullException">Throws ArgumentNullException if <paramref name="obj"/> is null.</exception>
        public static void IsNotNull(object obj, string paramName)
        {
            if (obj == null)
                throw new ArgumentNullException(paramName);
        }

        /// <summary>
        /// Throws ArgumentException if the sequence is empty.
        /// </summary>
        /// <typeparam name="T">Type of the object.</typeparam>
        /// <param name="obj">The instance to which the assertion applies.</param>
        /// <param name="paramName">Name of the parameter.</param>
        /// <exception cref="ArgumentException">Throws ArgumentException if <paramref name="obj"/> is an empty sequence.</exception>
        public static void IsNotEmpty<T>(T obj, string paramName) where T : IEnumerable
        {
            if (!((IEnumerable)obj).GetEnumerator().MoveNext())
                throw new ArgumentException("Empty sequence", paramName);
        }
    }
}