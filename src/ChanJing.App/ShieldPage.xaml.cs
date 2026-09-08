using ChanJing.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChanJing_App;

/// <summary>
/// 屏蔽管理页：分类名单勾选 + 自定义域名 + 应用/撤销 hosts 屏蔽。
/// </summary>
public sealed partial class ShieldPage : Page
{
    private readonly BlocklistService _blocklist = AppServices.Blocklist;

    public ShieldPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        CategoryPanel.Children.Clear();
        var enabled = _blocklist.GetEnabledCategories();

        foreach (var category in BlocklistService.DefaultCategories.Keys)
        {
            var checkBox = new CheckBox
            {
                Content = category,
                IsChecked = enabled.Contains(category),
                Tag = category,
                FontSize = 14
            };
            checkBox.Checked += OnCategoryChanged;
            checkBox.Unchecked += OnCategoryChanged;
            CategoryPanel.Children.Add(checkBox);
        }

        RefreshCustomDomains();
        RefreshStatus();
    }

    private void OnCategoryChanged(object sender, RoutedEventArgs e)
    {
        var enabled = CategoryPanel.Children
            .OfType<CheckBox>()
            .Where(c => c.IsChecked == true)
            .Select(c => (string)c.Tag);
        _blocklist.SetEnabledCategories(enabled);
        RefreshStatus();
    }

    private void AddDomain_Click(object sender, RoutedEventArgs e)
    {
        var input = DomainBox.Text;
        if (string.IsNullOrWhiteSpace(input)) return;

        var list = _blocklist.GetCustomDomains().ToList();
        list.Add(input);
        _blocklist.SetCustomDomains(list);

        DomainBox.Text = string.Empty;
        RefreshCustomDomains();
        RefreshStatus();
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        _blocklist.Apply();
        RefreshStatus();
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        _blocklist.Remove();
        RefreshStatus();
    }

    private void RefreshCustomDomains()
    {
        DomainList.ItemsSource = _blocklist.GetCustomDomains().ToList();
    }

    private void RefreshStatus()
    {
        var active = _blocklist.GetActiveDomains();
        StatusText.Text = _blocklist.IsApplied()
            ? $"屏蔽已生效（{active.Count} 个域名）"
            : $"未生效（已选 {active.Count} 个域名，点击「应用屏蔽」）";
    }
}
