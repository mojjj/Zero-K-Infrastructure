using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LobbyClient;
using ZkData;

namespace ZkLobbyServer
{
    public class ZkServerTraceListener: TraceListener
    {
        public ZkLobbyServer ZkLobbyServer { get; set; }

        public ZkServerTraceListener(ZkLobbyServer zkLobbyServer = null)
        {
            using (var db = new ZkDataContext())
            {
                var oldEntry = DateTime.UtcNow.AddDays(-14);
                // ExecuteSqlCommandCompat, not ExecuteSqlCommand: EF Core spells it ExecuteSqlRaw, and
                // this was the last call in ZkLobbyServer that only EF6 could compile. The twins are
                // ZkData/DbCompat.cs and ZkData.Core/Ef6Compat/DbCompatCore.cs; both take the same
                // {0} placeholders, so the SQL reaching the server is unchanged.
                db.Database.ExecuteSqlCommandCompat("delete from LogEntries where Time < {0}", oldEntry);
            }
            this.ZkLobbyServer = zkLobbyServer;
        }

        public override void TraceEvent(TraceEventCache eventCache, string source, TraceEventType eventType, int id, string message)
        {
            ProcessEvent(eventType, message);
        }

        public override void TraceEvent(TraceEventCache eventCache, string source, TraceEventType eventType, int id, string format,
                                        params object[] args)
        {
            ProcessEvent(eventType, string.Format(format, args));
        }

        public override void Write(string message)
        {
            ProcessEvent(TraceEventType.Verbose, message);
        }

        public override void WriteLine(string message)
        {
            ProcessEvent(TraceEventType.Verbose, message);
        }

        async Task ProcessEvent(TraceEventType type, string text)
        {
            using (var db = new ZkDataContext()) {
                db.LogEntries.Add(new LogEntry() { Time = DateTime.UtcNow, Message = text, TraceEventType = type });
                await db.SaveChangesAsync();
            }
        }
    }
}