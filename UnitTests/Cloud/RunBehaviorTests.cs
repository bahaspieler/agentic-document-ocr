using OcrAgent.Runs;

namespace UnitTests.Cloud;

[TestClass]
public sealed class RunBehaviorTests
{
    [TestMethod]
    public void NewId_ProducesTimestampedVersion7Identifier()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var id = RunId.NewId();
        var timestamp = RunId.GetTimestamp(id);

        Assert.AreEqual('7', id[14]);
        Assert.IsGreaterThanOrEqualTo(before, timestamp);
        Assert.IsLessThanOrEqualTo(DateTimeOffset.UtcNow.AddSeconds(1), timestamp);
        StringAssert.EndsWith(RunId.ToRowKey(id), $"_{id}");
    }

    [TestMethod]
    public void TryGetTimestamp_RejectsNonVersion7Identifiers()
    {
        var accepted = RunId.TryGetTimestamp(Guid.NewGuid().ToString(), out _);

        Assert.IsFalse(accepted);
    }

    [TestMethod]
    public void RunScope_NestedHandlesRestoreAmbientValues()
    {
        Assert.IsFalse(RunScope.IsInsideJob);
        Assert.IsNull(RunScope.ConversationId);

        using (RunScope.EnterConversation("conversation-1"))
        using (RunScope.EnterSource("chat"))
        using (RunScope.Enter())
        {
            Assert.IsTrue(RunScope.IsInsideJob);
            Assert.AreEqual("conversation-1", RunScope.ConversationId);
            Assert.AreEqual("chat", RunScope.Source);

            using (RunScope.Enter())
                Assert.IsTrue(RunScope.IsInsideJob);
        }

        Assert.IsFalse(RunScope.IsInsideJob);
        Assert.IsNull(RunScope.ConversationId);
    }
}
