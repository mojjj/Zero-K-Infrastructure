// Dead in ChartsController: named by a using, never mentioned again.
namespace System.Web.Helpers { internal static class DeadUsingMarker { } }

// EntityFramework.Extensions is NOT here, and nearly was. PlanetwarsAdminController's using
// looks dead by the same test - one occurrence, the using line - but the namespace supplies
// DbSet<T>.Delete() and IQueryable<T>.Update(), EF6's batch operations, which the controller
// calls six times without naming the namespace again. An empty shim would have let it compile
// and thrown at runtime. EF Core has ExecuteDelete/ExecuteUpdate, which are not the same API,
// so that controller needs porting rather than shimming.
