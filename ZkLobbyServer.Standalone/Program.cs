using System;
using System.Diagnostics;
using System.Threading;
using ZkData;
using ZkLobbyServer;
using ZkLobbyServer.Api;

namespace ZkLobbyServer.Standalone
{
    /// <summary>
    /// The lobby server as its own process.
    ///
    /// **This is the thing Phase 1 has been for.** Until now `ZkLobbyServer` was a Library with no
    /// entry point: the only way to run it was for the website to start it inside the IIS worker,
    /// which is why an app-pool recycle disconnected every player.
    ///
    /// It starts the same `ServerRunner` the website used to, plus a `LobbyApiHost` so the website
    /// can ask it things from wherever it now lives.
    ///
    /// Configuration is MiscVars, read from the database both halves already share - see
    /// <see cref="LobbyApiConfiguration"/>. Nothing here reads a config file, because a second
    /// config file is a second thing to get out of step.
    ///
    /// **It does not decide anything the website also decides.** If the website's LobbyApiUrl is
    /// unset it will start its own server, and then there are two - so this refuses to start
    /// without a URL configured, rather than quietly becoming the second one.
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            Trace.Listeners.Add(new ConsoleTraceListener());

            // The connection string comes from GlobalConst, exactly as it does for the website -
            // this process deliberately introduces no second way to configure the database.
            string url, secret, prefix;
            try
            {
                url = MiscVar.GetValue(LobbyApiConfiguration.UrlKey);
                secret = MiscVar.GetValue(LobbyApiConfiguration.SecretKey);
                prefix = LobbyApiConfiguration.ListenPrefix(MiscVar.GetValue(LobbyApiConfiguration.ListenPrefixKey));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("could not read configuration from MiscVars: " + ex.Message);
                Console.Error.WriteLine("check GlobalConst's connection string and that the database is reachable.");
                return 2;
            }

            if (!LobbyApiConfiguration.IsRemote(url))
            {
                // Refusing rather than starting: with no URL the website starts its own server, and
                // two servers against one database both accept logins and both run PlanetWars turns.
                Console.Error.WriteLine(
                    "refusing to start: the " + LobbyApiConfiguration.UrlKey + " MiscVar is not set, "
                    + "so the website is still starting a lobby server of its own. Set it to where "
                    + "this process will listen, and " + LobbyApiConfiguration.SecretKey + " too.");
                return 2;
            }

            if (string.IsNullOrWhiteSpace(secret))
            {
                Console.Error.WriteLine(
                    "refusing to start: " + LobbyApiConfiguration.SecretKey + " is not set. The lobby "
                    + "API kicks players and posts as a moderator; it does not listen unauthenticated.");
                return 2;
            }

            var sitePath = args.Length > 0 ? args[0] : Environment.CurrentDirectory;

            Trace.TraceInformation("Starting rating systems");
            Ratings.RatingSystems.Init();
            Ratings.MapRatings.Init();

            Trace.TraceInformation("Starting lobby server");
            var runner = new ServerRunner(sitePath, new StandalonePlanetwarsEventCreator());
            runner.Run();

            using (var apiHost = new LobbyApiHost(new InProcessLobbyServerApi(runner.ZkLobbyServer), secret, prefix))
            {
                apiHost.Start();
                Console.WriteLine("lobby server running; API on " + apiHost.Prefix);
                Console.WriteLine("press Ctrl+C to stop");

                var stop = new ManualResetEventSlim();
                Console.CancelKeyPress += (s, e) => { e.Cancel = true; stop.Set(); };
                stop.Wait();
            }

            Trace.TraceInformation("Stopped");
            return 0;
        }
    }
}
