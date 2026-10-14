using System;
using System.IO;
using NUnit.Framework;
using Ohrbob.App.Services;

namespace Ohrbob.App.Tests.Services;

[TestFixture]
public class ConfigServiceTests
{
    private string _tempFilePath = null!;

    [SetUp]
    public void SetUp()
    {
        _tempFilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".json");
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_tempFilePath))
        {
            File.Delete(_tempFilePath);
        }
    }

    [Test]
    public void Load_WhenNoFileExists_ShouldBeNullOrEmptyAndNotCrash()
    {
        if (File.Exists(_tempFilePath))
        {
            File.Delete(_tempFilePath);
        }

        var service = new ConfigService(_tempFilePath);
        
        Assert.DoesNotThrow(() => service.Load());
        Assert.That(service.ServerUrl, Is.Null.Or.Empty);
        Assert.That(service.StreamToken, Is.Null.Or.Empty);
    }

    [Test]
    public void SaveAndLoad_ShouldMatchSavedValues()
    {
        var service = new ConfigService(_tempFilePath);
        const string expectedUrl = "https://example.com/ohrbob";
        const string expectedToken = "secret-token-123";

        service.Save(expectedUrl, expectedToken);

        Assert.That(service.ServerUrl, Is.EqualTo(expectedUrl));
        Assert.That(service.StreamToken, Is.EqualTo(expectedToken));

        var loadService = new ConfigService(_tempFilePath);
        loadService.Load();

        Assert.That(loadService.ServerUrl, Is.EqualTo(expectedUrl));
        Assert.That(loadService.StreamToken, Is.EqualTo(expectedToken));
    }
}
