using System;
using System.Threading;
using System.Threading.Tasks;
using LobbyClient;
using PlasmaShared;

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

        // Deliberately NOT one of the channels ChannelManager hands out by default ("zk", zkadmin,
        // top20, core). Those are joined by the server at login, so asking for one of them proves
        // nothing about the explicit JoinChannel below - the response would already have arrived.
        // An unknown name is allowed by CanJoin, which still has to read the account to decide.
        private const string Channel = "probechannel";

        public static async Task<int> Main(string[] args)
        {
            if (args.Length < 4)
            {
                Console.Error.WriteLine(
                    "usage: LobbyClientProbe <host> <port> <name> <password> [map expected-text]");
                return 2;
            }

            var host = args[0];
            var port = int.Parse(args[1]);
            var name = args[2];
            var password = args[3];

            var client = new TasClient("LobbyClientProbe 1.0");

            var connected = new TaskCompletionSource<bool>();
            var joinedChannel = new TaskCompletionSource<string>();
            var heard = new TaskCompletionSource<bool>();
            var battleOpened = new TaskCompletionSource<bool>();
            var registered = new TaskCompletionSource<string>();
            var loggedIn = new TaskCompletionSource<string>();

            client.Connected += (s, e) => connected.TrySetResult(true);
            client.ConnectionLost += (s, e) =>
            {
                connected.TrySetException(new Exception("connection lost: " + e.ServerParams?[0]));
                registered.TrySetException(new Exception("connection lost before the server answered Register"));
                loggedIn.TrySetException(new Exception("connection lost before the server answered Login"));
                joinedChannel.TrySetException(new Exception("connection lost before the server answered JoinChannel"));
                heard.TrySetException(new Exception("connection lost before the server relayed the message"));
                battleOpened.TrySetException(new Exception("connection lost before the server opened the battle"));
            };
            client.RegistrationAccepted += (s, e) => registered.TrySetResult(null);
            client.RegistrationDenied += (s, e) => registered.TrySetResult(e.ResultCode.ToString());
            client.LoginAccepted += (s, e) => loggedIn.TrySetResult(null);
            client.LoginDenied += (s, e) => loggedIn.TrySetResult(e.ResultCode.ToString());
            // Both handlers check WHICH channel. The server auto-joins an account's default
            // channels at login, so an unfiltered handler is satisfied by one of those before the
            // explicit JoinChannel below is even sent - which made a deliberately refused join to
            // the moderator channel report success.
            client.BattleOpened += (s, e) => { if (e.FounderName == name) battleOpened.TrySetResult(true); };
            client.ChannelJoined += (s, e) => { if (e.Name == Channel) joinedChannel.TrySetResult(null); };
            client.ChannelJoinFailed += (s, e) => { if (e.ChannelName == Channel) joinedChannel.TrySetResult(e.Reason ?? "refused without a reason"); };

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

            // Past login. ChannelManager.CanJoin does db.Accounts.FindAsync, so joining is another
            // EF Core round trip on a path a build cannot reach; saying something comes back
            // through the server, which is the first thing here that proves two-way traffic rather
            // than request and reply.
            await client.JoinChannel(Channel);
            if (!await Within(joinedChannel.Task, "join channel")) return 1;
            var join = joinedChannel.Task.Result;
            if (join != null)
            {
                Console.WriteLine($"   FAIL  the server refused to join #{Channel}: {join}");
                return 1;
            }
            Console.WriteLine($"   ok    joined #{Channel} - not a default channel, so this is the explicit join, and CanJoin read the account through EF Core");

            var message = "probe " + Guid.NewGuid().ToString("N").Substring(0, 8);
            client.Said += (s, e) => { if (e.Text == message) heard.TrySetResult(true); };
            await client.Say(SayPlace.Channel, Channel, message, false);
            if (!await Within(heard.Task, "say")) return 1;
            Console.WriteLine($"   ok    the server relayed what the client said back to it");

            // The last protocol step before a game: the server constructs a ServerBattle and
            // announces it to everyone, then joins us to it. Nothing starts Spring here.
            await client.OpenBattle(new BattleHeader
            {
                Title = "probe battle",
                Map = args.Length >= 6 ? args[4] : "test_map_1",
                Game = "test_mod_1",
                Engine = "105.1.1-2511-g2c4d0a1",
                MaxPlayers = 2,
                Mode = AutohostMode.None,
                Password = null,
            });
            if (!await Within(battleOpened.Task, "open battle")) return 1;
            Console.WriteLine($"   ok    the server opened a battle and announced it");

            // Opening the battle is what makes the server look up the map's metadata, and
            // !listmapoptions is the only place that lookup is visible from outside: it answers
            // "this map has no map options" when HostedMapInfo is null, which is what a player
            // sees when the server cannot reach the metadata at all.
            //
            // Only when asked for. Without the argument this probe behaves exactly as before, so
            // tools/lobby-core-start.sh - which runs against a server that does not even compile
            // the battle commands - is untouched.
            if (args.Length >= 6)
            {
                var expected = args[5];
                var answered = new TaskCompletionSource<string>();
                // BattlePrivate, not Battle. ServerBattle.Respond passes the asking user to
                // SayBattle, and that addresses the reply to them - a battle command answers
                // the person who ran it, not the room. Filtering on Battle alone waits forever.
                client.Said += (s, e) =>
                {
                    if ((e.Place == SayPlace.Battle || e.Place == SayPlace.BattlePrivate)
                        && e.Text != null && e.Text.Contains(expected))
                        answered.TrySetResult(e.Text);
                };

                // Asked repeatedly, because a battle say is dropped outright unless the sender is
                // already in the battle - and the founder is joined by the server AFTER it has
                // broadcast BattleAdded, which is the event this probe waited on. One ask races
                // that join and loses silently; the command is a read, so asking again is free.
                string reply = null;
                for (var attempt = 0; attempt < 15 && reply == null; attempt++)
                {
                    await client.Say(SayPlace.Battle, "", "!listmapoptions", false);
                    if (await Task.WhenAny(answered.Task, Task.Delay(TimeSpan.FromSeconds(2))) == answered.Task)
                        reply = answered.Task.Result;
                }

                if (reply == null)
                {
                    Console.WriteLine($"   FAIL  !listmapoptions for {args[4]} never mentioned '{expected}'");
                    return 1;
                }

                Console.WriteLine($"   ok    !listmapoptions for {args[4]} answered '{reply.Trim()}'");
            }

            // A login that must FAIL, and fail cleanly - on its own connection, because a refused
            // login leaves the server-side user unauthenticated and everything after it on that
            // connection then silently does nothing. Found by doing it inline first: the battle
            // stopped opening.
            //
            // LoginChecker looks the name up with an exact-match query and a case-insensitive
            // fallback. The fallback used string.Equals with a StringComparison, which EF Core
            // cannot translate, so on .NET 9 it threw for every name that does not exist - the
            // ordinary "wrong username" case - and the server answered nothing at all.
            return await AnUnknownNameIsRefused(host, port);
        }

        private static async Task<int> AnUnknownNameIsRefused(string host, int port)
        {
            var client = new TasClient("LobbyClientProbe 1.0");
            var connected = new TaskCompletionSource<bool>();
            var answered = new TaskCompletionSource<string>();

            client.Connected += (s, e) => connected.TrySetResult(true);
            client.ConnectionLost += (s, e) =>
            {
                connected.TrySetException(new Exception("connection lost"));
                answered.TrySetException(new Exception("the server dropped the connection instead of refusing the login"));
            };
            client.LoginAccepted += (s, e) => answered.TrySetResult(null);
            client.LoginDenied += (s, e) => answered.TrySetResult(e.ResultCode.ToString());

            client.Connect(host, port);
            if (!await Within(connected.Task, "connect for the unknown-name login")) return 1;

            await client.Login("nosuchaccount" + Guid.NewGuid().ToString("N").Substring(0, 8), "whatever");
            if (!await Within(answered.Task, "login with an unknown name")) return 1;

            var denial = answered.Task.Result;
            if (denial == null)
            {
                Console.WriteLine("   FAIL  the server ACCEPTED a login for an account that does not exist");
                return 1;
            }
            Console.WriteLine($"   ok    an unknown name is refused, not crashed on ({denial})");
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
