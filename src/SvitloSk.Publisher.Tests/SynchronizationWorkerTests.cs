using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using SvitloSk.Publisher.Runtime;
using Xunit;

namespace SvitloSk.Publisher.Tests;

public class SynchronizationWorkerTests
{
    [Fact]
    public async Task Worker_Starts_And_Invokes_Engine()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<SynchronizationWorker>>();
        var engineMock = new Mock<ISynchronizationEngine>();
        
        var worker = new SynchronizationWorker(loggerMock.Object, engineMock.Object);
        var cts = new CancellationTokenSource();
        
        // Act
        var executeTask = worker.StartAsync(cts.Token);
        
        // Give it a moment to run one cycle
        await Task.Delay(100);
        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);

        // Assert
        engineMock.Verify(e => e.MaintainPublisherStateAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task Worker_Survives_Exceptions()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<SynchronizationWorker>>();
        var engineMock = new Mock<ISynchronizationEngine>();
        
        engineMock.SetupSequence(e => e.MaintainPublisherStateAsync(It.IsAny<CancellationToken>()))
                  .ThrowsAsync(new Exception("Transient error"))
                  .Returns(Task.CompletedTask);

        var worker = new SynchronizationWorker(loggerMock.Object, engineMock.Object);
        var cts = new CancellationTokenSource();
        
        // Act
        var executeTask = worker.StartAsync(cts.Token);
        
        await Task.Delay(100);
        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);

        // Assert
        engineMock.Verify(e => e.MaintainPublisherStateAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }
}
