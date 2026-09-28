using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using LobbyClient;
using Newtonsoft.Json;
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
        private static string joinedName;
        private static SpringBattleContext startedContext;

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
            var joined = new ManualResetEventSlim(false);
            server.PlayerJoined += (s, e) => { joinedName = e.Username; joined.Set(); };
            var battleStarted = new ManualResetEventSlim(false);
            server.BattleStarted += (s, e) => { startedContext = e; battleStarted.Set(); };
            var gameOver = new ManualResetEventSlim(false);
            server.GameOver += (s, e) => gameOver.Set();

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

                // A real player. spring-headless connects to the game above and plays it; the engine
                // then tells the autohost, LobbyClient's Talker receives that over UDP, and
                // DedicatedServer raises PlayerJoined. Everything in that chain is the port's.
                var headless = Path.Combine(Path.GetDirectoryName(binary), "spring-headless");
                if (!File.Exists(headless))
                {
                    Console.WriteLine("   ----  no spring-headless beside the dedicated server; stopping at 'a server is up'");
                    return 0;
                }

                var clientScript = Path.Combine(paths.WritableDirectory, "client-script.txt");
                // MyPasswd must match the player's ScriptPassword in the host script, or the engine
                // answers "server requested quit or rejected connection" and the client exits before
                // it ever joins - which looks identical to the autohost path being broken.
                File.WriteAllText(clientScript,
                    "[GAME]\n{\n\tHostIP=127.0.0.1;\n\tHostPort=" + port + ";\n\tIsHost=0;\n\tMyPlayerName=probe;\n\tMyPasswd="
                    + context.Players[0].ScriptPassword + ";\n}\n");

                using (var client = Process.Start(new ProcessStartInfo(headless, "\"" + clientScript + "\"")
                {
                    WorkingDirectory = Path.GetDirectoryName(headless),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                }))
                {
                    // Drained, or the pipes fill and the client stalls before it ever joins.
                    client.OutputDataReceived += (s2, e2) => { };
                    client.ErrorDataReceived += (s2, e2) => { if (e2.Data != null && e2.Data.Contains("ExitSpringProcess")) Console.WriteLine("     client refused: " + e2.Data); };
                    client.BeginOutputReadLine();
                    client.BeginErrorReadLine();

                    try
                    {
                        if (!joined.Wait(TimeSpan.FromSeconds(90)))
                        {
                            Console.WriteLine("   FAIL  a client connected to the engine but DedicatedServer never raised PlayerJoined");
                            Console.WriteLine("         that is the Talker UDP path: the engine reports to it and it raises the event");
                            return 1;
                        }
                    }
                    finally
                    {
                        try { if (!client.HasExited) client.Kill(); } catch { }
                    }
                }

                Console.WriteLine("   ok    a real player joined, and the ported code was told: PlayerJoined(" + joinedName + ")");

                // The end of a game, synthesised.
                //
                // The engine will not send SERVER_STARTPLAYING here: the host script uses
                // StartPosType=2, so Spring waits for every player to place a start position and
                // ready up, which a headless client never does. Without it Context.IngameStartTime
                // stays null and DedicatedServer refuses to raise GameOver at all - so GAMEOVER on
                // its own would be inert, and a check built on it would pass by never being reached.
                //
                // Both packets are built to the layout Talker parses, which is the layout the engine
                // emits; the PLAYER_JOINED above arrived in exactly this shape from a real one.
                var autohostPort = AutohostPortFrom(script);
                if (autohostPort == 0)
                {
                    Console.WriteLine("   FAIL  no AutohostPort in the generated script, so nothing can be sent to the Talker");
                    return 1;
                }

                Send(autohostPort, StartPlaying("0123456789abcdef0123456789abcdef", "probe.sdfz"));
                if (!battleStarted.Wait(TimeSpan.FromSeconds(15)))
                {
                    Console.WriteLine("   FAIL  SERVER_STARTPLAYING did not reach BattleStarted");
                    return 1;
                }
                Console.WriteLine("   ok    SERVER_STARTPLAYING parsed: BattleStarted, replay=" + startedContext?.ReplayName
                                  + ", engineBattleID=" + startedContext?.EngineBattleID);

                Send(autohostPort, GameOver(0, new byte[] { 0 }));
                if (!gameOver.Wait(TimeSpan.FromSeconds(15)))
                {
                    Console.WriteLine("   FAIL  SERVER_GAMEOVER did not reach GameOver");
                    return 1;
                }

                var winners = server.Context?.ActualPlayers?.Where(x => x.IsVictoryTeam).Select(x => x.Name).ToList();
                Console.WriteLine("   ok    SERVER_GAMEOVER parsed: GameOver, duration=" + server.Context?.Duration
                                  + "s, winners=[" + string.Join(",", winners ?? new List<string>()) + "]");

                if (server.Context?.GameEndedOk != true)
                {
                    Console.WriteLine("   FAIL  the context does not record the game as ended, so nothing downstream would store a result");
                    return 1;
                }
                Console.WriteLine("   ok    the battle context is complete - this is what BattleResultHandler is handed");

                // Captured, so the other end of the chain can be fed something a real engine
                // produced rather than something a test wrote. tools/battle-result-probe reads it.
                var capture = Environment.GetEnvironmentVariable("ZK_BATTLE_CONTEXT_OUT");
                if (!string.IsNullOrEmpty(capture))
                {
                    File.WriteAllText(capture, JsonConvert.SerializeObject(server.Context, Formatting.Indented));
                    Console.WriteLine("   ok    captured the context to " + capture + " for the storing end");
                }
                return 0;
            }
            finally
            {
                try { server.ExitGame(); } catch { }
            }
        }

        /// <summary>The port ScriptGenerator wrote for the Talker, read back out of the script.</summary>
        private static int AutohostPortFrom(string script)
        {
            var match = Regex.Match(script, @"AutohostPort=(\d+);");
            return match.Success ? int.Parse(match.Groups[1].Value) : 0;
        }

        private static void Send(int port, byte[] payload)
        {
            using (var client = new UdpClient())
                client.Send(payload, payload.Length, "127.0.0.1", port);
        }

        /// <summary>[2][size:4 LE][gameID:16][replay name] - the layout Talker reads.</summary>
        private static byte[] StartPlaying(string gameIdHex, string replayName)
        {
            var name = Encoding.UTF8.GetBytes(replayName);
            var gameId = Enumerable.Range(0, 16).Select(i => Convert.ToByte(gameIdHex.Substring(i * 2, 2), 16)).ToArray();
            var packet = new byte[21 + name.Length];
            packet[0] = (byte)2;
            packet[1] = (byte)(packet.Length & 0xFF);
            packet[2] = (byte)((packet.Length >> 8) & 0xFF);
            packet[3] = (byte)((packet.Length >> 16) & 0xFF);
            packet[4] = (byte)((packet.Length >> 24) & 0xFF);
            Array.Copy(gameId, 0, packet, 5, 16);
            Array.Copy(name, 0, packet, 21, name.Length);
            return packet;
        }

        /// <summary>[3][size][playerNumber][winning ally teams] - the layout Talker reads.</summary>
        private static byte[] GameOver(byte playerNumber, byte[] winningAllyTeams)
        {
            var packet = new byte[3 + winningAllyTeams.Length];
            packet[0] = (byte)3;
            packet[1] = (byte)packet.Length;
            packet[2] = playerNumber;
            Array.Copy(winningAllyTeams, 0, packet, 3, winningAllyTeams.Length);
            return packet;
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
