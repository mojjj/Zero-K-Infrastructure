using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;

namespace ZkLobbyServer.Api
{
    /// <summary>
    /// The wire contract between the website and a lobby server in another process.
    ///
    /// **Why HTTP and JSON.** Both halves are .NET Framework 4.8 today, which rules out the
    /// obvious alternative: grpc-dotnet needs .NET Core, and Grpc.Core is unmaintained. HttpListener
    /// and HttpClient are in the BCL on 4.8 and unchanged on .NET 9, so the transport does not have
    /// to be rewritten by the port that is going on around it. Newtonsoft is already a dependency
    /// of both projects.
    ///
    /// **The interface is the allowlist.** A request names a member; the host looks it up on
    /// <see cref="ILobbyServerApi"/> and nowhere else, and invokes it through the interface
    /// reference. There is no path from a request to a method the interface does not declare -
    /// which matters, because this API can kick players, post to the moderator channel and redeem
    /// session tokens.
    ///
    /// **What makes JSON safe here** is the check that already runs on every pull request:
    /// `ZkData.Core -- seam` walks every member's parameter and return types transitively and
    /// refuses EF entities and DbContexts. A member that cannot be serialised cannot reach this
    /// file without failing that first.
    /// </summary>
    public static class LobbyApiProtocol
    {
        /// <summary>Requests are POSTed to this prefix plus the member name.</summary>
        public const string PathPrefix = "/lobbyapi/";

        public const string AuthorizationScheme = "Bearer";

        /// <summary>A request body larger than this is refused unread.</summary>
        public const int MaxRequestBytes = 256 * 1024;

        /// <summary>
        /// No type handling, deliberately: <c>TypeNameHandling</c> lets a payload name the type to
        /// construct, which is a remote code execution primitive. Every type on this interface is
        /// known from the member signature, so nothing needs to be told what it is.
        /// </summary>
        public static readonly JsonSerializerSettings Json = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.None,
            NullValueHandling = NullValueHandling.Include,
            DateTimeZoneHandling = DateTimeZoneHandling.Utc,
        };

        public static string Serialize(object value) => JsonConvert.SerializeObject(value, Json);

        public static T Deserialize<T>(string json) => JsonConvert.DeserializeObject<T>(json, Json);

        /// <summary>The members a request may name, by name. Built once, from the interface.</summary>
        public static readonly IReadOnlyDictionary<string, MethodInfo> Members = BuildMembers();

        private static Dictionary<string, MethodInfo> BuildMembers()
        {
            var members = new Dictionary<string, MethodInfo>(StringComparer.Ordinal);

            foreach (var property in typeof(ILobbyServerApi).GetProperties())
            {
                var getter = property.GetGetMethod();
                if (getter != null) members[property.Name] = getter;
            }

            foreach (var method in typeof(ILobbyServerApi).GetMethods().Where(m => !m.IsSpecialName))
            {
                // Overloads would make a name ambiguous on the wire. There are none today; this
                // makes adding one a startup failure rather than a coin toss at dispatch time.
                if (members.ContainsKey(method.Name))
                    throw new InvalidOperationException(
                        "ILobbyServerApi." + method.Name + " is overloaded, which the remote protocol "
                        + "addresses members by name cannot express. Give one of them a distinct name.");
                members[method.Name] = method;
            }

            return members;
        }

        /// <summary>
        /// Comparison whose duration does not depend on how much of the secret was right, so a
        /// caller cannot find it a byte at a time by timing the answer.
        ///
        /// Compares SHA-256 digests rather than the strings: they are always the same length, so
        /// the loop below is genuinely fixed-cost. Comparing the raw bytes needs care about
        /// differing lengths, and the version of this that tried was correct but not obviously so,
        /// which is not a property worth having in an authentication check.
        /// </summary>
        public static bool SecretEquals(string a, string b)
        {
            if (a == null || b == null) return false;
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var x = sha.ComputeHash(Encoding.UTF8.GetBytes(a));
                var y = sha.ComputeHash(Encoding.UTF8.GetBytes(b));
                var difference = 0;
                for (var i = 0; i < x.Length; i++) difference |= x[i] ^ y[i];
                return difference == 0;
            }
        }
    }

    /// <summary>What comes back. One of the two fields is set.</summary>
    public class LobbyApiResponse
    {
        public object result;
        public string error;
    }
}
