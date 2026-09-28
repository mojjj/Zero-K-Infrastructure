using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using LobbyClient;
using PlasmaShared;
using ZkData;

namespace DedicatedProbe
{
    /// <summary>
    ///     Hosts a Spring game through <see cref="DedicatedServer" /> - the same method
    ///     ZkLobbyServer calls when a battle starts - and checks the engine actually comes up.
    ///
    ///     usage: DedicatedProbe &lt;writable-dir&gt; &lt;engine-version&gt; &lt;map&gt; &lt;game&gt; &lt;port&gt;
    ///
    ///     The point is that this is the production path: <c>ScriptGenerator.GenerateHostScript</c>
    ///     writes the script, <c>SpringPaths</c> resolves the binary and sets SPRING_DATADIR and
    ///     friends, and <c>Process.Start</c> launches it. A hand-written script proves the engine
    ///     runs; this proves the port can make it run.
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            if (args.Length < 5)
            {
                Console.Error.WriteLine("usage: DedicatedProbe <writable-dir> <engine-version> <map> <game> <port>");
                return 2;
            }

            var writable = args[0];
            var engine = args[1];
            var map = args[2];
            var game = args[3];
            var port = int.Parse(args[4]);

            Trace.Listeners.Add(new ConsoleTraceListener());

            var paths = new SpringPaths(writable, false, true);

            var binary = paths.GetDedicatedServerPath(engine);
            if (binary == null)
            {
                Console.WriteLine("   FAIL  SpringPaths does not believe engine " + engine + " is installed under " + writable);
                Console.WriteLine("         it wants <writable>/engine/" + paths.Platform + "/<version>/ with spring and done.txt");
                return 1;
            }
            Console.WriteLine("   ok    the port resolved the dedicated server: " + binary);

            var context = new LobbyHostingContext
            {
                FounderName = "probe",
                Map = map,
                Mod = game,
                EngineVersion = engine,
                Title = "dedicated probe",
                Mode = AutohostMode.None,
                BattleID = 1,
                Players = new List<PlayerTeam>
                {
                    new PlayerTeam { Name = "probe", AllyID = 0, LobbyID = 1, IsSpectator = false, ScriptPassword = "x" },
                },
            };

            var server = new DedicatedServer(paths);
            var started = new ManualResetEventSlim(false);
            server.DedicatedServerStarted += (s, e) => started.Set();

            string script;
            try
            {
                script = server.HostGame(context, "127.0.0.1", port);
            }
            catch (Exception ex)
            {
                Console.WriteLine("   FAIL  HostGame threw: " + ex.GetType().Name + ": " + ex.Message.Split('\n')[0]);
                return 1;
            }

            if (string.IsNullOrEmpty(script))
            {
                Console.WriteLine("   FAIL  HostGame produced no script");
                return 1;
            }
            Console.WriteLine("   ok    ScriptGenerator produced a host script (" + script.Length + " chars)");

            try
            {
                // The engine binds UDP, so "can something connect" is not the question a TCP probe
                // would answer. Connecting a UDP socket only fixes the peer, so this waits for the
                // server to say it is up instead - and falls back to asking the OS.
                if (!started.Wait(TimeSpan.FromSeconds(45)))
                {
                    Console.WriteLine("   FAIL  DedicatedServer never raised DedicatedServerStarted within 45s");
                    return 1;
                }
                Console.WriteLine("   ok    DedicatedServer launched the process (this is the event firing, not the game being up)");

                // Polled, not sampled once: DedicatedServerStarted fires when the PROCESS starts,
                // and the engine binds its game port a few tens of milliseconds later. Checking
                // immediately reported "nothing is listening" while the log said
                // "[GameServer] Server started on port 8452".
                if (!WaitForPort(port, TimeSpan.FromSeconds(30)))
                {
                    Console.WriteLine("   FAIL  nothing is listening on UDP " + port + ", so no client could reach the game");
                    return 1;
                }
                Console.WriteLine("   ok    UDP " + port + " is held - a player could connect to this game");

                if (!server.IsRunning)
                {
                    Console.WriteLine("   FAIL  the server reports it is not running");
                    return 1;
                }
                Console.WriteLine("   ok    a Spring dedicated server is running, started by the ported code");
                return 0;
            }
            finally
            {
                try { server.ExitGame(); } catch { }
            }
        }

        private static bool WaitForPort(int port, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (PortIsHeld(port)) return true;
                Thread.Sleep(250);
            }
            return false;
        }

        /// <summary>
        ///     True when something already holds the UDP port: binding it ourselves fails.
        ///
        ///     Binds the loopback address the script names rather than the wildcard. A wildcard
        ///     bind can succeed alongside a specific one, so it answers a different question than
        ///     the one being asked.
        /// </summary>
        private static bool PortIsHeld(int port)
        {
            try
            {
                using (var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, port))) return false;
            }
            catch (SocketException)
            {
                return true;
            }
        }
    }
}
