using System;
using System.Collections.Generic;

namespace PlasmaShared
{
    /// <summary>
    /// The part of <see cref="Utils"/> that has no framework dependency. Utils.cs itself
    /// pulls in System.Drawing, which on .NET 9 lives in System.Drawing.Common and is
    /// Windows-only - so it cannot be linked into the .NET 9 test project, and these
    /// helpers can. See Tests.Portable/Tests.Portable.csproj.
    /// </summary>
    public static partial class Utils
    {
        public static void ForEach<T>(this IEnumerable<T> enumeration, Action<T> action)
        {
            foreach (T item in enumeration)
            {
                action(item);
            }
        }
    }
}
