using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using ZkData;
using System.Data.Entity.SqlServer;

namespace ZkData.Core
{
    /// <summary>
    /// Reads Zero-K's data through the EF Core model.
    ///
    /// The schema diff proves the model would BUILD the right database. It cannot prove the
    /// model can READ the existing one: a column mapped to the wrong name, a value
    /// conversion EF6 did implicitly and EF Core does not, a navigation that produces
    /// invalid SQL - none of those change the schema the model emits, so none of them show
    /// up in db/schema/schema.txt. They show up here, as an exception on the first row.
    ///
    ///     ZK_CONNECTION_STRING=...zk_test... dotnet run -- read
    ///
    /// Two passes. The sweep selects from every mapped table, which makes SQL Server check
    /// every column name the model believes in, and materialises whatever rows it finds,
    /// which checks every conversion those rows exercise. The query pass then runs the
    /// shapes production actually issues - the WHR loader's battle query above all - because
    /// a model can map every column correctly and still fail to translate a join.
    /// </summary>
    public static class ReadVerification
    {
        private const int ProbeRows = 20;

        public static int Run(ZkDataContext db)
        {
            var failures = new List<string>();
            int tables = 0, tablesWithRows = 0, rows = 0;

            {
                Console.WriteLine("== sweep: select from every mapped table");
                foreach (var entity in db.Model.GetEntityTypes().OrderBy(e => e.Name))
                {
                    if (entity.GetTableName() == null) continue;
                    tables++;
                    try
                    {
                        int read = entity.HasSharedClrType
                            ? db.Set<Dictionary<string, object>>(entity.Name).AsNoTracking().Take(ProbeRows).ToList().Count
                            : (int)ProbeMethod.MakeGenericMethod(entity.ClrType).Invoke(null, new object[] { db });
                        rows += read;
                        if (read > 0) tablesWithRows++;
                    }
                    catch (Exception ex)
                    {
                        failures.Add(entity.GetTableName() + ": " + Innermost(ex));
                    }
                }

                Console.WriteLine("   " + tables + " tables read, " + tablesWithRows + " of them with rows (" +
                                  rows + " rows materialised)");
                Console.WriteLine("   the empty ones prove their columns exist and are selectable, not their conversions");
                foreach (var failure in failures) Console.WriteLine("   FAILED " + failure);

                Console.WriteLine();
                Console.WriteLine("== queries: the shapes production issues");
                RunQueries(db, failures);
            }

            Console.WriteLine();
            if (Unexercised.Count > 0)
                Console.WriteLine("not exercised by this fixture, valid SQL only: " + string.Join(", ", Unexercised));
            if (failures.Count == 0)
            {
                Console.WriteLine("the EF Core model reads this database.");
                return 0;
            }
            Console.WriteLine(failures.Count + " failure(s) - the model does not read this database yet.");
            return 1;
        }

