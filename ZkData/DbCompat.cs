using System.Data.Entity;

namespace ZkData
{
    /// <summary>
    /// Call sites that EF6 and EF Core spell differently, behind one name.
    ///
    /// EF6 has <c>Database.CommandTimeout</c> as a settable property; EF Core has
    /// <c>SetCommandTimeout</c>. C# has no extension properties, so the call site has to
    /// move rather than the API - this is that move, made once, with an EF Core twin in
    /// ZkData.Core. Same for marking an entity modified.
    ///
    /// See ZkData/EFCORE-MIGRATION.md. When the port completes, the EF6 version goes and
    /// the EF Core one stays.
    /// </summary>
    public static class DbCompat
    {
        public static void SetCommandTimeoutCompat(this Database database, int seconds)
        {
            database.CommandTimeout = seconds;
        }

        public static void MarkModified<T>(this ZkDataContext db, T entity) where T : class
        {
            db.Entry(entity).State = EntityState.Modified;
        }
    }
}
