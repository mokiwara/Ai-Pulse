using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace AIUsageHub;

public sealed class UiKit
{
    public bool Dark { get; }
    public Brush Background { get; }
    public Brush Card { get; }
    public Brush Foreground { get; }
    public Brush Muted { get; }
    public Brush Border { get; }
    public Brush Accent { get; } = new LinearGradientBrush(Color.FromRgb(60, 143, 255), Color.FromRgb(38, 99, 242), 90);
    public Brush Track { get; }

    public UiKit(string theme)
    {
        var systemLight = true;
        try { using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"); systemLight = (int?)key?.GetValue("AppsUseLightTheme") != 0; }
        catch { }
        Dark = theme == "Dark" || theme == "System" && !systemLight;
        Background = Dark ? new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#142B4B"), (Color)ColorConverter.ConvertFromString("#091321"), 70) : Solid("#F5F7FB");
        Card = Solid(Dark ? "#17273D" : "#FFFFFF");
        Foreground = Solid(Dark ? "#F6F8FB" : "#1B2433");
        Muted = Solid(Dark ? "#A1B6D3" : "#647084");
        Border = Solid(Dark ? "#2A3D57" : "#E4E9F1");
        Track = Solid(Dark ? "#2A3C55" : "#E8EDF5");
    }

    public static FrameworkElement BrandMark(double scale = 1)
    {
        var bars = new StackPanel { Orientation = Orientation.Horizontal, Height = 20 * scale, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (height, color) in new[] { (13d, "#889BEE"), (20d, "#59CDC7"), (10d, "#AF8ACA") })
            bars.Children.Add(new Border { Width = 4 * scale, Height = height * scale, CornerRadius = new CornerRadius(2 * scale), Background = Solid(color), Margin = new Thickness(0,0,2 * scale,0), VerticalAlignment = VerticalAlignment.Bottom });
        return bars;
    }

    public static Brush Solid(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

    public TextBlock Text(string value, double size = 14, bool bold = false, Brush? color = null) => new()
    {
        Text = value, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        Foreground = color ?? Foreground, TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center
    };

    public Border Panel(UIElement child, Thickness? padding = null) => new()
    {
        Background = Card, BorderBrush = Border, BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(16), Padding = padding ?? new Thickness(18), Child = child,
        Margin = new Thickness(0, 0, 0, 12)
    };

    public Button Button(string label, Action click, bool primary = false)
    {
        var button = new Button
        {
            Content = label, Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 8, 0),
            Background = primary ? Accent : Card, Foreground = primary ? Brushes.White : Foreground,
            BorderBrush = primary ? Accent : Border, BorderThickness = new Thickness(1),
            FontSize = 13, FontWeight = FontWeights.SemiBold, Cursor = System.Windows.Input.Cursors.Hand
        };
        button.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
<ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button">
 <Border x:Name="surface" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="8" Padding="{TemplateBinding Padding}">
  <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalAlignment="Center"/>
 </Border>
 <ControlTemplate.Triggers>
  <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="surface" Property="Opacity" Value="0.78"/></Trigger>
  <Trigger Property="IsPressed" Value="True"><Setter TargetName="surface" Property="Opacity" Value="0.55"/></Trigger>
  <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.4"/></Trigger>
  <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="surface" Property="BorderBrush" Value="#72B6FF"/></Trigger>
 </ControlTemplate.Triggers>
</ControlTemplate>
""");
        button.Click += (_, _) => click();
        return button;
    }

    public void InstallResources(FrameworkElement element)
    {
        element.Resources["ControlSurface"] = Card;
        element.Resources["ControlText"] = Foreground;
        element.Resources["ControlBorder"] = Border;
        var resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse("""
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
 <Style TargetType="CheckBox">
  <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="CheckBox">
   <Grid Margin="0,6"><Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="48"/></Grid.ColumnDefinitions>
    <ContentPresenter VerticalAlignment="Center" Margin="0,0,16,0"/>
    <Border x:Name="track" Grid.Column="1" Width="42" Height="24" CornerRadius="12" Background="#526079">
     <Ellipse x:Name="thumb" Width="18" Height="18" Fill="White" HorizontalAlignment="Left" Margin="3"/>
    </Border>
   </Grid>
   <ControlTemplate.Triggers>
    <Trigger Property="IsChecked" Value="True"><Setter TargetName="track" Property="Background" Value="#2975FF"/><Setter TargetName="thumb" Property="HorizontalAlignment" Value="Right"/></Trigger>
    <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="track" Property="BorderBrush" Value="#A9D5FF"/><Setter TargetName="track" Property="BorderThickness" Value="2"/></Trigger>
    <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.4"/></Trigger>
   </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType="ComboBox">
  <Setter Property="Foreground" Value="{DynamicResource ControlText}"/><Setter Property="Background" Value="{DynamicResource ControlSurface}"/>
  <Setter Property="FontSize" Value="13"/><Setter Property="MinHeight" Value="36"/>
  <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBox">
   <Grid>
    <ToggleButton Focusable="False" IsChecked="{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}">
     <ToggleButton.Template><ControlTemplate TargetType="ToggleButton"><Border Background="{DynamicResource ControlSurface}" BorderBrush="{DynamicResource ControlBorder}" BorderThickness="1" CornerRadius="7"><TextBlock Text="&#x2304;" Foreground="{DynamicResource ControlText}" HorizontalAlignment="Right" VerticalAlignment="Center" Margin="0,0,12,0"/></Border></ControlTemplate></ToggleButton.Template>
    </ToggleButton>
    <ContentPresenter Content="{TemplateBinding SelectionBoxItem}" Margin="12,8,34,8" IsHitTestVisible="False"/>
    <Popup x:Name="PART_Popup" IsOpen="{TemplateBinding IsDropDownOpen}" Placement="Bottom" AllowsTransparency="True" Focusable="False">
     <Border Background="{DynamicResource ControlSurface}" BorderBrush="{DynamicResource ControlBorder}" BorderThickness="1" Padding="4" MinWidth="{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}"><ScrollViewer MaxHeight="260"><ItemsPresenter/></ScrollViewer></Border>
    </Popup>
   </Grid>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType="ComboBoxItem"><Setter Property="Foreground" Value="{DynamicResource ControlText}"/><Setter Property="Padding" Value="10,7"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBoxItem"><Border x:Name="item" Background="Transparent" Padding="{TemplateBinding Padding}"><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property="IsHighlighted" Value="True"><Setter TargetName="item" Property="Background" Value="#384F72"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
 <Style TargetType="ProgressBar"><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ProgressBar"><Grid x:Name="PART_Track"><Border Background="{TemplateBinding Background}" CornerRadius="4"/><Border x:Name="PART_Indicator" HorizontalAlignment="Left" Background="{TemplateBinding Foreground}" CornerRadius="4"/></Grid></ControlTemplate></Setter.Value></Setter></Style>
 <Style TargetType="ScrollBar"><Setter Property="Width" Value="8"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ScrollBar"><Track x:Name="PART_Track" IsDirectionReversed="True"><Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType="Thumb"><Border Background="#50627C" CornerRadius="4" Margin="2"/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb></Track></ControlTemplate></Setter.Value></Setter></Style>
</ResourceDictionary>
""");
        element.Resources.MergedDictionaries.Clear();
        element.Resources.MergedDictionaries.Add(resources);
    }

    public ProgressBar Progress(double value) => new()
    {
        Minimum = 0, Maximum = 100, Value = value, Height = 8,
        Foreground = value >= 95 ? Solid("#F16D6D") : value >= 85 ? Solid("#EBA54C") : Accent,
        Background = Track, BorderThickness = new Thickness(0), Margin = new Thickness(0, 5, 0, 5)
    };
}
