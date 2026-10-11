namespace Skanyxx.Host;

/// <summary>
/// What this process is (D200, D201). <see cref="Server"/> is the full workspace: modules, databases, sign-in.
/// <see cref="Remote"/> is the desktop launcher: it opens nothing but a redirect to the company's Skanyxx.
/// Read by the Host before anything else loads; modules never see it.
/// </summary>
public enum HostMode
{
    Server,
    Remote
}
