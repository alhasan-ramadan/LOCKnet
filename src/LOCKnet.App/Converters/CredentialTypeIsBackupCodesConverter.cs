using Avalonia.Data.Converters;
using LOCKnet.Core.DataAbstractions;
using System.Globalization;

namespace LOCKnet.App.Converters;

/// <summary>
/// Konvertiert einen <see cref="CredentialType"/>-Wert in <c>true</c>, wenn er <see cref="CredentialType.BackupCodes"/> ist.
/// </summary>
public sealed class CredentialTypeIsBackupCodesConverter : IValueConverter
{
	/// <inheritdoc/>
	public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
		=> value is CredentialType t && t == CredentialType.BackupCodes;

	/// <inheritdoc/>
	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
		=> value is true ? CredentialType.BackupCodes : CredentialType.Password;
}
