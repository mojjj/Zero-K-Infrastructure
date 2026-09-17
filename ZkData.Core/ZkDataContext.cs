using System;
using Microsoft.EntityFrameworkCore;
using ZkData.Core.Ef6Compat;

namespace ZkData
{
    /// <summary>
    /// The EF Core context, deliberately carrying the same name and namespace as the EF6
    /// one it will replace.
    ///
    /// That is not cosmetic. Entity classes call <c>new ZkDataContext()</c> from their own
    /// business logic - Account, Galaxy, MiscVar, Avatar and four others - so a context
    /// under any other name would mean editing those files, which would break the EF6
    /// build that production runs on. Same name, same DbSet names, different provider
    /// underneath: the entity sources stay byte-identical between the two models, and the
    /// schema diff stays honest.
    ///
    /// The DbSets below are generated from the EF6 context, all 85 of them, so neither can
    /// quietly gain a table the other lacks.
    /// </summary>
    public partial class ZkDataContext : DbContext
    {
        /// <summary>Set by the harness; production would read configuration.</summary>
        public static string ConnectionString =
            Environment.GetEnvironmentVariable("ZK_CONNECTION_STRING");

        public ZkDataContext() { }

        public ZkDataContext(DbContextOptions<ZkDataContext> options) : base(options) { }

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            if (!options.IsConfigured) options.UseSqlServer(ConnectionString);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // EF6 applied [Index] on properties by convention; EF Core has no such
            // convention, so the attributes are read back and declared explicitly.
            // EF6 accepted several [Key] properties ordered by [Column(Order)]; EF Core
            // requires the key declared explicitly. Do this before the indexes, because a
            // primary key is itself an index.
            modelBuilder.ApplyEf6CompositeKeys();
            modelBuilder.ApplyEf6IndexAttributes();

            // EF Core maps DateTime to datetime2; EF6 mapped it to datetime, and the
            // database has datetime.
            modelBuilder.ApplyEf6DateTimeMapping();

            // EF6's fluent relationship configuration, translated - see
            // ZkDataContext.Relationships.cs and the generator beside it.
            ConfigureRelationships(modelBuilder);
            ConfigureRelationshipsByHand(modelBuilder);

            // Column facets read out of db/schema/schema.txt - see
            // ZkDataContext.ColumnFacets.cs and the generator beside it.
            ConfigureColumnFacets(modelBuilder);

        }

        /// <summary>Generated in ZkDataContext.Relationships.cs.</summary>
        partial void ConfigureRelationships(ModelBuilder modelBuilder);

        /// <summary>The handful the generator leaves alone - ZkDataContext.RelationshipsByHand.cs.</summary>
        partial void ConfigureRelationshipsByHand(ModelBuilder modelBuilder);

        /// <summary>Generated in ZkDataContext.ColumnFacets.cs.</summary>
        partial void ConfigureColumnFacets(ModelBuilder modelBuilder);

        /// <summary>
        /// EF6's change-tracking wrapper, which IEntityAfterChange implementations take.
        /// Kept so those entity classes compile unchanged.
        /// </summary>
        public class EntityEntry
        {
            public object Entity { get; private set; }
            public System.Data.Entity.EntityState State { get; private set; }
            public ZkDataContext Context { get; private set; }

            public EntityEntry(object entity, System.Data.Entity.EntityState state, ZkDataContext context)
            {
                Entity = entity;
                State = state;
                Context = context;
            }
        }

