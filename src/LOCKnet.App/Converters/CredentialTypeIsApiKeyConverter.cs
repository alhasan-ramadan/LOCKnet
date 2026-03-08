using Avalonia.Data.Converters;
using LOCKnet.Core.DataAbstractions;
using System.Globalization;

namespace LOCKnet.App.Converters;

/// <summary>
/// Konvertiert einen <see cref="CredentialType"/>-Wert in <c>true</c>, wenn er <see cref="CredentialType.ApiKey"/> ist.
/// Wird in AXAML verwendet, um API-Schlüssel-Badges und bedingte Sichtbarkeit zu steuern.
/// </summary>
public sealed class CredentialTypeIsApiKeyConverter : IValueConverter
{
	/// <inheritdoc/>
	public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
		=> value is CredentialType t && t == CredentialType.ApiKey;

	/// <inheritdoc/>
	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
		=> value is true ? CredentialType.ApiKey : CredentialType.Password;
}
