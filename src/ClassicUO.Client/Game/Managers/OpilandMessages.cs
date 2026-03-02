// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClassicUO.Utility.Logging;

namespace ClassicUO.Game.Managers;

/// <summary>
/// Base class for all Opiland WebSocket messages
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(MobilePositionMessage), "position")]
[JsonDerivedType(typeof(ChatMessage), "chat")]
public abstract class OpilandMessage
{
    /// <summary>
    /// Serial of the player mobile sending this message
    /// </summary>
    [JsonPropertyName("serial")]
    public uint Serial { get; set; }

    /// <summary>
    /// Unix timestamp (seconds) when the message was created
    /// </summary>
    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    /// <summary>
    /// Serialize message to JSON string for transmission
    /// </summary>
    public string Serialize()
    {
        try
        {
            // Serialize as base type to include the type discriminator
            return JsonSerializer.Serialize<OpilandMessage>(this, _jsonOptions);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to serialize Opiland message: {ex}");
            return null;
        }
    }

    /// <summary>
    /// Deserialize message from JSON string
    /// </summary>
    public static OpilandMessage Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<OpilandMessage>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to deserialize Opiland message: {ex}");
            return null;
        }
    }
}

/// <summary>
/// Message containing player position information
/// </summary>
public class MobilePositionMessage : OpilandMessage
{
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }

    [JsonPropertyName("z")]
    public int Z { get; set; }

    [JsonPropertyName("mapIndex")]
    public int MapIndex { get; set; }

    [JsonPropertyName("hue")]
    public int Hue { get; set; }
}

/// <summary>
/// Message containing chat text to be broadcast
/// </summary>
public class ChatMessage : OpilandMessage
{
    [JsonPropertyName("sender")]
    public string Sender { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; }

    [JsonPropertyName("hue")]
    public ushort Hue { get; set; }
}

/// <summary>
/// Event args for chat message received events
/// </summary>
public class ChatMessageReceivedEventArgs : EventArgs
{
    public string ClientId { get; }
    public ChatMessage Message { get; }

    public ChatMessageReceivedEventArgs(ChatMessage message)
    {
        Message = message;
    }

    public ChatMessageReceivedEventArgs(string clientId, ChatMessage message)
    {
        ClientId = clientId;
        Message = message;
    }
}

/// <summary>
/// Manager for parsing and handling Opiland messages
/// </summary>
public sealed class OpilandMessageParser
{
    private static readonly Lazy<OpilandMessageParser> _instance = new(() => new OpilandMessageParser());
    public static OpilandMessageParser Instance => _instance.Value;

    /// <summary>
    /// Event fired when a position message is received
    /// </summary>
    public event EventHandler<MobilePositionMessage> PositionMessageReceived;

    /// <summary>
    /// Event fired when a chat message is received
    /// </summary>
    public event EventHandler<ChatMessage> ChatMessageReceived;

    /// <summary>
    /// Event fired when an unknown message type is received
    /// </summary>
    public event EventHandler<OpilandMessage> UnknownMessageReceived;

    private OpilandMessageParser()
    {
    }

    /// <summary>
    /// Parse a JSON message string and dispatch to appropriate event
    /// </summary>
    /// <param name="json">The JSON message string</param>
    /// <returns>The parsed message, or null if parsing failed</returns>
    public OpilandMessage ParseAndDispatch(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            OpilandMessage message = OpilandMessage.Deserialize(json);

            if (message == null)
            {
                return null;
            }

            // Dispatch to appropriate event on main thread
            MainThreadQueue.EnqueueAction(() =>
            {
                DispatchMessage(message);
            });

            return message;
        }
        catch (Exception ex)
        {
            Log.Error($"Error parsing Opiland message: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Parse a JSON message string into a typed message (synchronous)
    /// </summary>
    /// <param name="json">The JSON message string</param>
    /// <returns>The parsed message, or null if parsing failed</returns>
    public OpilandMessage Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return OpilandMessage.Deserialize(json);
    }

    /// <summary>
    /// Parse and cast to a specific message type
    /// </summary>
    public T ParseAs<T>(string json) where T : OpilandMessage
    {
        return Parse(json) as T;
    }

    /// <summary>
    /// Dispatch a message to the appropriate event handler
    /// </summary>
    private void DispatchMessage(OpilandMessage message)
    {
        switch (message)
        {
            case MobilePositionMessage positionMsg:
                PositionMessageReceived?.Invoke(this, positionMsg);
                break;

            case ChatMessage chatMsg:
                ChatMessageReceived?.Invoke(this, chatMsg);
                break;

            default:
                UnknownMessageReceived?.Invoke(this, message);
                Log.Warn($"Unknown Opiland message type: {message.GetType().Name}");
                break;
        }
    }

    /// <summary>
    /// Create a position message for the given mobile
    /// </summary>
    public static MobilePositionMessage CreatePositionMessage(uint serial, string name, int x, int y, int z, int mapIndex, int hue)
    {
        return new MobilePositionMessage
        {
            Serial = serial,
            Name = name,
            X = x,
            Y = y,
            Z = z,
            MapIndex = mapIndex,
            Hue = hue
        };
    }

    /// <summary>
    /// Create a chat message
    /// </summary>
    public static ChatMessage CreateChatMessage(uint serial, string sender, string text, ushort hue = 0)
    {
        return new ChatMessage
        {
            Serial = serial,
            Sender = sender,
            Text = text,
            Hue = hue
        };
    }
}
