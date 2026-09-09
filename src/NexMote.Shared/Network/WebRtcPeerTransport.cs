using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using NexMote.Shared.Contracts;
using SIPSorcery.Net;

namespace NexMote.Shared.Network;

/// <summary>
/// Teknisyen ve Ajan arasında STUN destekli doğrudan uçtan uca (P2P) WebRTC veri kanalı yöneticisi.
/// Bağlantı sağlanamazsa veya koparsa üst katman otomatik olarak SignalR sunucu rölesine döner.
/// </summary>
public sealed class WebRtcPeerTransport : IDisposable
{
    private RTCPeerConnection? _peerConnection;
    private readonly ConcurrentDictionary<string, RTCDataChannel> _dataChannels = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _stateLock = new();
    private bool _isDisposed;

    public bool IsConnected { get; private set; }

    public event Action<WebRtcSignalMessage>? OnSignalReady;
    public event Action<string, string>? OnDataMessageReceived;
    public event Action<bool>? OnConnectionStateChanged;
    public event Action<string>? OnLog;

    private static readonly RTCConfiguration DefaultConfig = new()
    {
        iceServers = new List<RTCIceServer>
        {
            new() { urls = "stun:stun.l.google.com:19302" },
            new() { urls = "stun:stun1.l.google.com:19302" },
            new() { urls = "stun:stun2.l.google.com:19302" }
        }
    };

    private void EnsurePeerConnection()
    {
        if (_peerConnection is not null) return;

        _peerConnection = new RTCPeerConnection(DefaultConfig);

        _peerConnection.onicecandidate += (candidate) =>
        {
            if (candidate is not null && !string.IsNullOrEmpty(candidate.candidate))
            {
                OnSignalReady?.Invoke(new WebRtcSignalMessage(
                    "candidate",
                    Candidate: candidate.candidate,
                    SdpMid: candidate.sdpMid,
                    SdpMLineIndex: (int)candidate.sdpMLineIndex));
            }
        };

        _peerConnection.onconnectionstatechange += (state) =>
        {
            OnLog?.Invoke($"WebRTC bağlantı durumu: {state}");
            var connected = state == RTCPeerConnectionState.connected;
            lock (_stateLock)
            {
                IsConnected = connected;
            }
            OnConnectionStateChanged?.Invoke(connected);
        };

        _peerConnection.ondatachannel += (channel) =>
        {
            OnLog?.Invoke($"WebRTC veri kanalı alındı: {channel.label}");
            RegisterDataChannel(channel);
        };
    }

    private void RegisterDataChannel(RTCDataChannel channel)
    {
        _dataChannels[channel.label] = channel;

        channel.onopen += () =>
        {
            OnLog?.Invoke($"WebRTC veri kanalı açıldı (P2P Aktif): {channel.label}");
            lock (_stateLock)
            {
                IsConnected = true;
            }
            OnConnectionStateChanged?.Invoke(true);
        };

        channel.onclose += () =>
        {
            OnLog?.Invoke($"WebRTC veri kanalı kapandı: {channel.label}");
            _dataChannels.TryRemove(channel.label, out _);
        };

        channel.onmessage += (dc, protocol, data) =>
        {
            if (data is { Length: > 0 })
            {
                var text = Encoding.UTF8.GetString(data);
                if (text.StartsWith(ChunkPrefix, StringComparison.Ordinal))
                {
                    var reassembled = ProcessIncomingChunk(text);
                    if (reassembled is not null)
                    {
                        OnDataMessageReceived?.Invoke(channel.label, reassembled);
                    }
                }
                else
                {
                    OnDataMessageReceived?.Invoke(channel.label, text);
                }
            }
        };
    }

    private const int MaxChunkSize = 32_768; // 32 KB per SCTP packet
    private const string ChunkPrefix = "__CHK__|";
    private long _chunkMsgCounter;
    private readonly ConcurrentDictionary<long, ChunkAssembly> _chunkAssemblies = new();

    private sealed class ChunkAssembly
    {
        public string[] Chunks { get; }
        public int TotalChunks { get; }
        private int _receivedCount;
        public long CreatedTicks { get; }

        public ChunkAssembly(int totalChunks)
        {
            TotalChunks = totalChunks;
            Chunks = new string[totalChunks];
            CreatedTicks = Stopwatch.GetTimestamp();
        }

        public bool AddChunk(int index, string data)
        {
            if (index >= 0 && index < TotalChunks && Chunks[index] == null)
            {
                Chunks[index] = data;
                return Interlocked.Increment(ref _receivedCount) == TotalChunks;
            }
            return false;
        }

        public string Reassemble() => string.Concat(Chunks);
    }

