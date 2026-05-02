using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui;

namespace Mockingbird.Services.Navigation;

/// <summary>
/// Thin <see cref="IPageService"/> adapter that delegates to the host's
/// <see cref="IServiceProvider"/>. wpfui's <c>NavigationView.SetPageService</c>
/// uses this to resolve page instances per ADR 0009 — every page is a transient
/// DI registration and gets its dependencies (logger, services) by constructor
/// injection.
/// </summary>
public sealed class PageService : IPageService
{
    private readonly IServiceProvider _provider;

    public PageService(IServiceProvider provider)
    {
        _provider = provider;
    }

    public T? GetPage<T>() where T : class
    {
        return _provider.GetRequiredService(typeof(T)) as T;
    }

    public System.Windows.FrameworkElement GetPage(Type pageType)
    {
        if (!typeof(System.Windows.FrameworkElement).IsAssignableFrom(pageType))
            throw new InvalidOperationException(
                $"PageService can only resolve FrameworkElement pages; got {pageType}.");
        return (System.Windows.FrameworkElement)_provider.GetRequiredService(pageType);
    }
}
