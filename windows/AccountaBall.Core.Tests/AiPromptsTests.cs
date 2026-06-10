using AccountaBall.Core.Util;
using Xunit;

namespace AccountaBall.Core.Tests;

/// These guard the load-bearing contract — if someone paraphrases a prompt and
/// drops the JSON schema or the bias instruction, the build fails.
public class AiPromptsTests
{
    [Fact]
    public void ClassifySystem_KeepsSchemaAndBias()
    {
        Assert.Contains("\"result\": \"TASK:N\" | \"AMBIGUOUS\" | \"OFFTASK\" | \"DONE:N\"", AiPrompts.ClassifySystem);
        Assert.Contains("TASK:N — the screen clearly matches the Nth task (0-based).", AiPrompts.ClassifySystem);
        Assert.Contains("prefer TASK:N or AMBIGUOUS over OFFTASK", AiPrompts.ClassifySystem);
        Assert.Contains("a false \"get back to\nwork\" costs more trust than a missed slack-off", AiPrompts.ClassifySystem);
    }

    [Fact]
    public void ExcuseSystem_KeepsVerdictSchema()
    {
        Assert.Contains("\"verdict\": \"JUSTIFIED\" | \"NOT_JUSTIFIED\"", AiPrompts.ExcuseSystem);
        Assert.Contains("Interpret \"helping a task\" BROADLY.", AiPrompts.ExcuseSystem);
    }

    [Fact]
    public void FreeBallSystem_KeepsJsonShapeAndGuards()
    {
        Assert.Contains("\"narrative\":\"<2-4 sentences, what they spent the session on>\"", AiPrompts.FreeBallSystem);
        Assert.Contains("never invent", AiPrompts.FreeBallSystem);
        Assert.Contains("Output JSON only, no prose.", AiPrompts.FreeBallSystem);
    }

    [Fact]
    public void SessionAndMatchAndSummarize_KeepSchemas()
    {
        Assert.Contains("\"tasks\": [{\"title\": \"<exact task title>\"", AiPrompts.SessionSystem);
        Assert.Contains("\"id\": \"<candidate id>\" | null", AiPrompts.MatchSystem);
        Assert.Contains("\"summary\": \"<one sentence>\"", AiPrompts.SummarizeSystem);
    }
}
