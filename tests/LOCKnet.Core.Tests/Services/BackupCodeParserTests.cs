using LOCKnet.Core.Services;

namespace LOCKnet.Core.Tests.Services;

public sealed class BackupCodeParserTests
{
	[Fact]
	public void Parse_SupportsNewLinesSemicolonsAndCommas()
	{
		var result = BackupCodeParser.Parse("abc\n def ; ghi, jkl");

		Assert.Equal(4, result.Count);
		Assert.Contains("abc", result);
		Assert.Contains("def", result);
		Assert.Contains("ghi", result);
		Assert.Contains("jkl", result);
	}

	[Fact]
	public void Parse_RemovesDuplicatesAndEmptyValues()
	{
		var result = BackupCodeParser.Parse("  a1  \n\nA1\n b2 ");

		Assert.Equal(2, result.Count);
		Assert.Equal("a1", result[0]);
		Assert.Equal("b2", result[1]);
	}
}
