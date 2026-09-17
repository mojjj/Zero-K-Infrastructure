using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ratings;
using ZkData;

namespace Tests.Database
{
    /// <summary>
    /// The Whole History Rating pipeline, end to end, against the committed fixture:
    /// read 150 real battles out of the database, rate 30 players, and check the results
    /// are the shape the site depends on.
    ///
    /// These are properties, not pinned numbers. WHR's output depends on iteration count
    /// and ordering, so asserting exact Elo would pin an implementation detail and break
    /// on any tuning. What must not change is the ordering, the convergence and the
    /// bounds.
    /// </summary>
    [TestClass]
    public class WhrPipelineTests
    {
        [TestInitialize]
        public void SetUp()
        {
            RatingPipeline.Require();
        }

        [TestMethod]
        public void Every_player_who_fought_gets_a_rating()
        {
            var played = RatingPipeline.WinLossByAccount();
            Assert.IsTrue(played.Count > 0, "the fixture has no non-spectating players");

            foreach (var accountId in played.Keys)
            {
                var rating = RatingPipeline.Casual.GetPlayerRating(accountId);
                Assert.IsNotNull(rating, "no rating for account " + accountId);
                Assert.IsFalse(float.IsNaN(rating.RealElo), "NaN elo for account " + accountId);
                Assert.IsFalse(float.IsInfinity(rating.RealElo), "infinite elo for account " + accountId);
            }
        }

        [TestMethod]
        public void Ratings_stay_inside_a_plausible_range()
        {
            // Not a tight bound - just wide enough to catch a scale or sign error, which is
            // the failure mode a port would introduce.
            foreach (var accountId in RatingPipeline.WinLossByAccount().Keys)
            {
                var elo = RatingPipeline.Casual.GetPlayerRating(accountId).RealElo;
                Assert.IsTrue(elo > 0 && elo < 5000, "elo out of range for account " + accountId + ": " + elo);
            }
        }

        [TestMethod]
        public void The_best_record_in_the_fixture_outranks_the_worst()
        {
            // Both ends are computed from the database rather than hardcoded, so the test
            // survives a regenerated fixture.
            var records = RatingPipeline.WinLossByAccount()
                .Where(x => x.Value.Item2 >= 10)
                .ToList();
            Assert.IsTrue(records.Count >= 2, "need at least two players with 10+ games; have " + records.Count);

            var best = records.OrderByDescending(x => (double)x.Value.Item1 / x.Value.Item2).First();
            var worst = records.OrderBy(x => (double)x.Value.Item1 / x.Value.Item2).First();

            var bestElo = RatingPipeline.Casual.GetPlayerRating(best.Key).RealElo;
            var worstElo = RatingPipeline.Casual.GetPlayerRating(worst.Key).RealElo;

            Console.WriteLine(string.Format("        best {0} {1}/{2} -> {3:0}, worst {4} {5}/{6} -> {7:0}",
                RatingPipeline.Players[best.Key], best.Value.Item1, best.Value.Item2, bestElo,
                RatingPipeline.Players[worst.Key], worst.Value.Item1, worst.Value.Item2, worstElo));

            Assert.IsTrue(bestElo > worstElo,
                "the best record should rate above the worst: " + bestElo + " vs " + worstElo);
        }

        [TestMethod]
        public void Win_rate_and_rating_agree_on_the_broad_ordering()
        {
            // Rank correlation rather than a per-player claim: WHR weighs opponent strength,
            // so an individual player can legitimately sit against their raw win rate.
            var records = RatingPipeline.WinLossByAccount().Where(x => x.Value.Item2 >= 10).ToList();
            var byWinRate = records.OrderBy(x => (double)x.Value.Item1 / x.Value.Item2).Select(x => x.Key).ToList();
            var byElo = records.OrderBy(x => RatingPipeline.Casual.GetPlayerRating(x.Key).RealElo).Select(x => x.Key).ToList();

            int concordant = 0, total = 0;
            for (var i = 0; i < byWinRate.Count; i++)
                for (var j = i + 1; j < byWinRate.Count; j++)
                {
                    total++;
                    if (byElo.IndexOf(byWinRate[i]) < byElo.IndexOf(byWinRate[j])) concordant++;
                }

            var agreement = (double)concordant / total;
            Console.WriteLine("        rank agreement: " + agreement.ToString("0.00") + " over " + total + " pairs");
            Assert.IsTrue(agreement > 0.6,
                "rating order should broadly follow win rate; agreement was " + agreement.ToString("0.00"));
        }

