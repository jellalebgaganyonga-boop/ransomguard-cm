using RansomGuard.Agent.Core.Detection.IronClad.Communication;
using RansomGuard.Agent.Core.Detection.IronClad.Models;

namespace RansomGuard.Agent.Tests.Detection.IronClad;

public sealed class IronCladProtocolTests
{
    [Fact]
    public void Serialize_CutPort_Produces_Correct_Format()
    {
        var cmd = new IronCladCommand
        {
            Id = Guid.NewGuid(),
            Action = IronCladAction.CutPort,
            Parameter = "3",
            IssuedAt = DateTime.UtcNow
        };

        var wire = IronCladProtocol.Serialize(cmd);

        Assert.Equal("CMD:CUT_PORT:3", wire);
    }

    [Fact]
    public void Serialize_RestoreAll_Produces_Correct_Format()
    {
        var cmd = new IronCladCommand
        {
            Id = Guid.NewGuid(),
            Action = IronCladAction.RestoreAll,
            Parameter = "",
            IssuedAt = DateTime.UtcNow
        };

        var wire = IronCladProtocol.Serialize(cmd);

        Assert.Equal("CMD:RESTORE_ALL", wire);
    }

    [Fact]
    public void Parse_AckPortCut_Returns_Typed_Response()
    {
        var commandId = Guid.NewGuid();

        var response = IronCladProtocol.Parse("ACK:PORT_3_CUT", commandId);

        Assert.Equal(IronCladResponseType.Ack, response.Type);
        Assert.True(response.IsAcknowledgement);
        Assert.Equal(commandId, response.CommandId);
        Assert.Equal("ACK:PORT_3_CUT", response.RawPayload);
    }

    [Fact]
    public void Parse_StatusResponse_Maps_All_Port_States()
    {
        var commandId = Guid.NewGuid();
        var raw = "STATUS:PORT_1_ACTIVE;PORT_2_ACTIVE;PORT_3_CUT;PORT_4_ACTIVE";

        var response = IronCladProtocol.Parse(raw, commandId);

        Assert.Equal(IronCladResponseType.Status, response.Type);
        Assert.False(response.IsAcknowledgement);

        var states = IronCladProtocol.ParsePortStates(response.RawPayload);
        Assert.Equal(4, states.Count);
        Assert.Equal(PortState.Active, states[1]);
        Assert.Equal(PortState.Active, states[2]);
        Assert.Equal(PortState.Cut, states[3]);
        Assert.Equal(PortState.Active, states[4]);
    }

    [Fact]
    public void Parse_Error_Response_Maps_Error_Code()
    {
        var commandId = Guid.NewGuid();

        var response = IronCladProtocol.Parse("ERR:E001:Invalid port number", commandId);

        Assert.Equal(IronCladResponseType.Error, response.Type);
        Assert.False(response.IsAcknowledgement);
        Assert.Equal("E001", response.ErrorCode);
        Assert.Equal("Invalid port number", response.ErrorMessage);
    }

    [Fact]
    public void Parse_Malformed_Input_Returns_Error_Response()
    {
        var commandId = Guid.NewGuid();

        var response = IronCladProtocol.Parse("GARBAGE_DATA", commandId);

        Assert.Equal(IronCladResponseType.Error, response.Type);
        Assert.Equal("E999", response.ErrorCode);
        Assert.Contains("Malformed", response.ErrorMessage);
    }

    [Theory]
    [InlineData(IronCladAction.CutPort, "1", "CMD:CUT_PORT:1")]
    [InlineData(IronCladAction.RestorePort, "4", "CMD:RESTORE_PORT:4")]
    [InlineData(IronCladAction.CutAll, "", "CMD:CUT_ALL")]
    [InlineData(IronCladAction.Status, "", "CMD:STATUS")]
    [InlineData(IronCladAction.Heartbeat, "", "CMD:HEARTBEAT")]
    public void Roundtrip_Serialize_Preserves_Action(IronCladAction action, string param, string expectedWire)
    {
        var cmd = new IronCladCommand
        {
            Id = Guid.NewGuid(),
            Action = action,
            Parameter = param,
            IssuedAt = DateTime.UtcNow
        };

        var wire = IronCladProtocol.Serialize(cmd);

        Assert.Equal(expectedWire, wire);
    }

    [Fact]
    public void Parse_Version_Response_Extracts_Version_String()
    {
        var commandId = Guid.NewGuid();

        var response = IronCladProtocol.Parse("VERSION:1.0.0-mock", commandId);

        Assert.Equal(IronCladResponseType.Version, response.Type);

        var version = IronCladProtocol.ParseVersion(response.RawPayload);
        Assert.Equal("1.0.0-mock", version);
    }

    [Fact]
    public void Parse_Pong_Returns_Heartbeat_Response()
    {
        var commandId = Guid.NewGuid();

        var response = IronCladProtocol.Parse("PONG", commandId);

        Assert.Equal(IronCladResponseType.Pong, response.Type);
        Assert.True(response.IsAcknowledgement);
    }

    [Fact]
    public void Parse_Empty_Input_Returns_Error()
    {
        var response = IronCladProtocol.Parse("", Guid.NewGuid());

        Assert.Equal(IronCladResponseType.Error, response.Type);
        Assert.Equal("E999", response.ErrorCode);
    }
}
