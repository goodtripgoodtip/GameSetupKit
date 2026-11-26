namespace GameInstaller.Sample;

internal sealed class SamplePlatform(string root, InstallerDefinition definition, InstallerPackage? package, bool useWindows) : IInstallerPlatform
{
    private readonly WindowsInstallerPlatform? windows = useWindows ? new WindowsInstallerPlatform(definition, package: package) : null;
    private readonly HashSet<InstallerFirewallRule> simulatedRules = [];

    public string? FindInstallation(InstallerDefinition product) => windows?.FindInstallation(product);

    public Task RegisterAsync(InstallerRegistration registration, CancellationToken token) =>
        windows?.RegisterAsync(registration, token) ?? Task.CompletedTask;

    public void RemoveShortcuts(InstallerRegistration registration) => windows?.RemoveShortcuts(registration);

    public Task RemoveRegistrationAsync(InstallerRegistration registration, CancellationToken token) =>
        windows?.RemoveRegistrationAsync(registration, token) ?? Task.CompletedTask;

    public IReadOnlyList<InstallerFirewallRule> FindPolicyChanges(IReadOnlyList<InstallerFirewallRule> rules, InstallerNetworkChoice choice)
    {
        File.AppendAllLines(Path.Combine(root, "firewall-preview.log"), rules.Select(rule => $"Review {choice}: {rule.Description}"));
        return rules.Where(rule => simulatedRules.Contains(rule) != (choice == InstallerNetworkChoice.BlockOutbound)).ToArray();
    }

    public Task ApplyPolicyAsync(InstallerRegistration registration, IReadOnlyList<InstallerFirewallRule> rules,
        InstallerNetworkChoice choice, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (choice == InstallerNetworkChoice.BlockOutbound)
            simulatedRules.UnionWith(rules);
        else
            simulatedRules.ExceptWith(rules);
        File.AppendAllLines(Path.Combine(root, "firewall-preview.log"), rules.Select(rule => $"Simulated {choice}: {rule.Description}"));
        return Task.CompletedTask;
    }
}
