using L4D2ModManager.Core;

namespace L4D2ModManager.Tests;

[TestClass]
public sealed class SyncPlannerTests
{
    [TestMethod]
    public void CreatesIncrementalPlanAndRemovesOnlyManagedFiles()
    {
        var remote = new RemoteFile("new.vpk", 10, new string('a', 64), "https://github.com/o/r/releases/download/v1/new.vpk");
        var manifest = new RemoteManifest(1, "1.0.0", 550, DateTimeOffset.UtcNow,
            [new("1", "New", null, null, "https://steamcommunity.com/", null, 1, [remote])]);
        var state = new LocalState(1, "0.9.0",
            [new("old.vpk", 5, new string('b', 64), "2"), new("new.vpk", 8, new string('c', 64), "1")]);

        var plan = SyncPlanner.Create(manifest, state);

        Assert.HasCount(1, plan.Downloads);
        Assert.AreEqual("new.vpk", plan.Downloads[0].File.Filename);
        Assert.HasCount(1, plan.Removals);
        Assert.AreEqual("old.vpk", plan.Removals[0].Filename);
    }

    [TestMethod]
    public void SecondPlanIsIdempotent()
    {
        var file = new RemoteFile("same.vpk", 10, new string('a', 64), "https://github.com/o/r/releases/download/v1/same.vpk");
        var manifest = new RemoteManifest(1, "1.0.0", 550, DateTimeOffset.UtcNow,
            [new("1", "Same", null, null, "https://steamcommunity.com/", null, 1, [file])]);
        var state = new LocalState(1, "1.0.0", [new("same.vpk", 10, new string('a', 64), "1")]);

        var plan = SyncPlanner.Create(manifest, state);

        Assert.IsFalse(plan.HasChanges);
        Assert.AreEqual(1, plan.UnchangedCount);
    }

    [TestMethod]
    [DataRow("../evil.vpk")]
    [DataRow("folder/evil.vpk")]
    [DataRow("not-a-vpk.txt")]
    public void RejectsUnsafeManifestPaths(string filename)
    {
        Assert.ThrowsExactly<InvalidDataException>(() => SafePath.ManagedFile(Path.GetTempPath(), filename));
    }
}
