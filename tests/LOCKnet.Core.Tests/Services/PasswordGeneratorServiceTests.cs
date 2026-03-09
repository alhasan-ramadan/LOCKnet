using LOCKnet.Core.Services;

namespace LOCKnet.Core.Tests.Services;

public sealed class PasswordGeneratorServiceTests
{
	private readonly PasswordGeneratorService _sut = new();

	[Fact]
	public void Generate_WithNullOptions_Throws()
	{
		Assert.Throws<ArgumentNullException>(() => _sut.Generate(null!));
	}

	[Fact]
	public void Generate_WithDefaultOptions_UsesConfiguredDefaults()
	{
		var options = new PasswordGeneratorOptions();

		var password = _sut.Generate(options);

		Assert.Equal(16, options.Length);
		Assert.True(options.UseUppercase);
		Assert.True(options.UseLowercase);
		Assert.True(options.UseDigits);
		Assert.True(options.UseSpecial);
		Assert.Equal(options.Length, password.Length);
	}

	[Theory]
	[InlineData(1)]
	[InlineData(8)]
	[InlineData(32)]
	[InlineData(64)]
	public void Generate_WithPositiveLength_UsesExactLength(int length)
	{
		var password = _sut.Generate(new PasswordGeneratorOptions
		{
			Length = length,
			UseUppercase = true,
			UseLowercase = true,
			UseDigits = true,
			UseSpecial = true,
		});

		Assert.Equal(length, password.Length);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	[InlineData(-99)]
	public void Generate_WithNonPositiveLength_FallsBackToDefaultLength(int length)
	{
		var password = _sut.Generate(new PasswordGeneratorOptions
		{
			Length = length,
			UseUppercase = true,
			UseLowercase = true,
			UseDigits = true,
			UseSpecial = true,
		});

		Assert.Equal(16, password.Length);
	}

	[Fact]
	public void Generate_WhenNoCharacterGroupSelected_FallsBackToLowercase()
	{
		var password = _sut.Generate(new PasswordGeneratorOptions
		{
			Length = 64,
			UseUppercase = false,
			UseLowercase = false,
			UseDigits = false,
			UseSpecial = false,
		});

		Assert.All(password, c => Assert.InRange(c, 'a', 'z'));
	}

	[Fact]
	public void Generate_WhenOnlyDigitsSelected_UsesDigitsOnly()
	{
		var password = _sut.Generate(new PasswordGeneratorOptions
		{
			Length = 80,
			UseUppercase = false,
			UseLowercase = false,
			UseDigits = true,
			UseSpecial = false,
		});

		Assert.All(password, c => Assert.True(char.IsDigit(c), $"Expected digit but got '{c}'."));
	}

	[Fact]
	public void Generate_WhenOnlySpecialSelected_UsesSpecialSetOnly()
	{
		const string specialChars = "!@#$%^&*()-_=+[]{}|;:,.<>?/";

		var password = _sut.Generate(new PasswordGeneratorOptions
		{
			Length = 80,
			UseUppercase = false,
			UseLowercase = false,
			UseDigits = false,
			UseSpecial = true,
		});

		Assert.All(password, c => Assert.Contains(c, specialChars));
	}
}
