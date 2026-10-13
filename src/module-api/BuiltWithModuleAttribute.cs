namespace Ubiquitous.Modules;

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class BuiltWithModuleAttribute(string assemblyName) : Attribute
{
    public string AssemblyName { get; } = assemblyName;
}