    private string? ProcessIncomingChunk(string text)
    {
        try
        {
            var now = Stopwatch.GetTimestamp();
            if (_chunkAssemblies.Count > 60)
            {
                foreach (var kvp in _chunkAssemblies)
                {
                    if ((now - kvp.Value.CreatedTicks) * 1000 / Stopwatch.Frequency > 5000)
                    {
                        _chunkAssemblies.TryRemove(kvp.Key, out _);
                    }
                }
            }

            var p1 = text.IndexOf('|', ChunkPrefix.Length);
            if (p1 < 0) return null;
            var p2 = text.IndexOf('|', p1 + 1);
            if (p2 < 0) return null;
            var p3 = text.IndexOf('|', p2 + 1);
            if (p3 < 0) return null;

            if (!long.TryParse(text.AsSpan(ChunkPrefix.Length, p1 - ChunkPrefix.Length), out var msgId) ||
                !int.TryParse(text.AsSpan(p1 + 1, p2 - p1 - 1), out var chunkIdx) ||
                !int.TryParse(text.AsSpan(p2 + 1, p3 - p2 - 1), out var totalChunks) ||
                totalChunks <= 0 || totalChunks > 1000)
            {
                return null;
            }

            var chunkData = text.Substring(p3 + 1);
            var assembly = _chunkAssemblies.GetOrAdd(msgId, _ => new ChunkAssembly(totalChunks));
            if (assembly.AddChunk(chunkIdx, chunkData))
            {
                _chunkAssemblies.TryRemove(msgId, out _);
                return assembly.Reassemble();
            }
        }
        catch
        {
        }
        return null;
    }

    /// <summary>
    /// Teknisyen tarafında çağrılır: Veri kanalını açar, SDP Offer üretir ve sinyal dinleyicisine iletir.
    /// </summary>
    public async Task StartOfferAsync()
    {
        EnsurePeerConnection();

        var streamChannel = await _peerConnection!.createDataChannel("stream");
        RegisterDataChannel(streamChannel);

        var inputChannel = await _peerConnection.createDataChannel("input");
        RegisterDataChannel(inputChannel);

        var offer = _peerConnection.createOffer();
        await _peerConnection.setLocalDescription(offer);

        OnSignalReady?.Invoke(new WebRtcSignalMessage("offer", Sdp: offer.sdp));
    }

    /// <summary>
    /// Ajan tarafında çağrılır: Teknisyenden gelen SDP Offer'ı kabul eder, SDP Answer üretir.
    /// </summary>
    public async Task HandleOfferAsync(string sdp)
    {
        EnsurePeerConnection();

        var init = new RTCSessionDescriptionInit
        {
            type = RTCSdpType.offer,
            sdp = sdp
        };
        _peerConnection!.setRemoteDescription(init);

        var answer = _peerConnection.createAnswer();
        await _peerConnection.setLocalDescription(answer);

        OnSignalReady?.Invoke(new WebRtcSignalMessage("answer", Sdp: answer.sdp));
    }

    /// <summary>
    /// Teknisyen tarafında çağrılır: Ajanın yanıtladığı SDP Answer'ı yerel eşe bağlar.
    /// </summary>
    public Task HandleAnswerAsync(string sdp)
    {
        if (_peerConnection is null) return Task.CompletedTask;

        var init = new RTCSessionDescriptionInit
        {
            type = RTCSdpType.answer,
            sdp = sdp
        };
        _peerConnection.setRemoteDescription(init);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Karşı taraftan gelen ICE candidate bilgilerini yerel eşe ekler.
    /// </summary>
    public void HandleCandidate(string candidate, string? sdpMid, int? sdpMLineIndex)
    {
        if (_peerConnection is null || string.IsNullOrWhiteSpace(candidate)) return;

        try
        {
            var init = new RTCIceCandidateInit
            {
                candidate = candidate,
                sdpMid = sdpMid,
                sdpMLineIndex = (ushort)(sdpMLineIndex ?? 0)
            };
            _peerConnection.addIceCandidate(init);
        }
        catch (Exception ex)
        {
            OnLog?.Invoke($"ICE adayı eklenirken hata: {ex.Message}");
        }
    }

    /// <summary>
    /// Açık olan belirtilen etiketli veri kanalından doğrudan P2P metin mesajı gönderir.
    /// Büyük mesajlar (örneğin yüksek çözünürlüklü ekran kareleri) otomatik olarak parçalanarak (chunking) iletilir.
    /// Başarılıysa true, veri kanalı açık değilse veya arabellek tıkalıysa false döner.
    /// </summary>
    public bool SendData(string channelLabel, string message)
    {
        if (_isDisposed || !IsConnected) return false;

        if (_dataChannels.TryGetValue(channelLabel, out var channel) &&
            channel.readyState == RTCDataChannelState.open)
        {
            try
            {
                // Arabellek taşma koruması (Bufferbloat guard): Bekleyen veri 512 KB'ı aşarsa gönderimi ertele
                if (channel.bufferedAmount > 512 * 1024)
                {
                    return false;
                }

                if (message.Length <= MaxChunkSize)
                {
                    channel.send(message);
                    return true;
                }

                // Büyük mesajı 32 KB'lık paketler halinde parçalayarak güvenle ilet
                var msgId = Interlocked.Increment(ref _chunkMsgCounter);
                var totalChunks = (message.Length + MaxChunkSize - 1) / MaxChunkSize;

                for (var i = 0; i < totalChunks; i++)
                {
                    var offset = i * MaxChunkSize;
                    var length = Math.Min(MaxChunkSize, message.Length - offset);
                    var chunkData = message.Substring(offset, length);
                    var chunkMsg = $"{ChunkPrefix}{msgId}|{i}|{totalChunks}|{chunkData}";
                    channel.send(chunkMsg);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        foreach (var channel in _dataChannels.Values)
        {
            try { channel.close(); } catch { }
        }
        _dataChannels.Clear();

        try
        {
            _peerConnection?.close();
        }
        catch { }
        _peerConnection = null;
        IsConnected = false;
    }
}
