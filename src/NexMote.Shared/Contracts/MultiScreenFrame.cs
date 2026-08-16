namespace NexMote.Shared.Contracts;

/// <summary>
/// Çoklu ekran eş zamanlı akışında, belirli bir fiziksel ekrana ait canlı JPEG ekran karesi paketi.
/// </summary>
/// <param name="DisplayIndex">Ekranın dizin numarası (0-indexed veya monitör sıra numarası).</param>
/// <param name="JpegBase64">Yakalanan ekran görüntüsünün Base64 kodlu JPEG verisi.</param>
public sealed record MultiScreenFrame(int DisplayIndex, string JpegBase64);
