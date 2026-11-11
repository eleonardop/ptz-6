using PtzStudio.Core.Models;

namespace PtzStudio.Core.Visca;

/// <summary>
/// Montagem dos comandos VISCA padrão (Sony). Os bytes seguem a especificação VISCA publicada;
/// o suporte a cada comando na LJ510-30X-N deve ser confirmado com o teste de conexão e o manual da câmera.
/// </summary>
public static class ViscaCommands
{
    public const int MaxPanSpeed = 0x18;
    public const int MaxTiltSpeed = 0x14;
    public const int MaxZoomSpeed = 8; // velocidades 1..8 -> código VISCA 0..7

    public static byte Header(int address) => (byte)(0x80 | Math.Clamp(address, 1, 7));

    /// <summary>pan: negativo = esquerda, positivo = direita. tilt: positivo = cima, negativo = baixo. 0 = parar o eixo.</summary>
    public static byte[] PanTiltDrive(int address, int pan, int tilt, int maxPan = MaxPanSpeed, int maxTilt = MaxTiltSpeed)
    {
        int ps = Math.Clamp(Math.Abs(pan), 0, Math.Min(maxPan, MaxPanSpeed));
        int ts = Math.Clamp(Math.Abs(tilt), 0, Math.Min(maxTilt, MaxTiltSpeed));
        byte pd = pan < 0 ? (byte)0x01 : pan > 0 ? (byte)0x02 : (byte)0x03;
        byte td = tilt > 0 ? (byte)0x01 : tilt < 0 ? (byte)0x02 : (byte)0x03;
        return new byte[] { Header(address), 0x01, 0x06, 0x01, (byte)Math.Max(1, ps), (byte)Math.Max(1, ts), pd, td, 0xFF };
    }

    public static byte[] PanTiltStop(int address) => PanTiltDrive(address, 0, 0);

    /// <summary>speed: positivo = aproximar (tele), negativo = afastar (wide), 0 = parar. Faixa 1..8 (mapeada para 0..7 do VISCA).</summary>
    public static byte[] Zoom(int address, int speed, int max = MaxZoomSpeed)
    {
        int s = Math.Clamp(Math.Abs(speed), 0, Math.Min(max, MaxZoomSpeed));
        byte code;
        if (speed == 0) code = 0x00;
        else if (speed > 0) code = (byte)(0x20 | Math.Clamp(s - 1, 0, 7));
        else code = (byte)(0x30 | Math.Clamp(s - 1, 0, 7));
        return new byte[] { Header(address), 0x01, 0x04, 0x07, code, 0xFF };
    }

    /// <summary>dir: positivo = foco distante, negativo = foco próximo, 0 = parar.</summary>
    public static byte[] Focus(int address, int dir)
    {
        byte code = dir > 0 ? (byte)0x02 : dir < 0 ? (byte)0x03 : (byte)0x00;
        return new byte[] { Header(address), 0x01, 0x04, 0x08, code, 0xFF };
    }

    public static byte[] AutoFocus(int address, bool on) =>
        new byte[] { Header(address), 0x01, 0x04, 0x38, on ? (byte)0x02 : (byte)0x03, 0xFF };

    public static byte[] PresetSet(int address, int slot) =>
        new byte[] { Header(address), 0x01, 0x04, 0x3F, 0x01, (byte)Math.Clamp(slot, 0, 254), 0xFF };

    public static byte[] PresetRecall(int address, int slot) =>
        new byte[] { Header(address), 0x01, 0x04, 0x3F, 0x02, (byte)Math.Clamp(slot, 0, 254), 0xFF };

    public static byte[] PresetReset(int address, int slot) =>
        new byte[] { Header(address), 0x01, 0x04, 0x3F, 0x00, (byte)Math.Clamp(slot, 0, 254), 0xFF };

    public static byte[] Home(int address) => new byte[] { Header(address), 0x01, 0x06, 0x04, 0xFF };

    /// <summary>Consulta de versão: usada para saber se a câmera responde.</summary>
    public static byte[] VersionInquiry(int address) => new byte[] { Header(address), 0x09, 0x00, 0x02, 0xFF };

