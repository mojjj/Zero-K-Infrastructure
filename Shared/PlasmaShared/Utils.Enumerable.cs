using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

using System.ComponentModel;

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
        public static IEnumerable<Type> GetAllTypesWithAttribute<T>()

        {

            var allowedAssemblies = new string[]

            {

                typeof(T).Assembly.GetName().Name,

                Assembly.GetEntryAssembly()?.GetName().Name, Assembly.GetExecutingAssembly().GetName().Name,

                Assembly.GetCallingAssembly().GetName().Name

            };

            

            return from a in AppDomain.CurrentDomain.GetAssemblies().Where(x=> allowedAssemblies.Contains(x.GetName().Name)).ToList().AsParallel()

                   from t in a.GetLoadableTypes()

                   let attributes = t.GetCustomAttributes(typeof(T), true)

                   where attributes != null && attributes.Length > 0

                   select t;

        }
        public static IEnumerable<Type> GetLoadableTypes(this Assembly assembly)
        {
            if (assembly == null) throw new ArgumentNullException(nameof(assembly));
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                return e.Types.Where(t => t != null);
            }
        }
        public static string EscapePath(this string path)
        {
            if (String.IsNullOrEmpty(path)) return path;
            var escaped = new StringBuilder();
            foreach (var c in path)
            {
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '(' || c == ')' || c == '.') escaped.Append(c);
                else escaped.Append('_');
            }
            return escaped.ToString();
        }
        public static string StringJoin(this IEnumerable<string> enumeration)
        {
            return string.Join(", ", enumeration);
        }
        public static string HashLobbyPassword(string pass)
        {
            var md5 = (MD5)HashAlgorithm.Create("MD5");
            md5.Initialize();
            var hashed = md5.ComputeHash(Encoding.ASCII.GetBytes(pass ?? ""));
            return Convert.ToBase64String(hashed);
        }
        public static bool ValidLobbyNameCharacter(char c)
        {
            if (c >= 'a' && c <= 'z') return true;
            if (c >= 'A' && c <= 'Z') return true;
            if (c >= '0' && c <= '9') return true;
            if (c == '_') return true;
            if (c == '[' || c == ']') return true;
            return false;
        }

        /// <summary>
        /// The [Description] of an enum value. Moved here from Utils.cs, which cannot
        /// compile outside .NET Framework; this is pure reflection and several views use it.
        /// </summary>
        public static string Description(this Enum e)
        {
            var da = (DescriptionAttribute[])(e.GetType().GetField(e.ToString()).GetCustomAttributes(typeof(DescriptionAttribute), false));
            return da.Length > 0 ? da[0].Description : e.ToString();
        }

        // Wanted by ForumParser/Tags/HeaderTag.cs, which the .NET 9 projects link.
        public static string StripInvalidLobbyNameChars(string name)
        {
            if (String.IsNullOrEmpty(name)) return name;
            var sb = new StringBuilder();
            foreach (var c in name.Where(Utils.ValidLobbyNameCharacter)) sb.Append(c);
            return sb.ToString();
        }

        // Wanted by PlanetwarsAdminController, which the .NET 9 projects will link.
        public static List<T> Shuffle<T>(this IEnumerable<T> source)
        {
            var list = source.ToList();
            ShuffleInPlace(list);
            return list;
        }

        public static void ShuffleInPlace<T>(IList<T> array)
        {
            var rng = new Random();
            var n = array.Count;
            while (n > 1)
            {
                var k = rng.Next(n);
                n--;
                var temp = array[n];
                array[n] = array[k];
                array[k] = temp;
            }
        }
    
        /// <summary>
        /// Pure string formatting, moved here from Utils.cs so the port can link it.
        /// Read by Planetwars/PwMatchMaker.cshtml and Missions/Detail.cshtml, and by the game
        /// client, which is why it stays in PlasmaShared rather than moving to the website.
        /// </summary>
        public static string PrintTimeRemaining(long secs)
        {
            if (secs <= 0) return "";
            if (secs < 60) return String.Format("{0}s", secs);
            if (secs < 3600) return String.Format("{0}m {1}s", secs / 60, secs % 60);
            return String.Format("{0}h {1}m {2}s", secs / 3600, secs / 60 % 60, secs % 60);
        }

        public static string PrintTimeRemaining(this TimeSpan timeSpan)
        {
            return PrintTimeRemaining((int)timeSpan.TotalSeconds);
        }
}
}
