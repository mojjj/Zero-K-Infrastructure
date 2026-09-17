using System;
using System.Linq;
using System.Linq.Expressions;
using EFCore = Microsoft.EntityFrameworkCore;

namespace System.Data.Entity
{
    /// <summary>
    /// Makes <c>using System.Data.Entity;</c> resolve on .NET 9, mapping the handful of
    /// EF6 APIs the entity classes use onto their EF Core equivalents.
    ///
    /// Same reasoning as the <c>[Index]</c> shim: EF6 still runs production, so the entity
    /// sources cannot be rewritten yet, and they are linked into both models unmodified so
    /// that a schema difference means a real difference. The surface needed turns out to be
    /// tiny - <c>Include</c>, <c>AsNoTracking</c> and <c>EntityState</c> - because the
    /// entity classes do very little data access themselves.
    ///
    /// This is scaffolding with an expiry date. When the entity sources move to EF Core
    /// for good, their usings change and this file goes.
    /// </summary>
    public static class QueryableExtensions
    {
        public static IQueryable<T> Include<T, TProperty>(this IQueryable<T> source,
            Expression<Func<T, TProperty>> path) where T : class
            => EFCore.EntityFrameworkQueryableExtensions.Include(source, path);

        public static IQueryable<T> Include<T>(this IQueryable<T> source, string path) where T : class
            => EFCore.EntityFrameworkQueryableExtensions.Include(source, path);

        public static IQueryable<T> AsNoTracking<T>(this IQueryable<T> source) where T : class
            => EFCore.EntityFrameworkQueryableExtensions.AsNoTracking(source);
    }

    /// <summary>
    /// EF6 spelled this <c>System.Data.Entity.EntityState</c>.
    ///
    /// Both sides of every comparison have to be this type, not EF Core's: entity code
    /// writes <c>entry.State == EntityState.Modified</c>, and two structurally identical
    /// enums do not compare equal. So ZkDataContext.EntityEntry carries this one, and the
    /// conversion to EF Core's happens here, once.
    /// </summary>
    public enum EntityState
    {
        Detached = EFCore.EntityState.Detached,
        Unchanged = EFCore.EntityState.Unchanged,
        Added = EFCore.EntityState.Added,
        Deleted = EFCore.EntityState.Deleted,
        Modified = EFCore.EntityState.Modified,
    }
}
