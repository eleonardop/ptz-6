using PtzStudio.Core.Models;
using PtzStudio.Core.Visca;
using Xunit;

namespace PtzStudio.Core.Tests;

public class ViscaTests
{
    [Fact]
    public void PanTilt_LeftUp()
        => Assert.Equal(new byte[] { 0x81, 0x01, 0x06, 0x01, 0x05, 0x03, 0x01, 0x01, 0xFF }, ViscaCommands.PanTiltDrive(1, -5, 3));

    [Fact]
    public void PanTilt_RightDown()
        => Assert.Equal(new byte[] { 0x81, 0x01, 0x06, 0x01, 0x0A, 0x04, 0x02, 0x02, 0xFF }, ViscaCommands.PanTiltDrive(1, 10, -4));

    [Fact]
    public void PanTilt_Stop()
        => Assert.Equal(new byte[] { 0x81, 0x01, 0x06, 0x01, 0x01, 0x01, 0x03, 0x03, 0xFF }, ViscaCommands.PanTiltStop(1));

    [Fact]
    public void PanTilt_ClampsToVisca_Limits()
    {
        var b = ViscaCommands.PanTiltDrive(1, 99, 99);
        Assert.Equal(0x18, b[4]);
        Assert.Equal(0x14, b[5]);
    }

    [Fact]
    public void Zoom_TeleWideStop()
    {
        Assert.Equal(0x00, ViscaCommands.Zoom(1, 0)[4]);
        Assert.Equal(0x20, ViscaCommands.Zoom(1, 1)[4]);
        Assert.Equal(0x27, ViscaCommands.Zoom(1, 99)[4]);  // limitado ao máximo (8 -> código 7)
        Assert.Equal(0x33, ViscaCommands.Zoom(1, -4)[4]);
    }

    [Fact]
    public void Preset_Set_Recall_Home()
    {
        Assert.Equal(new byte[] { 0x81, 0x01, 0x04, 0x3F, 0x01, 0x05, 0xFF }, ViscaCommands.PresetSet(1, 5));
        Assert.Equal(new byte[] { 0x81, 0x01, 0x04, 0x3F, 0x02, 0x05, 0xFF }, ViscaCommands.PresetRecall(1, 5));
        Assert.Equal(new byte[] { 0x81, 0x01, 0x06, 0x04, 0xFF }, ViscaCommands.Home(1));
    }

    [Fact]
    public void Focus_Commands()
    {
        Assert.Equal(0x02, ViscaCommands.Focus(1, 1)[4]);
        Assert.Equal(0x03, ViscaCommands.Focus(1, -1)[4]);
        Assert.Equal(0x00, ViscaCommands.Focus(1, 0)[4]);
    }

    [Fact]
    public void OverIp_Header_IsCorrect()
    {
        var payload = ViscaCommands.Home(1);
        var f = ViscaFraming.Wrap(payload, 0x01020304, ViscaPayloadType.Command);
        Assert.Equal(new byte[] { 0x01, 0x00, 0x00, 0x05, 0x01, 0x02, 0x03, 0x04 }, f[..8]);
        Assert.Equal(payload, f[8..]);
    }

    [Fact]
    public void Response_Parsing()
    {
        Assert.Equal(ViscaResponseKind.Ack, ViscaResponse.Parse(new byte[] { 0x90, 0x41, 0xFF }, ViscaTransportMode.UdpPuro).Kind);
        Assert.Equal(ViscaResponseKind.Conclusao, ViscaResponse.Parse(new byte[] { 0x90, 0x51, 0xFF }, ViscaTransportMode.UdpPuro).Kind);
        Assert.Equal(ViscaResponseKind.Erro, ViscaResponse.Parse(new byte[] { 0x90, 0x60, 0x02, 0xFF }, ViscaTransportMode.UdpPuro).Kind);
        Assert.Equal(ViscaResponseKind.Inquerito, ViscaResponse.Parse(new byte[] { 0x90, 0x50, 0x00, 0x20, 0xFF }, ViscaTransportMode.UdpPuro).Kind);
        var wrapped = ViscaFraming.Wrap(new byte[] { 0x90, 0x41, 0xFF }, 1, ViscaPayloadType.Command);
        Assert.Equal(ViscaResponseKind.Ack, ViscaResponse.Parse(wrapped, ViscaTransportMode.ViscaOverIp).Kind);
    }
}
