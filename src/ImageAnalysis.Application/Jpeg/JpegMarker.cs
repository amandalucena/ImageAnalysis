namespace ImageAnalysis.Application.Jpeg;

/// <summary>
/// Marcadores definidos pela ITU-T T.81 (ISO/IEC 10918-1), pelo segundo byte do código:
/// SOI é 0xFF 0xD8, então JpegMarker.Soi vale 0xD8. Um código que não está aqui
/// (ex.: 0x02–0xBF) é desconhecido e não deve ser convertido para este enum; ver JpegMarkers.
/// </summary>
public enum JpegMarker : byte
{
    // Uso temporário em codificação aritmética; sem segmento de dados.
    Tem = 0x01,

    // Início de frame (SOFn). 0xC4, 0xC8 e 0xCC ficam no meio da faixa, mas não são SOF.
    Sof0 = 0xC0,  // Baseline, Huffman
    Sof1 = 0xC1,  // Sequencial estendido, Huffman
    Sof2 = 0xC2,  // Progressive, Huffman
    Sof3 = 0xC3,  // Lossless, Huffman
    Dht = 0xC4,   // Tabelas Huffman
    Sof5 = 0xC5,  // Sequencial diferencial (hierárquico), Huffman
    Sof6 = 0xC6,  // Progressive diferencial (hierárquico), Huffman
    Sof7 = 0xC7,  // Lossless diferencial (hierárquico), Huffman
    Jpg = 0xC8,   // Reservado para extensões
    Sof9 = 0xC9,  // Sequencial estendido, aritmético
    Sof10 = 0xCA, // Progressive, aritmético
    Sof11 = 0xCB, // Lossless, aritmético
    Dac = 0xCC,   // Condicionamento aritmético
    Sof13 = 0xCD, // Sequencial diferencial (hierárquico), aritmético
    Sof14 = 0xCE, // Progressive diferencial (hierárquico), aritmético
    Sof15 = 0xCF, // Lossless diferencial (hierárquico), aritmético

    // Reinício; aparecem dentro dos dados comprimidos, sem segmento de dados.
    Rst0 = 0xD0,
    Rst1 = 0xD1,
    Rst2 = 0xD2,
    Rst3 = 0xD3,
    Rst4 = 0xD4,
    Rst5 = 0xD5,
    Rst6 = 0xD6,
    Rst7 = 0xD7,

    Soi = 0xD8,   // Início da imagem; sem segmento de dados
    Eoi = 0xD9,   // Fim da imagem; sem segmento de dados
    Sos = 0xDA,   // Início do scan, seguido dos dados comprimidos
    Dqt = 0xDB,   // Tabelas de quantização
    Dnl = 0xDC,   // Número de linhas
    Dri = 0xDD,   // Intervalo de reinício
    Dhp = 0xDE,   // Progressão hierárquica
    Exp = 0xDF,   // Expansão de referência

    // Dados de aplicação. O conteúdo (JFIF, Exif, XMP, ICC, Adobe...) é identificado na 22.3.
    App0 = 0xE0,
    App1 = 0xE1,
    App2 = 0xE2,
    App3 = 0xE3,
    App4 = 0xE4,
    App5 = 0xE5,
    App6 = 0xE6,
    App7 = 0xE7,
    App8 = 0xE8,
    App9 = 0xE9,
    App10 = 0xEA,
    App11 = 0xEB,
    App12 = 0xEC,
    App13 = 0xED,
    App14 = 0xEE,
    App15 = 0xEF,

    // Reservados para extensões JPEG.
    Jpg0 = 0xF0,
    Jpg1 = 0xF1,
    Jpg2 = 0xF2,
    Jpg3 = 0xF3,
    Jpg4 = 0xF4,
    Jpg5 = 0xF5,
    Jpg6 = 0xF6,
    Jpg7 = 0xF7,
    Jpg8 = 0xF8,
    Jpg9 = 0xF9,
    Jpg10 = 0xFA,
    Jpg11 = 0xFB,
    Jpg12 = 0xFC,
    Jpg13 = 0xFD,

    Com = 0xFE    // Comentário
}

/// <summary>
/// Classificação dos códigos de marcador. Recebe o byte bruto, e não JpegMarker, para que o
/// leitor classifique também códigos desconhecidos sem convertê-los para o enum.
/// </summary>
public static class JpegMarkers
{
    // 0xFF nunca é código: é preenchimento entre marcadores. 0x00 é o byte de stuffing
    // dos dados comprimidos (0xFF 0x00 representa o byte 0xFF).
    public static bool IsKnown(byte code) => Enum.IsDefined((JpegMarker)code);

    /// <summary>
    /// Marcadores sem campo de comprimento: SOI, EOI, RST0–RST7 e TEM.
    /// </summary>
    public static bool IsStandalone(byte code) =>
        code is (byte)JpegMarker.Soi or (byte)JpegMarker.Eoi or (byte)JpegMarker.Tem || IsRestart(code);

    public static bool IsStartOfFrame(byte code) =>
        code is >= (byte)JpegMarker.Sof0 and <= (byte)JpegMarker.Sof15
            and not (byte)JpegMarker.Dht
            and not (byte)JpegMarker.Jpg
            and not (byte)JpegMarker.Dac;

    public static bool IsRestart(byte code) =>
        code is >= (byte)JpegMarker.Rst0 and <= (byte)JpegMarker.Rst7;

    public static bool IsApplication(byte code) =>
        code is >= (byte)JpegMarker.App0 and <= (byte)JpegMarker.App15;

    public static bool IsReserved(byte code) =>
        code is (byte)JpegMarker.Jpg or >= (byte)JpegMarker.Jpg0 and <= (byte)JpegMarker.Jpg13;
}
