using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace ZkData
{
    /// <summary>
    /// The EF Core twin of ZkData/DbCompat.cs. Same method names, so the linked call sites
    /// compile against either model unchanged.
    /// </summary>
    public static class DbCompat
    {
        public static void SetCommandTimeoutCompat(this DatabaseFacade database, int seconds)
        {
            database.SetCommandTimeout(seconds);
        }

        public static void MarkModified<T>(this ZkDataContext db, T entity) where T : class
        {
            db.Entry(entity).State = EntityState.Modified;
        }
    }
}
