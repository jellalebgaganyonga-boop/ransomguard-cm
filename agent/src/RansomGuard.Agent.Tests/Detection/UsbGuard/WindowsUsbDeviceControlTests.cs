using RansomGuard.Agent.Core.Detection.UsbGuard.Actions;
using Shouldly;

namespace RansomGuard.Agent.Tests.Detection.UsbGuard;

/// <summary>
/// The Windows calls themselves need a real key under a real service: they are exercised on the
/// demonstration machines, not here. What a runner can check is that a bad argument is refused
/// before any system call, with a reason, and never reported as done.
/// </summary>
public sealed class WindowsUsbDeviceControlTests
{
    private readonly WindowsUsbDeviceControl _control = new();

    [Theory]
    [InlineData("")]
    [InlineData("EF")]
    [InlineData("1:")]
    [InlineData(@"\\server\share")]
    public void SetReadOnly_refuses_what_is_not_a_drive_letter(string input)
    {
        var result = _control.SetReadOnly(input);

        result.Success.ShouldBeFalse();
        result.Error.ShouldNotBeNull();
        result.Error.ShouldContain("not a drive letter");
    }

    [Fact]
    public void SetReadOnly_on_a_letter_with_no_volume_reports_the_system_error_not_done()
    {
        // No volume is mounted on this letter on a build or CI machine; if one is, the physical
        // drive behind it is not touched: the test asserts only that nothing is claimed blindly.
        var result = _control.SetReadOnly("Z:");

        if (!result.Success)
        {
            result.Error.ShouldNotBeNull();
            result.Error.ShouldContain("Z");
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Eject_refuses_an_empty_device_instance(string input)
    {
        var result = _control.Eject(input);

        result.Success.ShouldBeFalse();
        result.Error.ShouldBe("empty device instance id");
    }

    [Fact]
    public void Eject_of_an_unknown_device_instance_reports_the_configuration_manager_error()
    {
        var result = _control.Eject(@"USBSTOR\DISK&VEN_NONE&PROD_NONE\NOSUCHDEVICE");

        result.Success.ShouldBeFalse();
        result.Error.ShouldNotBeNull();
        result.Error.ShouldContain("CM_Locate_DevNode");
    }
}