        public virtual DbSet<AbuseReport> AbuseReports { get; set; }
        public virtual DbSet<Account> Accounts { get; set; }
        public virtual DbSet<AccountBattleAward> AccountBattleAwards { get; set; }
        public virtual DbSet<AccountCampaignJournalProgress> AccountCampaignJournalProgress { get; set; }
        public virtual DbSet<AccountCampaignProgress> AccountCampaignProgress { get; set; }
        public virtual DbSet<AccountCampaignVar> AccountCampaignVars { get; set; }
        public virtual DbSet<AccountForumVote> AccountForumVotes { get; set; }
        public virtual DbSet<AccountIP> AccountIPs { get; set; }
        public virtual DbSet<AccountMapBan> AccountMapBans { get; set; }
        public virtual DbSet<AccountRating> AccountRatings { get; set; }
        public virtual DbSet<AccountPlanet> AccountPlanets { get; set; }
        public virtual DbSet<AccountRole> AccountRoles { get; set; }
        public virtual DbSet<AccountUnlock> AccountUnlocks { get; set; }
        public virtual DbSet<AccountUserID> AccountUserIDs { get; set; }
        public virtual DbSet<Avatar> Avatars { get; set; }
        public virtual DbSet<BlockedCompany> BlockedCompanies { get; set; }
        public virtual DbSet<BlockedHost> BlockedHosts { get; set; }
        public virtual DbSet<Campaign> Campaigns { get; set; }
        public virtual DbSet<CampaignEvent> CampaignEvents { get; set; }
        public virtual DbSet<CampaignJournal> CampaignJournals { get; set; }
        public virtual DbSet<CampaignJournalVar> CampaignJournalVars { get; set; }
        public virtual DbSet<CampaignLink> CampaignLinks { get; set; }
        public virtual DbSet<CampaignPlanet> CampaignPlanets { get; set; }
        public virtual DbSet<CampaignPlanetVar> CampaignPlanetVars { get; set; }
        public virtual DbSet<CampaignVar> CampaignVars { get; set; }
        public virtual DbSet<Clan> Clans { get; set; }
        public virtual DbSet<Commander> Commanders { get; set; }
        public virtual DbSet<CommanderDecoration> CommanderDecorations { get; set; }
        public virtual DbSet<CommanderDecorationIcon> CommanderDecorationIcons { get; set; }
        public virtual DbSet<CommanderDecorationSlot> CommanderDecorationSlots { get; set; }
        public virtual DbSet<CommanderModule> CommanderModules { get; set; }
        public virtual DbSet<CommanderSlot> CommanderSlots { get; set; }
        public virtual DbSet<Contribution> Contributions { get; set; }
        public virtual DbSet<ContributionJar> ContributionJars { get; set; }
        public virtual DbSet<Event> Events { get; set; }
        public virtual DbSet<Faction> Factions { get; set; }
        public virtual DbSet<FactionTreaty> FactionTreaties { get; set; }
        public virtual DbSet<ForumCategory> ForumCategories { get; set; }
        public virtual DbSet<ForumLastRead> ForumLastReads { get; set; }
        public virtual DbSet<ForumPost> ForumPosts { get; set; }
        public virtual DbSet<ForumPostEdit> ForumPostEdits { get; set; }
        public virtual DbSet<ForumThread> ForumThreads { get; set; }
        public virtual DbSet<ForumThreadLastRead> ForumThreadLastReads { get; set; }
        public virtual DbSet<Galaxy> Galaxies { get; set; }
        public virtual DbSet<KudosPurchase> KudosPurchases { get; set; }
        public virtual DbSet<Link> Links { get; set; }
        public virtual DbSet<MapRating> MapRatings { get; set; }
        public virtual DbSet<Mission> Missions { get; set; }
        public virtual DbSet<MissionScore> MissionScores { get; set; }
        public virtual DbSet<News> News { get; set; }
        public virtual DbSet<Planet> Planets { get; set; }
        public virtual DbSet<PlanetFaction> PlanetFactions { get; set; }
        public virtual DbSet<PlanetOwnerHistory> PlanetOwnerHistories { get; set; }
        public virtual DbSet<PlanetStructure> PlanetStructures { get; set; }
        public virtual DbSet<Poll> Polls { get; set; }
        public virtual DbSet<PollOption> PollOptions { get; set; }
        public virtual DbSet<PollVote> PollVotes { get; set; }
        public virtual DbSet<Punishment> Punishments { get; set; }
        public virtual DbSet<Rating> Ratings { get; set; }
        public virtual DbSet<Resource> Resources { get; set; }
        public virtual DbSet<ResourceContentFile> ResourceContentFiles { get; set; }
        public virtual DbSet<ResourceDependency> ResourceDependencies { get; set; }
        public virtual DbSet<RoleType> RoleTypes { get; set; }
        public virtual DbSet<RoleTypeHierarchy> RoleTypeHierarchies { get; set; }
        public virtual DbSet<SpringBattle> SpringBattles { get; set; }
        public virtual DbSet<SpringBattlePlayer> SpringBattlePlayers { get; set; }
        public virtual DbSet<StructureType> StructureTypes { get; set; }
        public virtual DbSet<TreatyEffect> TreatyEffects { get; set; }
        public virtual DbSet<TreatyEffectType> TreatyEffectTypes { get; set; }
        public virtual DbSet<Unlock> Unlocks { get; set; }
        public virtual DbSet<MiscVar> MiscVars { get; set; }
        public virtual DbSet<LobbyChatHistory> LobbyChatHistories { get; set; }
        public virtual DbSet<LogEntry> LogEntries { get; set; }
        public virtual DbSet<Word> IndexWords { get; set; }
        public virtual DbSet<ForumPostWord> IndexForumPosts { get; set; }
        public virtual DbSet<AccountRelation> AccountRelations { get; set; }
        public virtual DbSet<SpringBattleBot> SpringBattleBots { get; set; }
        public virtual DbSet<SpringFilesUnitsyncAttempt> SpringFilesUnitsyncAttempts { get; set; }
        public virtual DbSet<Autohost> Autohosts { get; set; }
        public virtual DbSet<MapPollOption> MapPollOptions { get; set; }
        public virtual DbSet<MapPollOutcome> MapPollOutcomes { get; set; }
        public virtual DbSet<LobbyChannelTopic> LobbyChannelTopics { get; set; }
        public virtual DbSet<LobbyNews> LobbyNews { get; set; }
        public virtual DbSet<GameMode> GameModes { get; set; }
    }
}
