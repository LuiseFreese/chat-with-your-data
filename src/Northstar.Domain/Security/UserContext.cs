namespace Northstar.Domain.Security;

/// <summary>
/// The identity a query is executed for. In Connected Mode this comes from the Entra sign-in and region
/// visibility is enforced by the semantic model (RLS) – <see cref="AllowedRegions"/> is informational only.
/// In Local Demo Mode it is a clearly labelled simulated persona and the local provider enforces
/// <see cref="AllowedRegions"/> itself.
/// </summary>
public sealed record UserContext(
    string UserPrincipalName,
    string DisplayName,
    string Role,
    bool IsSimulated,
    IReadOnlyList<string>? AllowedRegions)
{
    public bool HasAllRegions => AllowedRegions is null;
    public bool CanSee(string region) => AllowedRegions is null || AllowedRegions.Contains(region, StringComparer.OrdinalIgnoreCase);
    public string AccessDescription => AllowedRegions is null ? "all regions" : string.Join(", ", AllowedRegions);
}

/// <summary>Simulated personas for Local Demo Mode. They mirror the RLS roles documented for the Fabric model.</summary>
public static class DemoPersonas
{
    public static readonly UserContext Cfo = new("megan.bowen@northstar.example", "Megan Bowen", "CFO", true, null);
    public static readonly UserContext RegionalWest = new("adele.vance@northstar.example", "Adele Vance", "Regional Sales Manager West", true, ["West"]);
    public static readonly UserContext RegionalNorthSouth = new("alex.wilber@northstar.example", "Alex Wilber", "Regional Sales Manager North & South", true, ["North", "South"]);

    public static IReadOnlyList<UserContext> All { get; } = [Cfo, RegionalWest, RegionalNorthSouth];

    public static UserContext? Find(string upn) => All.FirstOrDefault(p => p.UserPrincipalName.Equals(upn, StringComparison.OrdinalIgnoreCase));
}
