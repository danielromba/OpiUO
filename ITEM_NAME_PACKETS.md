# Ultima Online Item/Entity Name Packets

## Overview
There are two main packet systems for obtaining item/entity names in Ultima Online:

---

## 1. **Single Click System (Legacy)**

### Request Packet: **0x09 - Single Click**
**File:** `OutgoingPackets.cs:846`

```csharp
public static void Send_ClickRequest(this AsyncNetClient socket, uint serial)
{
    const byte ID = 0x09;
    
    var writer = new StackDataWriter(length < 0 ? 64 : length);
    writer.WriteUInt8(ID);           // Packet ID: 0x09
    writer.WriteUInt32BE(serial);    // Item/Entity Serial
    
    socket.Send(writer.BufferWritten);
}
```

**Structure:**
- Byte 0: Packet ID (0x09)
- Bytes 1-4: Serial (uint32, Big Endian)
- **Total Size:** 5 bytes

### Response Packet: **0xCC - Display Cliloc String**
**File:** `DisplayClilocString.cs:23`

```csharp
uint serial = p.ReadUInt32BE();         // Entity serial
ushort graphic = p.ReadUInt16BE();      // Graphic ID
MessageType type = p.ReadUInt8();       // Message type
ushort hue = p.ReadUInt16BE();          // Text hue
ushort font = p.ReadUInt16BE();         // Font
uint cliloc = p.ReadUInt32BE();         // Cliloc ID (name)
AffixType flags = p.ReadUInt8();        // Flags (if 0xCC)
string name = p.ReadASCII(30);          // Entity name (30 chars)
string affix = p.ReadASCII();           // Optional affix
string arguments = p.ReadUnicode();     // Optional arguments
```

**Key Points:**
- Returns name as ASCII string (30 characters max)
- Includes cliloc ID for localized names
- Can have prefix/suffix text via affix field

---

## 2. **Object Properties List (OPL) System (Modern)**

### Request Packet: **0xD6 - Query Properties (Mega Cliloc Request)**
**File:** `OutgoingPackets.cs:3147`

```csharp
public static void Send_MegaClilocRequest(this AsyncNetClient socket, List<uint> serials)
{
    const byte ID = 0xD6;
    
    var writer = new StackDataWriter(length < 0 ? 64 : length);
    writer.WriteUInt8(ID);
    
    // Can request up to 15 items at once
    int count = Math.Min(15, serials.Count);
    
    for (int i = 0; i < count; ++i)
    {
        writer.WriteUInt32BE(serials[i]);  // Each serial
    }
    
    socket.Send(writer.BufferWritten);
}
```

**Structure:**
- Byte 0: Packet ID (0xD6)
- Bytes 1-2: Packet Length (variable)
- Bytes 3+: List of serials (4 bytes each, max 15)
- **Size:** Variable (3 + (4 × count) bytes)

**Features:**
- Batch request (up to 15 items at once)
- Returns complete property list, not just name
- More efficient for multiple items

### Response Packet: **0xD6 - Mega Cliloc (Object Properties)**
**File:** `MegaCliloc.cs:16`

```csharp
ushort unknown = p.ReadUInt16BE();      // Unknown (must be ≤1)
uint serial = p.ReadUInt32BE();         // Item/Entity serial
p.Skip(2);                               // Skip 2 bytes
uint revision = p.ReadUInt32BE();        // Revision number

// Loop through all properties
while (p.Position < p.Length)
{
    int cliloc = p.ReadUInt32BE();       // Property cliloc ID
    if (cliloc == 0) break;              // 0 = end of list
    
    ushort length = p.ReadUInt16BE();    // Argument length
    string argument = string.Empty;
    
    if (length != 0)
        argument = p.ReadUnicodeLE(length / 2);  // Unicode arguments
    
    // Translate cliloc to get actual text
    string propertyText = Clilocs.Translate(cliloc, argument);
}
```

**Key Points:**
- First property (cliloc) is usually the item name
- Returns all properties (weight, durability, etc.)
- Uses cliloc system for localization
- Includes revision number for caching

---

## 3. **Alternative: Extended Command (0xBF) - Query Properties**
**File:** `OutgoingPackets.cs:3130`

