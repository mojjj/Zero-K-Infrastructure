using System;
using System.Threading;
using System.Threading.Tasks;
using LobbyClient;

namespace LobbyClientProbe
{
    /// <summary>
    ///     Registers an account and logs in, against a lobby server that is already running.
    ///
    ///     usage: LobbyClientProbe &lt;host&gt; &lt;port&gt; &lt;name&gt; &lt;password&gt;
    ///
    ///     The point is that opening a port is not the same as serving anybody. This completes the
    ///     handshake: the TCP connect, the line-framed JSON both ways, a Register that writes an
    ///     Account through EF Core, and a Login the server accepts. Exits 0 only if the server said
    ///     Ok to both.
    /// </summary>
    public static class Program
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

        public static async Task<int> Main(string[] args)
        {
            if (args.Length < 4)
            {
                Console.Error.WriteLine("usage: LobbyClientProbe <host> <port> <name> <password>");
                return 2;
            }

            var host = args[0];
            var port = int.Parse(args[1]);
            var name = args[2];
            var password = args[3];

            var client = new TasClient("LobbyClientProbe 1.0");

            var connected = new TaskCompletionSource<bool>();
            var registered = new TaskCompletionSource<string>();
            var loggedIn = new TaskCompletionSource<string>();

            client.Connected += (s, e) => connected.TrySetResult(true);
            client.ConnectionLost += (s, e) =>
            {
                connected.TrySetException(new Exception("connection lost: " + e.ServerParams?[0]));
                registered.TrySetException(new Exception("connection lost before the server answered Register"));
                loggedIn.TrySetException(new Exception("connection lost before the server answered Login"));
            };
            client.RegistrationAccepted += (s, e) => registered.TrySetResult(null);
            client.RegistrationDenied += (s, e) => registered.TrySetResult(e.ResultCode.ToString());
            client.LoginAccepted += (s, e) => loggedIn.TrySetResult(null);
            client.LoginDenied += (s, e) => loggedIn.TrySetResult(e.ResultCode.ToString());

            Console.WriteLine($"connecting to {host}:{port}");
            client.Connect(host, port);
            if (!await Within(connected.Task, "connect")) return 1;
            Console.WriteLine("   ok    connected, and the server greeted us");

            await client.Register(name, password);
            if (!await Within(registered.Task, "register")) return 1;
            // "It already exists" is a pass: the account is there, which is all login needs. Both
            // spellings happen when this runs twice against a database that was not reloaded in
            // between - the server says AlreadyRegisteredWithThisPassword when the password also
            // matches, and NameAlreadyTaken when it does not.
            var reg = registered.Task.Result;
            if (reg != null && reg != "NameAlreadyTaken" && reg != "AlreadyRegisteredWithThisPassword")
            {
                Console.WriteLine($"   FAIL  the server refused to register '{name}': {reg}");
                return 1;
            }
            Console.WriteLine(reg == null
                ? $"   ok    registered '{name}' - an Account was written through EF Core"
                : $"   ok    '{name}' was already registered ({reg}), which login is happy with");

            await client.Login(name, password);
            if (!await Within(loggedIn.Task, "login")) return 1;
            var login = loggedIn.Task.Result;
            if (login != null)
            {
                Console.WriteLine($"   FAIL  the server refused the login: {login}");
                return 1;
            }
            Console.WriteLine("   ok    logged in - the server accepted a real client");

            return 0;
        }

        /// <summary>
        ///     Waits, and turns "it never answered" into a message that says which step hung rather
        ///     than a stack trace or a silent hang in CI.
        /// </summary>
        private static async Task<bool> Within(Task task, string step)
        {
            var finished = await Task.WhenAny(task, Task.Delay(Timeout));
            if (finished != task)
            {
                Console.WriteLine($"   FAIL  the server never answered {step} within {Timeout.TotalSeconds:0}s");
                return false;
            }

            try
            {
                await task;
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   FAIL  {step}: {ex.Message}");
                return false;
            }
        }
    }
}
