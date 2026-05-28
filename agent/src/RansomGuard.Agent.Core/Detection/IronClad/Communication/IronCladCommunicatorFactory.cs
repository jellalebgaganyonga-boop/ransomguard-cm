using Microsoft.Extensions.DependencyInjection;

namespace RansomGuard.Agent.Core.Detection.IronClad.Communication;

/// <summary>
/// Factory for creating the appropriate IronClad communicator based on configuration.
/// </summary>
public interface IIronCladCommunicatorFactory
{
    /// <summary>Creates a communicator instance based on the configured communication mode.</summary>
    IIronCladCommunicator Create();
}

/// <summary>
/// Runtime factory that selects TcpMockCommunicator (Sprint 5) or SerialPortCommunicator (Sprint 8)
/// based on <see cref="IronCladOptions.CommunicationMode"/>.
/// </summary>
public sealed class IronCladCommunicatorFactory : IIronCladCommunicatorFactory
{
    private readonly IronCladOptions _options;
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes the factory with configuration and service provider.
    /// </summary>
    public IronCladCommunicatorFactory(IronCladOptions options, IServiceProvider serviceProvider)
    {
        _options = options;
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public IIronCladCommunicator Create() => _options.CommunicationMode switch
    {
        IronCladCommunicationMode.TcpMock => _serviceProvider.GetRequiredService<TcpMockCommunicator>(),
        IronCladCommunicationMode.SerialPort => _serviceProvider.GetRequiredService<SerialPortCommunicator>(),
        _ => throw new InvalidOperationException($"Unknown IronClad communication mode: {_options.CommunicationMode}")
    };
}
