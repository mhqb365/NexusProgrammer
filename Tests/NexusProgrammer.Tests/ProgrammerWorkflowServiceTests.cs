using NexusProgrammer;
using Xunit;

namespace NexusProgrammer.Tests;

public class ProgrammerWorkflowServiceTests
{
    [Fact]
    public void ResolveSelectionAutoPrefersCh341BeforeCh347()
    {
        var detection = new ProgrammerDetection(
            T48Detected: true,
            Rt809fDetected: true,
            Rt809hDetected: true,
            Ch347Detected: true,
            Ch341Detected: true);

        var selection = ProgrammerWorkflowService.ResolveSelection("auto", detection);

        Assert.Equal("ch341", selection.Key);
        Assert.Equal("CH341 connected", selection.StatusText);
        Assert.True(selection.IsConnected);
    }

    [Fact]
    public void ResolveSelectionUsesRequestedProgrammerWhenDetected()
    {
        var detection = new ProgrammerDetection(
            T48Detected: false,
            Rt809fDetected: false,
            Rt809hDetected: true,
            Ch347Detected: false,
            Ch341Detected: false);

        var selection = ProgrammerWorkflowService.ResolveSelection("rt809h", detection);

        Assert.Equal("rt809h", selection.Key);
        Assert.Equal("RT809H connected", selection.StatusText);
        Assert.True(selection.IsConnected);
    }

    [Fact]
    public void ResolveSelectionReportsSelectedProgrammerDisconnected()
    {
        var detection = new ProgrammerDetection(
            T48Detected: false,
            Rt809fDetected: false,
            Rt809hDetected: false,
            Ch347Detected: false,
            Ch341Detected: false);

        var selection = ProgrammerWorkflowService.ResolveSelection("t48", detection);

        Assert.Equal("none", selection.Key);
        Assert.Equal("XGecu T48 disconnected", selection.StatusText);
        Assert.False(selection.IsConnected);
    }
}
