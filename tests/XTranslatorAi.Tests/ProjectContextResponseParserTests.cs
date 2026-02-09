using XTranslatorAi.Core.Text.ProjectContext;
using Xunit;

namespace XTranslatorAi.Tests;

public class ProjectContextResponseParserTests
{
    [Fact]
    public void TryParseContext_ReturnsTrue_ForSimpleJson()
    {
        var ok = ProjectContextResponseParser.TryParseContext("{\"context\":\"hello\"}", out var ctx);
        Assert.True(ok);
        Assert.Equal("hello", ctx);
    }

    [Fact]
    public void TryParseContext_ReturnsTrue_ForJsonInsideCodeFenceWithPreface()
    {
        var raw = "Here is the JSON:\n```json\n{\"context\":\"hello\"}\n```";
        var ok = ProjectContextResponseParser.TryParseContext(raw, out var ctx);
        Assert.True(ok);
        Assert.Equal("hello", ctx);
    }

    [Fact]
    public void TryParseContext_ReturnsTrue_ForNestedContext()
    {
        var raw = "{\"result\":{\"context\":\"nested\"}}";
        var ok = ProjectContextResponseParser.TryParseContext(raw, out var ctx);
        Assert.True(ok);
        Assert.Equal("nested", ctx);
    }

    [Fact]
    public void TryParseContext_ReturnsTrue_ForCaseInsensitiveKey()
    {
        var raw = "{\"Context\":\"caps\"}";
        var ok = ProjectContextResponseParser.TryParseContext(raw, out var ctx);
        Assert.True(ok);
        Assert.Equal("caps", ctx);
    }

    [Fact]
    public void TryParseContext_ReturnsTrue_ForArrayRoot()
    {
        var raw = "[{\"context\":\"array\"}]";
        var ok = ProjectContextResponseParser.TryParseContext(raw, out var ctx);
        Assert.True(ok);
        Assert.Equal("array", ctx);
    }

    [Fact]
    public void TryParseContext_ReturnsFalse_WhenMissing()
    {
        var ok = ProjectContextResponseParser.TryParseContext("{\"foo\":\"bar\"}", out var ctx);
        Assert.False(ok);
        Assert.Equal("", ctx);
    }
}