    // ---------------- Imagem da câmera ----------------
    // Brilho = o "ajuste de vídeo" da câmera (CAM_Brightness, 04 A1), valor absoluto.
    // Exposição = compensação de exposição (CAM_ExpComp, 04 3E / 0E / 4E).
    // O suporte de cada um na LJ510-30X-N é CONFIRMADO PELA PRÓPRIA CÂMERA com as consultas abaixo; nunca é presumido.

    public static byte[] BrightnessDirect(int address, int value)
    {
        int v = Math.Clamp(value, 0, 255);
        return new byte[] { Header(address), 0x01, 0x04, 0xA1, 0x00, 0x00, (byte)((v >> 4) & 0x0F), (byte)(v & 0x0F), 0xFF };
    }

    public static byte[] BrightnessInquiry(int address) => new byte[] { Header(address), 0x09, 0x04, 0xA1, 0xFF };

    public static byte[] ExpCompPower(int address, bool on) =>
        new byte[] { Header(address), 0x01, 0x04, 0x3E, on ? (byte)0x02 : (byte)0x03, 0xFF };

    /// <summary>dir: positivo = mais exposição, negativo = menos, 0 = voltar ao padrão (0 EV).</summary>
    public static byte[] ExpCompStep(int address, int dir) =>
        new byte[] { Header(address), 0x01, 0x04, 0x0E, dir > 0 ? (byte)0x02 : dir < 0 ? (byte)0x03 : (byte)0x00, 0xFF };

    /// <summary>position: 0..14 (7 = 0 EV).</summary>
    public static byte[] ExpCompDirect(int address, int position)
    {
        int p = Math.Clamp(position, 0, 14);
        return new byte[] { Header(address), 0x01, 0x04, 0x4E, 0x00, 0x00, (byte)((p >> 4) & 0x0F), (byte)(p & 0x0F), 0xFF };
    }

    public static byte[] ExpCompPowerInquiry(int address) => new byte[] { Header(address), 0x09, 0x04, 0x3E, 0xFF };
    public static byte[] ExpCompPositionInquiry(int address) => new byte[] { Header(address), 0x09, 0x04, 0x4E, 0xFF };

    /// <summary>Lê o valor de uma resposta de consulta no formato "y0 50 00 00 0p 0q FF" (p e q = metades do valor).</summary>
    public static bool TryReadValue(byte[]? reply, out int value)
    {
        value = 0;
        if (reply == null || reply.Length < 7 || reply[1] != 0x50 || reply[^1] != 0xFF) return false;
        value = ((reply[4] & 0x0F) << 4) | (reply[5] & 0x0F);
        return true;
    }

    /// <summary>Lê ligado/desligado de uma resposta "y0 50 02 FF" (ligado) ou "y0 50 03 FF" (desligado).</summary>
    public static bool TryReadOnOff(byte[]? reply, out bool on)
    {
        on = false;
        if (reply == null || reply.Length < 4 || reply[1] != 0x50 || reply[^1] != 0xFF) return false;
        if (reply[2] == 0x02) { on = true; return true; }
        return reply[2] == 0x03;
    }
}

public enum ViscaResponseKind { Desconhecida, Ack, Conclusao, Erro, Inquerito }

public sealed record ViscaResponse(ViscaResponseKind Kind, byte[] Payload)
{
    public static ViscaResponse Parse(byte[] data, ViscaTransportMode mode)
    {
        var p = data;
        if (mode == ViscaTransportMode.ViscaOverIp && data.Length >= 8) p = data[8..];
        var kind = ViscaResponseKind.Desconhecida;
        if (p.Length >= 3 && p[0] >= 0x90 && p[^1] == 0xFF)
        {
            switch (p[1] >> 4)
            {
                case 4: kind = ViscaResponseKind.Ack; break;
                case 5: kind = p.Length > 3 ? ViscaResponseKind.Inquerito : ViscaResponseKind.Conclusao; break;
                case 6: kind = ViscaResponseKind.Erro; break;
            }
        }
        return new ViscaResponse(kind, p);
    }
}
