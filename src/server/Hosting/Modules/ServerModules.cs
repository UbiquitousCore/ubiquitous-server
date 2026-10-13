using System.Reflection;
using Microsoft.Extensions.Logging;
using Ubiquitous.Modules;

namespace Ubiquitous.Server;

public sealed class ServerModules
{
    private readonly List<Func<DateTimeOffset, CancellationToken, Task>> _ticks = [];
    private readonly List<Func<CancellationToken, Task>> _shutdowns = [];
    private readonly ILogger _logger;

    public ServerModules(ILogger logger) => _logger = logger;

    // The server project names each module it was built with (see server.csproj).
    public static IReadOnlyList<IServerModule> BuiltIn() =>
        [.. typeof(ServerModules).Assembly.GetCustomAttributes<BuiltWithModuleAttribute>()
            .Select(attribute => Assembly.Load(attribute.AssemblyName))
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IServerModule).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .Select(type => (IServerModule)Activator.CreateInstance(type)!)];

    public void Register(IEnumerable<IServerModule> modules, Func<IServerModule, IModuleHost> hostFor)
    {
        foreach (var module in modules)
        {
            module.Register(hostFor(module));
            _logger.LogInformation("Module {Module} loaded", module.Name);
        }
    }

    internal void AddTick(Func<DateTimeOffset, CancellationToken, Task> tick)
    {
        lock (_ticks) _ticks.Add(tick);
    }

    internal void AddShutdown(Func<CancellationToken, Task> shutdown)
    {
        lock (_shutdowns) _shutdowns.Add(shutdown);
    }

    public async Task TickAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        Func<DateTimeOffset, CancellationToken, Task>[] ticks;
        lock (_ticks) ticks = [.. _ticks];
        foreach (var tick in ticks)
        {
            try { await tick(now, cancellationToken); }
            catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogError(ex, "Module tick failed"); }
        }
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        Func<CancellationToken, Task>[] shutdowns;
        lock (_shutdowns) shutdowns = [.. _shutdowns];
        foreach (var shutdown in shutdowns)
        {
            try { await shutdown(cancellationToken); }
            catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogError(ex, "Module shutdown failed"); }
        }
    }
}
