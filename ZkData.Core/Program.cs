using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using ZkData;

namespace ZkData.Core
{
    /// <summary>
    /// Builds a database from the EF Core model so its schema can be compared with the one
    /// the EF6 migrations produce (db/schema/schema.txt).
    ///
    ///     ZK_CONNECTION_STRING=... dotnet run -- create   build a database from the model
    ///     ZK_CONNECTION_STRING=... dotnet run -- read     read an existing one through it
    ///     ZK_CONNECTION_STRING=... dotnet run -- write    write to one through it (rolled back)
    ///     ZK_CONNECTION_STRING=... dotnet run -- rate     run the rating pipeline, print the result
    ///
    /// The comparison is the point: it is how the port knows whether the model is right,
    /// rather than whether it compiles.
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            var command = args.FirstOrDefault() ?? "summary";
            if (string.IsNullOrEmpty(ZkDataContext.ConnectionString))
            {
                Console.Error.WriteLine("Set ZK_CONNECTION_STRING first - see db/README.md.");
                return 2;
            }

            try
            {
                using (var db = new ZkDataContext())
                {
                    switch (command)
                    {
                        case "create":
                            Console.WriteLine("dropping and recreating from the EF Core model...");
                            db.Database.EnsureDeleted();
                            db.Database.EnsureCreated();
                            Console.WriteLine("created");
                            break;

                        case "read":
                            return ReadVerification.Run(db);

                        case "write":
                            return WriteVerification.Run(db);

                        case "rate":
                            return RatingRun.Run(db, args.Skip(1).FirstOrDefault());

                        // A development convenience, and deliberately a thin one: the fixture
                        // stores PasswordBcrypt as NULL for every account, so there is nothing to
                        // sign in as while testing the site by hand.
                        //
                        // It calls Account.SetPasswordPlain, which is production code - the same
                        // BCrypt(MD5-of-password) the lobby server writes at registration - so
                        // logging in afterwards exercises the real verification rather than a
                        // bypass. Point it at the test database, not at anything real.
                        case "set-password":
                        {
                            var name = args.Skip(1).FirstOrDefault();
                            var password = args.Skip(2).FirstOrDefault();
                            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(password))
                            {
                                Console.Error.WriteLine("usage: set-password <account name> <password>");
                                return 2;
                            }
                            var account = db.Accounts.FirstOrDefault(x => x.Name == name);
                            if (account == null)
                            {
                                Console.Error.WriteLine("no account named " + name);
                                return 2;
                            }
                            account.SetPasswordPlain(password);
                            db.SaveChanges();
                            Console.WriteLine("set a password for " + account.Name + " (AccountID "
                                              + account.AccountID + ")");
                            return 0;
                        }

                        case "summary":
                        default:
                            var model = db.Model;
                            var entities = model.GetEntityTypes().ToList();
                            Console.WriteLine("entity types: " + entities.Count);
                            Console.WriteLine("properties:   " + entities.Sum(e => e.GetProperties().Count()));
                            Console.WriteLine("indexes:      " + entities.Sum(e => e.GetIndexes().Count()));
                            Console.WriteLine("foreign keys: " + entities.Sum(e => e.GetForeignKeys().Count()));
                            break;
                    }
                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.GetType().Name + ": " + ex.Message);
                for (var inner = ex.InnerException; inner != null; inner = inner.InnerException)
                    Console.Error.WriteLine("  inner: " + inner.Message);
                return 1;
            }
        }
    }
}
