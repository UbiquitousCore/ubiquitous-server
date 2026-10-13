namespace Ubiquitous.Modules;

public interface IServerModule
{
    string Name { get; }

    void Register(IModuleHost host);
}
