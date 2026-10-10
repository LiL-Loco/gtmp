using GTMP.Server.MasterApi.Configuration;
using GTMP.Server.MasterApi.Contracts;

namespace GTMP.Server.MasterApi.Tests;

/// <summary>
/// Covers <see cref="MasterApiSettings.Validate"/>.
/// </summary>
/// <remarks>
/// Every check here corresponds to a mistake that loads without raising, which
/// is exactly why it needs a test: the launcher cannot tell the difference
/// between "no news" and "news whose fields never bound".
/// </remarks>
public sealed class SettingsValidationTests
{
    [Fact]
    public void ACorrectConfigurationHasNoProblems()
    {
        var settings = new MasterApiSettings
        {
            Servers =
            {
                new ServerEntry { Port = 4499, ServerName = "Dev", IpPortAddress = "127.0.0.1:4499" }
            },
            VerifiedServers = { "127.0.0.1:4499" },
            News =
            {
                new ForumPost { Id = 1, Title = "Hello" }
            },
            Components =
            {
                new StatusComponent { Id = 1, Name = "Master", CreatedAt = new DateTime(2026, 1, 1) }
            }
        };

        Assert.Empty(settings.Validate());
    }

    [Fact]
    public void AnEmptyIpIsReportedWithTheFieldNameToUse()
    {
        var settings = new MasterApiSettings
        {
            Servers =
            {
                new ServerEntry { Port = 4499, ServerName = "Dev", IpPortAddress = string.Empty }
            }
        };

        var problem = Assert.Single(settings.Validate());

        Assert.Contains("\"ip\"", problem);
        Assert.Contains("IpPortAddress", problem);
    }

    [Fact]
    public void AVerifiedAddressWithoutAServerIsReported()
    {
        var settings = new MasterApiSettings
        {
            VerifiedServers = { "10.0.0.1:4499" }
        };

        var problem = Assert.Single(settings.Validate());

        Assert.Contains("VerifiedServers", problem);
        Assert.Contains("10.0.0.1:4499", problem);
    }

    [Fact]
    public void ANewsEntryWithoutThreadIdIsReported()
    {
        var settings = new MasterApiSettings
        {
            News =
            {
                new ForumPost { Id = 0, Title = "Hello" }
            }
        };

        var problem = Assert.Single(settings.Validate());

        Assert.Contains("thread_id", problem);
    }

    [Fact]
    public void AComponentWithoutCreateDateIsReported()
    {
        var settings = new MasterApiSettings
        {
            Components =
            {
                new StatusComponent { Id = 1, Name = "Master", CreatedAt = default }
            }
        };

        var problem = Assert.Single(settings.Validate());

        Assert.Contains("create_at", problem);
    }

    [Fact]
    public void AServerWithoutAPortIsReported()
    {
        var settings = new MasterApiSettings
        {
            Servers =
            {
                new ServerEntry { Port = 0, ServerName = "Dev", IpPortAddress = "127.0.0.1:4499" }
            }
        };

        var problem = Assert.Single(settings.Validate());

        Assert.Contains("Port", problem);
    }
}
