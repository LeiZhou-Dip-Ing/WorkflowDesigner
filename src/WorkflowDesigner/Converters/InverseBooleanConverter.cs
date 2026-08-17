using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WorkflowCore.WpfDemo.Converters;

public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool boolean ? !boolean : DependencyProperty.UnsetValue;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool boolean ? !boolean : DependencyProperty.UnsetValue;
}
