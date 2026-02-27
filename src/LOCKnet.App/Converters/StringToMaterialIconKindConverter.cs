using Avalonia.Data.Converters;
using System.Globalization;

namespace LOCKnet.App.Converters;

public sealed class StringToMaterialIconKindConverter : IValueConverter
{
	private static readonly Type? MaterialIconKindType =
		Type.GetType("Material.Icons.MaterialIconKind, Material.Icons") ??
		Type.GetType("Material.Icons.MaterialIconKind, Material.Icons.Avalonia");

	public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		if (MaterialIconKindType is null)
		{
			return value?.ToString() ?? string.Empty;
		}

		if (value is string text && Enum.TryParse(MaterialIconKindType, text, out var parsedKind))
		{
			return parsedKind;
		}

		return Enum.GetValues(MaterialIconKindType).GetValue(0) ?? value?.ToString() ?? string.Empty;
	}

	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		return value?.ToString() ?? string.Empty;
	}
}
