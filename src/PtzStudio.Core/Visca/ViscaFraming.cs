namespace PtzStudio.Core.Visca;

/// <summary>Tipo de payload do cabeçalho de 8 bytes do VISCA over IP.</summary>
public enum ViscaPayloadType : ushort
{
    Command = 0x0100,
    Inquiry = 0x0110,
    Reply = 0x0111
}

/// <summary>
/// Cabeçalho do VISCA over IP (8 bytes): tipo do payload (2 bytes), tamanho do payload (2 bytes)
/// e número de sequência (4 bytes), todos em ordem big-endian, seguidos do pacote VISCA.
/// </summary>
public static class ViscaFraming
{
    public static byte[] Wrap(byte[] payload, uint sequence, ViscaPayloadType type)
    {
        var data = new byte[8 + payload.Length];
        ushort t = (ushort)type;
        data[0] = (byte)(t >> 8);
        data[1] = (byte)(t & 0xFF);
        data[2] = (byte)((payload.Length >> 8) & 0xFF);
        data[3] = (byte)(payload.Length & 0xFF);
        data[4] = (byte)(sequence >> 24);
        data[5] = (byte)(sequence >> 16);
        data[6] = (byte)(sequence >> 8);
        data[7] = (byte)sequence;
        Buffer.BlockCopy(payload, 0, data, 8, payload.Length);
        return data;
    }
}
