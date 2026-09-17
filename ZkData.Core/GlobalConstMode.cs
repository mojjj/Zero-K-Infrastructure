namespace ZkData
{
    /// <summary>
    /// The half of <c>GlobalConst</c> that cannot be linked here.
    ///
    /// Production resolves <c>Mode</c> at startup from settings or the compiled
    /// environment, in the same file that holds the content-service factory - so
    /// GlobalConst.cs itself needs WCF and cannot compile on .NET 9. This harness builds a
    /// schema, which does not depend on the deployment mode, so Local is stated explicitly
    /// rather than inherited.
    ///
    /// When the port reaches GlobalConst itself, this goes.
    /// </summary>
    public static partial class GlobalConst
    {
        public static ModeType Mode => ModeType.Local;
    }
}
