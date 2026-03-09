using Avalonia.Controls;
using Avalonia.Media;
using LOCKnet.App;
using LOCKnet.App.Converters;
using LOCKnet.App.ViewModels;
using LOCKnet.Core.DataAbstractions;
using System.Globalization;

namespace LOCKnet.Data.Tests;

public sealed class AppConvertersAndLocatorTests
{
	[Fact]
	public void CredentialTypeIsApiKeyConverter_ConvertsBothDirections()
	{
		var converter = new CredentialTypeIsApiKeyConverter();

		Assert.True((bool)converter.Convert(CredentialType.ApiKey, typeof(bool), null, CultureInfo.InvariantCulture));
		Assert.False((bool)converter.Convert(CredentialType.Password, typeof(bool), null, CultureInfo.InvariantCulture));
		Assert.Equal(CredentialType.ApiKey, converter.ConvertBack(true, typeof(CredentialType), null, CultureInfo.InvariantCulture));
		Assert.Equal(CredentialType.Password, converter.ConvertBack(false, typeof(CredentialType), null, CultureInfo.InvariantCulture));
	}

	[Fact]
	public void CredentialTypeIsBackupCodesConverter_ConvertsBothDirections()
	{
		var converter = new CredentialTypeIsBackupCodesConverter();

		Assert.True((bool)converter.Convert(CredentialType.BackupCodes, typeof(bool), null, CultureInfo.InvariantCulture));
		Assert.False((bool)converter.Convert(CredentialType.Password, typeof(bool), null, CultureInfo.InvariantCulture));
		Assert.Equal(CredentialType.BackupCodes, converter.ConvertBack(true, typeof(CredentialType), null, CultureInfo.InvariantCulture));
		Assert.Equal(CredentialType.Password, converter.ConvertBack(false, typeof(CredentialType), null, CultureInfo.InvariantCulture));
	}

	[Fact]
	public void IconSelectedBrushConverter_ReturnsSelectedAndUnselectedBrushes()
	{
		var converter = new IconSelectedBrushConverter();

		var selected = (IBrush)converter.Convert("Key", typeof(IBrush), "Key", CultureInfo.InvariantCulture);
		var unselected = (IBrush)converter.Convert("Key", typeof(IBrush), "Lock", CultureInfo.InvariantCulture);

		Assert.NotEqual(Brushes.Transparent, selected);
		Assert.Equal(Brushes.Transparent, unselected);
		Assert.Throws<NotSupportedException>(() => converter.ConvertBack(null, typeof(string), null, CultureInfo.InvariantCulture));
	}

	[Fact]
	public void StringToMaterialIconKindConverter_HandlesKnownUnknownAndNullValues()
	{
		var converter = new StringToMaterialIconKindConverter();

		var known = converter.Convert("Key", typeof(object), null, CultureInfo.InvariantCulture);
		var unknown = converter.Convert("DefinitelyNotARealIcon", typeof(object), null, CultureInfo.InvariantCulture);
		var fallback = converter.Convert(null, typeof(object), null, CultureInfo.InvariantCulture);

		Assert.NotNull(known);
		Assert.NotNull(unknown);
		Assert.NotNull(fallback);
		Assert.Equal("Key", converter.ConvertBack(known, typeof(string), null, CultureInfo.InvariantCulture).ToString());
	}

	[Fact]
	public void ViewLocator_MatchBuildAndFallback_WorkAsExpected()
	{
		using var scope = new AppServicesScope(ensureAvalonia: true);
		var locator = new ViewLocator();

		Assert.False(locator.Match(null));
		Assert.True(locator.Match(new CredentialListDesignViewModel()));
		Assert.Null(locator.Build(null));

		var known = locator.Build(new TutorialViewModel());
		Assert.NotNull(known);
		Assert.Equal("TutorialView", known!.GetType().Name);

		var fallback = locator.Build(new MissingScreenViewModel());
		var text = Assert.IsType<TextBlock>(fallback);
		Assert.Contains("Not Found", text.Text);
	}

	private sealed class MissingScreenViewModel : ViewModelBase
	{
	}
}
