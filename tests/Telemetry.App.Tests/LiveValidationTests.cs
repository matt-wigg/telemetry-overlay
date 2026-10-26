using System.Text.Json;
using Xunit;

namespace Telemetry.App.Tests;

public sealed class LiveValidationTests
{
    [Fact]
    public async Task MissingTargetWritesFailureWithoutStartingCollectorOrRequestingElevation()
    {
        string directory = Path.Combine(Path.GetTempPath(), "Telemetry-live-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Equal(2, await NativeValidation.VerifyLiveAsync(int.MaxValue, directory));
            using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "live-fps-validation.json")));
            Assert.False(report.RootElement.GetProperty("passed").GetBoolean());
            Assert.Equal("Selected process is not running", report.RootElement.GetProperty("reason").GetString());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
