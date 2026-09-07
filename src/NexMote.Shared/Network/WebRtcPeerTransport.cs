using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
                OnDataMessageReceived?.Invoke(channel.label, text);
            }
        };
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
    /// Başarılıysa true, veri kanalı açık değilse false (böylece SignalR rölesine dönülebilir) döner.
    /// </summary>
    public bool SendData(string channelLabel, string message)
    {
        if (_isDisposed || !IsConnected) return false;

        if (_dataChannels.TryGetValue(channelLabel, out var channel) &&
            channel.readyState == RTCDataChannelState.open)
        {
            try
            {
                channel.send(message);
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
