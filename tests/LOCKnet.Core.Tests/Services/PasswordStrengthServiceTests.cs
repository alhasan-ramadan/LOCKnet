using LOCKnet.Core.Services;

namespace LOCKnet.Core.Tests.Services;

public sealed class PasswordStrengthServiceTests
{
	private readonly PasswordStrengthService _sut = new();

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	public void Evaluate_WithNullOrEmpty_ReturnsVeryWeak(string? password)
	{
		var result = _sut.Evaluate(password!);

		Assert.Equal(0, result.Score);
		Assert.Equal("Sehr schwach", result.Label);
		Assert.Equal("#FF4757", result.Color);
	}

	[Fact]
	public void Evaluate_WithShortLowercaseOnly_ReturnsVeryWeak()
	{
		var result = _sut.Evaluate("abc");

		Assert.Equal(0, result.Score);
		Assert.Equal("Sehr schwach", result.Label);
		Assert.Equal("#FF4757", result.Color);
	}

	[Fact]
	public void Evaluate_WithLengthAndTwoClasses_ReturnsWeak()
	{
		var result = _sut.Evaluate("abcd1234");

		Assert.Equal(1, result.Score);
		Assert.Equal("Schwach", result.Label);
		Assert.Equal("#FF6B35", result.Color);
	}

	[Fact]
	public void Evaluate_WithUpperLowerDigitsAndMinLength_ReturnsMedium()
	{
		var result = _sut.Evaluate("Abcd1234");

		Assert.Equal(2, result.Score);
		Assert.Equal("Mittel", result.Label);
		Assert.Equal("#FFB347", result.Color);
	}

	[Fact]
	public void Evaluate_WithAllCharacterClasses_ReturnsStrong()
	{
		var result = _sut.Evaluate("Abcd1234!");

		Assert.Equal(3, result.Score);
		Assert.Equal("Stark", result.Label);
		Assert.Equal("#2ED573B3", result.Color);
	}

	[Fact]
	public void Evaluate_WithLongAllCharacterClasses_ReturnsVeryStrong()
	{
		var result = _sut.Evaluate("Abcd1234!xyz");

		Assert.Equal(4, result.Score);
		Assert.Equal("Sehr stark", result.Label);
		Assert.Equal("#2ED573", result.Color);
	}

	[Fact]
	public void Evaluate_ReturnsPasswordStrengthRecordWithProvidedValues()
	{
		var strength = new PasswordStrength(2, "Mittel", "#FFB347");

		Assert.Equal(2, strength.Score);
		Assert.Equal("Mittel", strength.Label);
		Assert.Equal("#FFB347", strength.Color);
	}
}
