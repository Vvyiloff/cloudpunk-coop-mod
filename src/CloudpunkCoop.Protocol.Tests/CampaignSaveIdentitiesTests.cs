using System;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace CloudpunkCoop.Protocol.Tests
{
    public class CampaignSaveIdentitiesTests
    {
        private const string Id = "123456789abcdef0123456789abcdef0";
        private const string OtherId = "abcdef0123456789abcdef0123456789";
        private static string Hash(int revision) => revision.ToString("x64");

        [Fact]
        public void SuccessfulSaveLineageSurvivesJsonRestartAndUnrelatedSaveIsIsolated()
        {
            var registry = new CampaignSaveIdentities();
            string campaign = registry.Resolve(Hash(1));
            Assert.True(Guid.TryParseExact(campaign, "N", out var uuid));
            Assert.NotEqual(Guid.Empty, uuid);
            Assert.Equal(campaign, registry.Resolve(Hash(1)));
            registry.Associate(Hash(2), campaign);
            registry.Associate(Hash(3), campaign);
            string unrelated = registry.Resolve(Hash(4));
            Assert.NotEqual(campaign, unrelated);
            var json = JsonSerializer.Serialize(registry.Export());
            var restored = new CampaignSaveIdentities(
                JsonSerializer.Deserialize<CampaignSaveAlias[]>(json)!);
            Assert.Equal(4, restored.Count);
            foreach (int revision in new[] { 1, 2, 3 })
                Assert.Equal(campaign, restored.Resolve(Hash(revision)));
            Assert.Equal(unrelated, restored.Resolve(Hash(4)));
            restored.Associate(Hash(5), campaign);
            Assert.Equal(campaign, restored.Resolve(Hash(5)));
            Assert.Equal(4, registry.Count);
        }

        [Fact]
        public void StrictlyValidatedUppercaseHexIsNormalizedAndAssociationIsIdempotent()
        {
            string hash = new string('a', 64);
            var registry = new CampaignSaveIdentities(new[] {
                new CampaignSaveAlias(hash.ToUpperInvariant(), Id.ToUpperInvariant()) });
            Assert.Equal(Id, registry.Resolve(hash));
            registry.Associate(hash.ToUpperInvariant(), Id.ToUpperInvariant());
            registry.Associate(Hash(2), Id.ToUpperInvariant());
            Assert.Equal(2, registry.Count);
            Assert.All(registry.Export(), entry => {
                Assert.Equal(entry.RevisionHash.ToLowerInvariant(), entry.RevisionHash);
                Assert.Equal(Id, entry.CampaignId);
            });
        }

        [Fact]
        public void ConflictingAssociationNeverRebindsOrChangesSnapshot()
        {
            var registry = new CampaignSaveIdentities(new[] {
                new CampaignSaveAlias(Hash(1), Id), new CampaignSaveAlias(Hash(2), OtherId) });
            var before = registry.Export();
            Assert.Throws<InvalidOperationException>(() => registry.Associate(Hash(1), OtherId));
            Assert.Equal(before.ToArray(), registry.Export().ToArray());
            Assert.Equal(Id, registry.Resolve(Hash(1)));
            Assert.Equal(OtherId, registry.Resolve(Hash(2)));
        }

        [Theory]
        [InlineData(null)] [InlineData("")] [InlineData("campaign-1")]
        [InlineData("0123")]
        public void InvalidHashesAreRejectedWithoutMutation(string? hash)
        {
            var registry = new CampaignSaveIdentities();
            Assert.ThrowsAny<ArgumentException>(() => registry.Resolve(hash!));
            Assert.ThrowsAny<ArgumentException>(() => registry.Associate(hash!, Id));
            Assert.Equal(0, registry.Count);
        }

        [Fact]
        public void MalformedExactLengthHashesAndNonNOrEmptyIdsAreRejectedBeforeMutation()
        {
            var registry = new CampaignSaveIdentities(new[] { new CampaignSaveAlias(Hash(1), Id) });
            var before = registry.Export();
            foreach (string hash in new[] { new string('g', 64), new string('０', 64),
                " " + Hash(1).Substring(1), Hash(1) + " " })
            {
                Assert.ThrowsAny<ArgumentException>(() => registry.Resolve(hash));
                Assert.ThrowsAny<ArgumentException>(() => registry.Associate(hash, Id));
            }
            foreach (string? id in new[] { null, "", "campaign-1", new string('0', 32),
                new string('g', 32), new string('Ａ', 32), Guid.ParseExact(Id, "N").ToString("D"),
                " " + Id, Id + " " })
                Assert.ThrowsAny<ArgumentException>(() => registry.Associate(Hash(2), id!));
            Assert.Throws<ArgumentException>(() => registry.Associate(Hash(2), OtherId));
            Assert.Equal(before.ToArray(), registry.Export().ToArray());
        }

        [Fact]
        public void CorruptImportsRejectEntireConstructionAndLeaveSourceRegistryUntouched()
        {
            var source = new CampaignSaveIdentities(new[] { new CampaignSaveAlias(Hash(1), Id) });
            var before = source.Export();
            CampaignSaveAlias[][] invalid = {
                new[] { before[0], new CampaignSaveAlias(Hash(2), "campaign-1") },
                new[] { before[0], new CampaignSaveAlias("bad", Id) },
                new[] { before[0], null! },
                new[] { before[0], new CampaignSaveAlias(Hash(1), Id) },
                new[] { before[0], new CampaignSaveAlias(Hash(1), OtherId) },
                new[] { new CampaignSaveAlias(new string('a', 64), Id),
                    new CampaignSaveAlias(new string('A', 64), Id) },
                new[] { new CampaignSaveAlias(Hash(2), null!) },
                new[] { new CampaignSaveAlias(null!, Id) },
                new[] { new CampaignSaveAlias(Hash(2), new string('0', 32)) }
            };
            foreach (var entries in invalid)
            {
                CampaignSaveIdentities? imported = null;
                Assert.ThrowsAny<ArgumentException>(() => imported = new CampaignSaveIdentities(entries));
                Assert.Null(imported);
            }
            Assert.Throws<ArgumentNullException>(() => new CampaignSaveIdentities(null!));
            Assert.Equal(before.ToArray(), source.Export().ToArray());
        }

        [Fact]
        public void ImportAndExportAreDetachedAndArrayMutationCannotChangeRegistry()
        {
            var input = new[] { new CampaignSaveAlias(Hash(1), Id) };
            var registry = new CampaignSaveIdentities(input);
            input[0] = new CampaignSaveAlias(Hash(1), OtherId);
            Assert.Equal(Id, registry.Resolve(Hash(1)));
            CampaignSaveAlias[] snapshot = registry.Export();
            var independentSnapshot = registry.Export();
            Assert.NotSame(snapshot, independentSnapshot);
            var changedRecord = snapshot[0] with { CampaignId = OtherId };
            Assert.NotEqual(changedRecord, snapshot[0]);
            snapshot[0] = changedRecord;
            Assert.Equal(Id, registry.Resolve(Hash(1)));
            Assert.Equal(Id, independentSnapshot[0].CampaignId);
            Assert.Equal(Id, registry.Export()[0].CampaignId);
            registry.Associate(Hash(2), Id);
            Assert.Single(snapshot);
            Assert.Single(independentSnapshot);
            Assert.Equal(OtherId, snapshot[0].CampaignId);
            Assert.Equal(2, registry.Count);
        }

        [Fact]
        public void CapacityFailuresLeaveAllAliasesUnchangedAndKnownOperationsStillWork()
        {
            var entries = Enumerable.Range(0, CampaignSaveIdentities.MaxAliases)
                .Select(i => new CampaignSaveAlias(Hash(i), Id)).ToArray();
            var registry = new CampaignSaveIdentities(entries);
            var before = registry.Export();
            Assert.Equal(8192, registry.Count);
            Assert.Throws<InvalidOperationException>(() => registry.Resolve(Hash(8192)));
            Assert.Throws<InvalidOperationException>(() => registry.Associate(Hash(8192), Id));
            Assert.Equal(before.ToArray(), registry.Export().ToArray());
            Assert.Equal(Id, registry.Resolve(Hash(0)));
            registry.Associate(Hash(0), Id);
            Assert.Throws<ArgumentException>(() => new CampaignSaveIdentities(
                entries.Concat(new[] { new CampaignSaveAlias(Hash(8192), Id) })));
            Assert.Equal(8192, registry.Count);
        }

        [Fact]
        public void ResolveAndAssociateCanFillLastSlotWithoutEvictingLineage()
        {
            var entries = Enumerable.Range(0, CampaignSaveIdentities.MaxAliases - 1)
                .Select(i => new CampaignSaveAlias(Hash(i), Id)).ToArray();
            var resolved = new CampaignSaveIdentities(entries);
            Assert.NotEqual(Id, resolved.Resolve(Hash(8191)));
            Assert.Equal(8192, resolved.Count);
            var associated = new CampaignSaveIdentities(entries);
            associated.Associate(Hash(8191), Id);
            Assert.Equal(Id, associated.Resolve(Hash(8191)));
            Assert.Equal(8192, associated.Count);
        }
    }
}