        private static void RunQueries(ZkDataContext db, List<string> failures)
        {
            // RatingSystems.Init: the query the whole rating pipeline starts from. Its shape is
            // the point - a range filter, a collection Include, AsNoTracking and an OrderBy, all
            // in one statement - so it is reproduced rather than simplified.
            Check("whr loader (filter + Include + AsNoTracking + OrderBy)", failures, () =>
            {
                var from = new DateTime(2000, 1, 1);
                var to = DateTime.Now.AddYears(1);
                var battles = db.SpringBattles
                    .Where(x => x.StartTime > from && x.StartTime < to)
                    .Include(x => x.SpringBattlePlayers)
                    .AsNoTracking()
                    .OrderBy(x => x.StartTime)
                    .ToList();
                var players = battles.Sum(b => b.SpringBattlePlayers.Count);
                return battles.Count + " battles, " + players + " player rows through the navigation";
            });

            // The rating pipeline reads these off the materialised battle, so a battle whose
            // players did not come back would silently rate nothing rather than fail.
            Check("battle players carry their flags", failures, () =>
            {
                var battle = db.SpringBattles.Include(x => x.SpringBattlePlayers).AsNoTracking()
                    .FirstOrDefault(x => x.SpringBattlePlayers.Any(p => !p.IsSpectator));
                if (battle == null) return "no battle with a non-spectator in the fixture";
                var teams = battle.SpringBattlePlayers.Where(p => !p.IsSpectator)
                    .Select(p => p.AllyNumber).Distinct().Count();
                var winners = battle.SpringBattlePlayers.Count(p => p.IsInVictoryTeam && !p.IsSpectator);
                return "battle " + battle.SpringBattleID + ": " + teams + " teams, " + winners + " winners";
            });

            Check("enum column filtered server side", failures, () =>
            {
                var byMode = db.SpringBattles.AsNoTracking()
                    .GroupBy(x => x.Mode).Select(g => new { g.Key, Count = g.Count() })
                    .ToList();
                return string.Join(", ", byMode.Select(m => m.Key + "=" + m.Count));
            });

            Check("aggregate translated to SQL", failures, () =>
            {
                var top = db.Accounts.AsNoTracking()
                    .OrderByDescending(a => a.SpringBattlePlayers.Count())
                    .Select(a => new { a.AccountID, Battles = a.SpringBattlePlayers.Count() })
                    .FirstOrDefault();
                return top == null ? "no accounts" : "busiest account " + top.AccountID + " with " + top.Battles + " battles";
            });

            // EF6's SqlFunctions.PatIndex, which ClansController uses four times to reject a
            // clan whose name or shortcut collides with an existing one. The mapping is
            // asserted on the SQL, not just on the result: a shim that silently returned
            // null would still "pass" a row count here, because no clan collides in the
            // fixture, and every clan name would then be accepted in production.
            Check("SqlFunctions.PatIndex translates to the built-in PATINDEX", failures, () =>
            {
                var shortcut = "ZK";
                var query = db.Clans.AsNoTracking()
                    .Where(x => SqlFunctions.PatIndex(shortcut, x.Shortcut) > 0);
                var sql = query.ToQueryString();
                if (!sql.Contains("PATINDEX("))
                    throw new Exception("no PATINDEX in the generated SQL: " + sql);
                // IsBuiltIn(); without it EF Core emits [dbo].[PATINDEX], which SQL Server
                // rejects, and EF6 never did.
                if (sql.Contains("[PATINDEX]"))
                    throw new Exception("PATINDEX emitted as a user function: " + sql);
                return query.Count() + " clan(s) match, from SQL that SQL Server accepted";
            });

            // The five many-to-many joins are the part of the model written by hand, so they
            // are the part most likely to be wrong. Each one is both translated and executed:
            // the generated SQL has to name the join table the database actually has, and SQL
            // Server has to accept the statement. That holds even where the fixture has no
            // rows - which is the case for all five, and is why the result says so rather
            // than reporting a pass.
            JoinTable("Clan.Events", "EventClan", failures, db.Clans.SelectMany(c => c.Events));
            JoinTable("Event.Accounts", "EventAccount", failures, db.Events.SelectMany(e => e.Accounts));
            JoinTable("Event.Factions", "EventFaction", failures, db.Events.SelectMany(e => e.Factions));
            JoinTable("Event.Planets", "EventPlanet", failures, db.Events.SelectMany(e => e.Planets));
            JoinTable("Event.SpringBattles", "EventSpringBattle", failures, db.Events.SelectMany(e => e.SpringBattles));

            Check("datetime round trip", failures, () =>
            {
                var span = db.SpringBattles.AsNoTracking()
                    .GroupBy(x => 1)
                    .Select(g => new { Min = g.Min(x => x.StartTime), Max = g.Max(x => x.StartTime) })
                    .FirstOrDefault();
                if (span == null) return "no battles";
                if (span.Min.Year < 2000 || span.Max > DateTime.Now.AddDays(1))
                    throw new Exception("implausible range " + span.Min + " .. " + span.Max);
                return span.Min.ToString("yyyy-MM-dd") + " .. " + span.Max.ToString("yyyy-MM-dd");
            });

            Check("string facets read back (varchar and nvarchar)", failures, () =>
            {
                var account = db.Accounts.AsNoTracking().FirstOrDefault(a => a.Name != null);
                if (account == null) return "no named account";
                return "account " + account.AccountID + " name length " + account.Name.Length;
            });

            Check("command timeout through DbCompat", failures, () =>
            {
                db.Database.SetCommandTimeoutCompat(240);
                return "set to " + db.Database.GetCommandTimeout();
            });
        }


        /// <summary>
        /// Translates and runs a many-to-many navigation, checking the SQL names the join
        /// table the database has rather than one EF Core invented.
        /// </summary>
        private static void JoinTable<T>(string navigation, string table, List<string> failures, IQueryable<T> query)
            where T : class
        {
            try
            {
                var sql = query.AsNoTracking().Take(ProbeRows).ToQueryString();
                if (!sql.Contains("[" + table + "]"))
                    throw new Exception("the SQL does not mention [" + table + "]");
                var rows = query.AsNoTracking().Take(ProbeRows).ToList().Count;
                Console.WriteLine("   " + (rows > 0 ? "ok    " : "empty ") + navigation + " through " + table +
                                  " - " + (rows > 0 ? rows + " rows" : "valid SQL, no rows in this fixture"));
                if (rows == 0) Unexercised.Add(navigation);
            }
            catch (Exception ex)
            {
                Console.WriteLine("   FAIL  " + navigation + " through " + table);
                failures.Add(navigation + ": " + Innermost(ex));
            }
        }

        private static readonly List<string> Unexercised = new List<string>();

        private static void Check(string name, List<string> failures, Func<string> body)
        {
            try
            {
                Console.WriteLine("   ok    " + name + " - " + body());
            }
            catch (Exception ex)
            {
                Console.WriteLine("   FAIL  " + name);
                failures.Add(name + ": " + Innermost(ex));
            }
        }

        private static readonly MethodInfo ProbeMethod =
            typeof(ReadVerification).GetMethod(nameof(Probe), BindingFlags.NonPublic | BindingFlags.Static);

        private static int Probe<T>(ZkDataContext db) where T : class
            => db.Set<T>().AsNoTracking().Take(ProbeRows).ToList().Count;

        private static string Innermost(Exception ex)
        {
            while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
            var message = ex.Message;
            for (var inner = ex.InnerException; inner != null; inner = inner.InnerException)
                message = inner.Message;
            return message.Replace("\r", " ").Replace("\n", " ");
        }
    }
}
