using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SvitloSk.Publisher.Runtime;
using Xunit;

namespace SvitloSk.Publisher.Tests;

public class SynchronizationWorkerTests
{
    private Mock<IServiceScopeFactory> CreateScopeFactoryMock(ISynchronizationEngine engine)
    {
        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        var scopeMock = new Mock<IServiceScope>();
        var serviceProviderMock = new Mock<IServiceProvider>();

        serviceProviderMock.Setup(sp => sp.GetService(typeof(ISynchronizationEngine)))
                           .Returns(engine);

        scopeMock.Setup(s => s.ServiceProvider).Returns(serviceProviderMock.Object);
        scopeFactoryMock.Setup(sf => sf.CreateScope()).Returns(scopeMock.Object);

        return scopeFactoryMock;
    }

    [Fact]
    public async Task Worker_Starts_And_Invokes_Engine()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<SynchronizationWorker>>();
        var engineMock = new Mock<ISynchronizationEngine>();
        var scopeFactoryMock = CreateScopeFactoryMock(engineMock.Object);
        
        var worker = new SynchronizationWorker(loggerMock.Object, scopeFactoryMock.Object);
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

        var scopeFactoryMock = CreateScopeFactoryMock(engineMock.Object);
        var worker = new SynchronizationWorker(loggerMock.Object, scopeFactoryMock.Object);
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