```csharp
const byte ID = 0xBF;  // Extended command packet

writer.WriteUInt8(ID);
writer.WriteUInt16BE(0x10);          // Subcommand 0x10 = Query Properties
writer.WriteUInt32BE(serial);        // Item serial
```

**Structure:**
- Byte 0: Packet ID (0xBF)
- Bytes 1-2: Packet Length
- Bytes 3-4: Subcommand (0x10)
- Bytes 5-8: Serial
- **Total Size:** 9 bytes

**Response:** Same as 0xD6 (Mega Cliloc response)

---

## Property Storage
**File:** `ObjectPropertiesListManager.cs:88`

The client caches received properties:

```csharp
public bool TryGetNameAndData(uint serial, out string name, out string data)
{
    if (_itemsProperties.TryGetValue(serial, out ItemProperty p))
    {
        name = p.Name;           // Cached name
        data = p.Data;           // Cached properties
        return true;
    }
    
    name = data = null;
    return false;
}
```

**ItemProperty Structure:**
```csharp
public class ItemProperty
{
    public string Name;          // Item name (from first cliloc)
    public string Data;          // All properties combined
    public uint Revision;        // For cache invalidation
    public uint Serial;          // Item serial
    public int NameCliloc;       // Name cliloc ID
}
```

---

## Usage Recommendations

### When to use **0x09 (Single Click)**:
- ✅ Quick name-only lookup for a single item
- ✅ Legacy client support required
- ✅ Minimal network traffic
- ❌ Don't use for batch requests
- ❌ Limited info (name only)

### When to use **0xD6 (Mega Cliloc)**:
- ✅ Batch requests (up to 15 items)
- ✅ Need complete property info (not just name)
- ✅ Modern client features (tooltips, item info)
- ✅ Efficient caching with revision numbers
- ❌ Slightly more overhead for single items

### When to use **0xBF/0x10 (Extended Query)**:
- ✅ Single item property request
- ✅ Alternative to 0xD6 for compatibility
- ✅ Same response format as 0xD6

---

## Example Flow

```
Client                          Server
  |                               |
  |-- 0xD6 (Request Properties) ->|
  |   Serial: 0x40001234          |
  |                               |
  |<- 0xD6 (Mega Cliloc) ---------|
  |   Serial: 0x40001234          |
  |   Revision: 1                 |
  |   Cliloc 1050039: "katana"    |  <- This is the name
  |   Cliloc 1060639: "dur 40/40" |
  |   Cliloc 1061168: "str req 20"|
  |   ... more properties ...     |
  |                               |
```

The **first cliloc** in the response is typically the item name.

---

## Packet IDs Quick Reference

| Packet ID | Name                    | Direction | Purpose                |
|-----------|-------------------------|-----------|------------------------|
| **0x09**  | Single Click            | Client→Server | Request item name (legacy) |
| **0xCC**  | Display Cliloc String   | Server→Client | Name response (legacy) |
| **0xD6**  | Mega Cliloc Request     | Client→Server | Request properties (batch) |
| **0xD6**  | Mega Cliloc Response    | Server→Client | Property list with name |
| **0xBF/0x10** | Extended Query Properties | Client→Server | Request properties (single) |

---

## Implementation Files

- **Outgoing:** `OutgoingPackets.cs`
  - `Send_ClickRequest()` - Line 846
  - `Send_MegaClilocRequest()` - Line 3147
  
- **Incoming:** 
  - `MegaCliloc.cs:16` - Handles 0xD6 response
  - `DisplayClilocString.cs:23` - Handles 0xCC response
  
- **Storage:** `ObjectPropertiesListManager.cs`
  - Caches item properties
  - Provides `TryGetNameAndData()` method

---

## Notes

1. **Cliloc System:** Names are stored as cliloc IDs that get translated to localized text
2. **Revision Numbers:** Used for cache invalidation - if revision changes, properties are outdated
3. **Batch Efficiency:** 0xD6 can request 15 items in one packet (saves bandwidth)
4. **First Property:** The first cliloc in OPL is almost always the item name
5. **Unicode vs ASCII:** Modern packets use Unicode, legacy uses ASCII
