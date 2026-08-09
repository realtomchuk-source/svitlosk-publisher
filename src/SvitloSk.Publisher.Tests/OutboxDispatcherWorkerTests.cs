using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using SvitloSk.Publisher.Channels;
using SvitloSk.Publisher.Domain;
using SvitloSk.Publisher.Runtime.Persistence;
using Xunit;
using SvitloSk.Publisher.Runtime;

namespace SvitloSk.Publisher.Tests;

public class OutboxDispatcherWorkerTests
{
    private readonly Mock<IOutboxRepository> _outboxRepoMock;
    private readonly Mock<IPublicationPipeline> _pipelineMock;
    private readonly Mock<IExternalPublicationIdentityResolver> _identityResolverMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IServiceScopeFactory> _scopeFactoryMock;
    private readonly OutboxDispatcherWorker _worker;

    public OutboxDispatcherWorkerTests()
    {
        _outboxRepoMock = new Mock<IOutboxRepository>();
        _pipelineMock = new Mock<IPublicationPipeline>();
        _identityResolverMock = new Mock<IExternalPublicationIdentityResolver>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();

        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock.Setup(sp => sp.GetService(typeof(IOutboxRepository))).Returns(_outboxRepoMock.Object);
        serviceProviderMock.Setup(sp => sp.GetService(typeof(IPublicationPipeline))).Returns(_pipelineMock.Object);
        serviceProviderMock.Setup(sp => sp.GetService(typeof(IExternalPublicationIdentityResolver))).Returns(_identityResolverMock.Object);
        serviceProviderMock.Setup(sp => sp.GetService(typeof(IUnitOfWork))).Returns(_unitOfWorkMock.Object);

        var scopeMock = new Mock<IServiceScope>();
        scopeMock.Setup(s => s.ServiceProvider).Returns(serviceProviderMock.Object);

        _scopeFactoryMock = new Mock<IServiceScopeFactory>();
        _scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);

        var loggerMock = new Mock<ILogger<OutboxDispatcherWorker>>();

        _worker = new OutboxDispatcherWorker(loggerMock.Object, _scopeFactoryMock.Object);
    }

    private Task InvokeWorkerAsync()
    {
        // Use reflection to invoke the private ProcessOutboxMessagesAsync method for testing
        var method = typeof(OutboxDispatcherWorker).GetMethod("ProcessOutboxMessagesAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (Task)method.Invoke(_worker, new object[] { CancellationToken.None });
    }

    [Fact]
    public async Task CreateText_Success_UpdatesStatusAndPersistsIdentity()
    {
        var msg = new OutboxMessage { OperationId = Guid.NewGuid(), PublicationId = Guid.NewGuid().ToString(), OperationType = TransportOperation.CREATE, ArtifactType = TransportArtifactType.TEXT_ONLY, Payload = "test" };
        _outboxRepoMock.Setup(x => x.GetPendingMessages(It.IsAny<int>())).Returns(new List<OutboxMessage> { msg });
        
        _pipelineMock.Setup(x => x.DispatchAsync(It.IsAny<PublicationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AcceptedPublication("chat1:msg1", "chat1"));

        await InvokeWorkerAsync();

        Assert.Equal(OutboxOperationStatus.Completed, msg.Status);
        _identityResolverMock.Verify(x => x.RecordExternalIdentity(msg.PublicationId, "chat1:msg1"), Times.Once);
        _unitOfWorkMock.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateText_Success_UpdatesStatusAndLeavesIdentity()
    {
        var msg = new OutboxMessage { OperationId = Guid.NewGuid(), PublicationId = Guid.NewGuid().ToString(), OperationType = TransportOperation.UPDATE, ArtifactType = TransportArtifactType.TEXT_ONLY, Payload = "test", ExternalIdentity = "chat1:msg1" };
        _outboxRepoMock.Setup(x => x.GetPendingMessages(It.IsAny<int>())).Returns(new List<OutboxMessage> { msg });
        
        _pipelineMock.Setup(x => x.DispatchAsync(It.IsAny<PublicationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AcceptedPublication("chat1:msg1", "chat1"));

        await InvokeWorkerAsync();

        Assert.Equal(OutboxOperationStatus.Completed, msg.Status);
        _identityResolverMock.Verify(x => x.RecordExternalIdentity(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _unitOfWorkMock.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Delete_Success_UpdatesStatusAndRemovesIdentity()
    {
        var msg = new OutboxMessage { OperationId = Guid.NewGuid(), PublicationId = Guid.NewGuid().ToString(), OperationType = TransportOperation.DELETE, ExternalIdentity = "chat1:msg1" };
        _outboxRepoMock.Setup(x => x.GetPendingMessages(It.IsAny<int>())).Returns(new List<OutboxMessage> { msg });
        
        _pipelineMock.Setup(x => x.DispatchAsync(It.IsAny<PublicationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AcceptedPublication("chat1:msg1", "chat1"));

        await InvokeWorkerAsync();

        Assert.Equal(OutboxOperationStatus.Completed, msg.Status);
        _identityResolverMock.Verify(x => x.RemoveExternalIdentity(msg.PublicationId), Times.Once);
        _unitOfWorkMock.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RetryableError_IncrementsAttemptAndSetsNextRetry()
    {
        var msg = new OutboxMessage { OperationId = Guid.NewGuid(), PublicationId = Guid.NewGuid().ToString(), OperationType = TransportOperation.CREATE, ArtifactType = TransportArtifactType.TEXT_ONLY, Payload = "test" };
        _outboxRepoMock.Setup(x => x.GetPendingMessages(It.IsAny<int>())).Returns(new List<OutboxMessage> { msg });
        
        _pipelineMock.Setup(x => x.DispatchAsync(It.IsAny<PublicationRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new System.Net.Http.HttpRequestException("Telegram API returned retryable error 429"));

        await InvokeWorkerAsync();

        Assert.Equal(OutboxOperationStatus.Pending, msg.Status);
        Assert.Equal(1, msg.AttemptCount);
        Assert.NotNull(msg.NextRetryAt);
        _unitOfWorkMock.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NonRetryableError_SetsStatusToFailed()
    {
        var msg = new OutboxMessage { OperationId = Guid.NewGuid(), PublicationId = Guid.NewGuid().ToString(), OperationType = TransportOperation.CREATE, ArtifactType = TransportArtifactType.TEXT_ONLY, Payload = "test" };
        _outboxRepoMock.Setup(x => x.GetPendingMessages(It.IsAny<int>())).Returns(new List<OutboxMessage> { msg });
        
        _pipelineMock.Setup(x => x.DispatchAsync(It.IsAny<PublicationRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Telegram API returned ok=false: invalid message"));

        await InvokeWorkerAsync();

        Assert.Equal(OutboxOperationStatus.Failed, msg.Status);
        Assert.Equal(1, msg.AttemptCount);
        _unitOfWorkMock.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