        [TestMethod]
        public void A_player_with_more_games_is_known_more_precisely()
        {
            var records = RatingPipeline.WinLossByAccount().Where(x => x.Value.Item2 >= 2).ToList();
            var most = records.OrderByDescending(x => x.Value.Item2).First();
            var least = records.OrderBy(x => x.Value.Item2).First();
            Assert.AreNotEqual(most.Key, least.Key, "need players with differing game counts");

            var mostStdev = RatingPipeline.Casual.GetPlayerRating(most.Key).EloStdev;
            var leastStdev = RatingPipeline.Casual.GetPlayerRating(least.Key).EloStdev;

            Console.WriteLine(string.Format("        {0} games -> stdev {1:0.0};  {2} games -> stdev {3:0.0}",
                most.Value.Item2, mostStdev, least.Value.Item2, leastStdev));

            Assert.IsTrue(mostStdev < leastStdev,
                "more games should mean lower uncertainty: " + mostStdev + " vs " + leastStdev);
        }

        [TestMethod]
        public void Predicted_win_chance_favours_the_stronger_side()
        {
            using (var db = new ZkDataContext())
            {
                var records = RatingPipeline.WinLossByAccount().Where(x => x.Value.Item2 >= 10).ToList();
                var strongId = records.OrderByDescending(x => RatingPipeline.Casual.GetPlayerRating(x.Key).RealElo).First().Key;
                var weakId = records.OrderBy(x => RatingPipeline.Casual.GetPlayerRating(x.Key).RealElo).First().Key;

                var strong = db.Accounts.Single(x => x.AccountID == strongId);
                var weak = db.Accounts.Single(x => x.AccountID == weakId);

                var chances = RatingPipeline.Casual.PredictOutcome(
                    new List<List<Account>> { new List<Account> { strong }, new List<Account> { weak } },
                    DateTime.UtcNow);

                Assert.AreEqual(2, chances.Count, "expected one chance per team");
                Console.WriteLine(string.Format("        strong {0:0.00} vs weak {1:0.00}", chances[0], chances[1]));
                Assert.IsTrue(chances[0] > chances[1],
                    "the stronger player should be favoured: " + chances[0] + " vs " + chances[1]);
                Assert.AreEqual(1.0f, chances.Sum(), 0.01f, "win chances should sum to 1");
            }
        }

        [TestMethod]
        public void Players_who_have_not_played_recently_are_rated_but_not_ranked()
        {
            // Rating and ranking are separate, and this is where the difference shows.
            // UpdateLadderRatings only sets onLadder for players with a game inside
            // GlobalConst.LadderActivityDays; the fixture's battles are historical, so
            // everyone here is rated and nobody is ranked.
            //
            // Stated as a test rather than worked around, because it is the reason
            // GetTopPlayers is empty against this data - which would otherwise look like a
            // broken pipeline to whoever reads this next.
            var id = RatingPipeline.BusiestPlayer;
            var rating = RatingPipeline.Casual.GetPlayerRating(id);

            Assert.IsTrue(rating.LastGameDate > 0, "the busiest player should have a computed rating");
            Assert.IsFalse(rating.Ranked, "a player last seen years ago should not be on the ladder");
            Assert.AreEqual(0, RatingPipeline.Casual.GetTopPlayers(10).Count,
                "no fixture player is recent enough to be ranked");
        }

        [TestMethod]
        public void Rating_history_is_recorded_for_the_days_a_player_fought()
        {
            var busiest = RatingPipeline.WinLossByAccount().OrderByDescending(x => x.Value.Item2).First();
            var history = RatingPipeline.Casual.GetPlayerRatingHistory(busiest.Key);

            Assert.IsNotNull(history, "no history for the busiest player");
            Assert.IsTrue(history.Count > 1,
                "a player with " + busiest.Value.Item2 + " games should have several rating days, got " + history.Count);
            foreach (var point in history)
                Assert.IsFalse(float.IsNaN(point.Value), "NaN in rating history at " + point.Key);
        }

        [TestMethod]
        public void Reading_a_rating_twice_gives_the_same_answer()
        {
            // Guards against the caches in WholeHistoryRating handing out different values
            // on successive reads, which would make any downstream display flicker.
            var id = RatingPipeline.WinLossByAccount().OrderByDescending(x => x.Value.Item2).First().Key;
            var first = RatingPipeline.Casual.GetPlayerRating(id).RealElo;
            var second = RatingPipeline.Casual.GetPlayerRating(id).RealElo;
            Assert.AreEqual(first, second, 0f, "the same account gave two different ratings");
        }
    }
}
