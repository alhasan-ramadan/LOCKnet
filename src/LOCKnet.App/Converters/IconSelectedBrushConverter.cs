using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;

namespace LOCKnet.App.Converters;

public sealed class IconSelectedBrushConverter : IValueConverter
{
	private static readonly IBrush SelectedBrush = new SolidColorBrush(Color.Parse("#6C63FF"));
	private static readonly IBrush UnselectedBrush = Brushes.Transparent;

	public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		var selectedKey = value as string;
		var currentKey = parameter as string;
		return string.Equals(selectedKey, currentKey, StringComparison.Ordinal) ? SelectedBrush : UnselectedBrush;
	}

	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		throw new NotSupportedException();
	}
}
