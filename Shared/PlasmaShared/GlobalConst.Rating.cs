namespace ZkData
{
    /// <summary>
    /// The rating constants used by the Whole History Rating core (ZkData/Ef/WHR).
    /// Split out of GlobalConst.cs, which cannot compile outside .NET Framework because it
    /// also holds an IContentServiceClient factory. These members are pure, so they can be
    /// linked into the .NET 9 test project - see Tests.Portable/Tests.Portable.csproj.
    /// </summary>
    public static partial class GlobalConst
    {
        public static int LadderAverageDays = 3;
        public static int LadderActivityDays => Mode == ModeType.Live ? 30 : 90;
        public const float EloToNaturalRatingMultiplierSquared = 0.00003313686f;

        /// <summary>whr expected player rating change over time</summary>
        public static float NaturalRatingVariancePerDay(float games) => EloToNaturalRatingMultiplierSquared * 200000 / (games + 400);

        /// <summary>whr expected player rating change per game played</summary>
        public const float NaturalRatingVariancePerGame = EloToNaturalRatingMultiplierSquared * 500;
    }
}
