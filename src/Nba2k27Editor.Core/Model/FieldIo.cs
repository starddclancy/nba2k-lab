using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Nba2k27Editor.Core.Memory;

namespace Nba2k27Editor.Core.Model;

/// <summary>Authored payload helpers, address resolution and value encoding.</summary>
public static class FieldIo
{
    private static readonly HashSet<string> ImplementationRequiredFlags = new(StringComparer.Ordinal)
    {
        "from_address_dropdown", "offset2",
    };

    public static readonly IReadOnlyDictionary<string, string> AddressDropdownTypes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["team_address_dropdown"] = "Teams",
        ["stadium_address_dropdown"] = "Stadiums",
        ["uniform_dropdown"] = "Jerseys",
    };

    private static readonly IReadOnlyDictionary<string, int> FixedNumericTypeWidths = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["byte"] = 1,
        ["ubyte"] = 1,
        ["ushort"] = 2,
        ["uint"] = 4,
        ["uint64"] = 8,
        ["ulonglong"] = 8,
        ["pointer"] = 8,
        ["address"] = 8,
        ["team_address_dropdown"] = 8,
        ["stadium_address_dropdown"] = 8,
        ["uniform_dropdown"] = 8,
    };

    private static readonly HashSet<string> ParentPointerTypes = new(StringComparer.Ordinal)
    {
        "pointer", "address", "uint64", "ulonglong",
    };

    private static readonly HashSet<string> PlayerZeroTo100FieldIds = new(StringComparer.Ordinal)
    {
        "MINPOTENTIAL", "MAXPOTENTIAL", "MINIMUMPOTENTIAL", "MAXIMUMPOTENTIAL",
        "AVGPERCENT", "AVERAGEPERCENT", "BUSTPERCENT", "BUSTPERCENTAGE",
        "BOOMPERCENT", "BOOMPERCENTAGE", "FINANCIALSECURITY", "LOYALTY", "PLAYFORWINNER",
    };

    private static readonly HashSet<string> NumericTypes = new(StringComparer.Ordinal)
    {
        "uint", "number", "integer", "byte", "ubyte", "ushort", "uint64", "ulonglong",
        "pointer", "address", "combo", "dropdown", "slider",
        "team_address_dropdown", "stadium_address_dropdown", "uniform_dropdown",
    };

    private static readonly Regex NonHex = new("[^0-9A-Fa-f]", RegexOptions.Compiled);

    public static int FieldOffset(JsonObject payload)
    {
        if (payload["address"] is not { } node)
        {
            throw new KeyNotFoundException("authored payload is missing address");
        }
        return Conversions.ToInt(NodeText(node));
    }

    public static string TypeKey(JsonObject payload)
    {
        var typeKey = NodeText(payload["type"]).Trim().ToLowerInvariant();
        if (typeKey.Length > 0)
        {
            return typeKey;
        }
        if (payload.ContainsKey("bit_length") && (payload.ContainsKey("bit_offset") || payload.ContainsKey("startBit")))
        {
            return "bitfield";
        }
        return string.Empty;
    }

    public static bool ImplementedPayload(JsonObject payload)
    {
        var typeKey = TypeKey(payload);
        if (typeKey == "result_score")
        {
            return payload.ContainsKey("offset2");
        }
        foreach (var flag in ImplementationRequiredFlags)
        {
            if (payload.ContainsKey(flag))
            {
                return false;
            }
        }
        return typeKey switch
        {
            "uint" or "number" or "integer" or "int" or "byte" or "ubyte" or "ushort" or "uint64"
                or "ulonglong" or "pointer" or "address" or "combo" or "dropdown" or "slider"
                or "bit" or "bitfield" or "vtable_int" or "float" or "string" or "wstring"
                or "utf16le_vtable" or "binary" or "hex_bytes" or "color"
                or "team_address_dropdown" or "stadium_address_dropdown" or "uniform_dropdown" => true,
            _ => false,
        };
    }

    public static bool ReadablePayload(JsonObject payload)
    {
        return ImplementedPayload(payload) || (TypeKey(payload) == "ptr_string" && !payload.ContainsKey("offset2"));
    }

    public static int ResolvedLengthBits(JsonObject payload)
    {
        int length = Conversions.ToInt(NodeText(payload["length"]));
        if (length > 0)
        {
            return length;
        }
        int bitLength = Conversions.ToInt(NodeText(payload["bit_length"]));
        if (bitLength > 0)
        {
            return bitLength;
        }
        int byteLength = Conversions.ToInt(NodeText(payload["byteLength"]));
        return byteLength > 0 ? byteLength * 8 : 0;
    }

    private static int BitsToBytes(int bits) => Math.Max(1, (bits + 7) / 8);

    public static int NumericWidth(JsonObject payload)
    {
        int explicitBytes = Conversions.ToInt(NodeText(payload["byteLength"]));
        if (explicitBytes > 0)
        {
            return explicitBytes;
        }
        var typeKey = TypeKey(payload);
        int authoredBits = ResolvedLengthBits(payload);
        if (typeKey is "color" or "hex_bytes")
        {
            int authoredLength = Conversions.ToInt(NodeText(payload["length"]));
            if (authoredLength > 0)
            {
                return authoredLength;
            }
        }
        if (typeKey == "binary" && !payload.ContainsKey("bit_offset") && !payload.ContainsKey("startBit"))
        {
            int authoredLength = Conversions.ToInt(NodeText(payload["length"]));
            if (authoredLength > 0)
            {
                return authoredLength;
            }
        }
        if (FixedNumericTypeWidths.TryGetValue(typeKey, out int width))
        {
            return width;
        }
        if (authoredBits > 0)
        {
            return BitsToBytes(authoredBits);
        }
        throw new KeyNotFoundException("authored payload is missing length, bit_length, or byteLength");
    }

    public static (int BitOffset, int BitLength, int Width) BitWindow(JsonObject payload)
    {
        int bitOffset = Conversions.ToInt(NodeText(payload["bit_offset"]));
        if (bitOffset == 0)
        {
            bitOffset = Conversions.ToInt(NodeText(payload["startBit"]));
        }
        int bitLength = ResolvedLengthBits(payload);
        if (bitLength <= 0)
        {
            throw new KeyNotFoundException("authored bitfield payload is missing length, bit_length, or byteLength");
        }
        return (bitOffset, bitLength, BitsToBytes(bitOffset + bitLength));
    }

    public static long ReadPointerValue(IMemory memory, long address)
    {
        var raw = memory.ReadBytes(address, memory.PointerSize);
        long value = 0;
        for (int index = raw.Length - 1; index >= 0; index--)
        {
            value = (value << 8) | raw[index];
        }
        return value;
    }

    public static long FieldAddress(
        IMemory memory,
        long recordAddress,
        JsonObject payload,
        JsonObject? parentPayload = null)
    {
        long baseAddress = recordAddress;
        if (parentPayload is not null)
        {
            long parentAddress = baseAddress + FieldOffset(parentPayload);
            baseAddress = ParentPointerTypes.Contains(TypeKey(parentPayload))
                ? ReadPointerValue(memory, parentAddress)
                : parentAddress;
            if (baseAddress == 0)
            {
                throw new InvalidOperationException(
                    $"Parent object is not loaded: {NodeText(payload["parent"])} (pointer at 0x{parentAddress:X})");
            }
        }
        long address = baseAddress + FieldOffset(payload);
        if (payload["requiresDereference"] is JsonValue deref && deref.GetValueKind() == System.Text.Json.JsonValueKind.True)
        {
            int dereferenceOffset = Conversions.ToInt(NodeText(payload["dereferenceAddress"]));
            long pointerSlot = dereferenceOffset != 0 ? baseAddress + dereferenceOffset : address;
            long pointer = ReadPointerValue(memory, pointerSlot);
            if (pointer == 0)
            {
                throw new InvalidOperationException($"Referenced object is not loaded (pointer at 0x{pointerSlot:X})");
            }
            address = pointer + FieldOffset(payload);
        }
        return address;
    }

    public static bool UsesBitfieldIo(JsonObject payload)
    {
        var typeKey = TypeKey(payload);
        if (typeKey is "bit" or "bitfield" or "vtable_int")
        {
            return true;
        }
        bool hasBitOffset = payload.ContainsKey("bit_offset") || payload.ContainsKey("startBit");
        return typeKey is "number" or "integer" or "int" or "binary" or "ushort"
            && hasBitOffset && ResolvedLengthBits(payload) > 0;
    }

    public static object ReadAuthoredValue(IMemory memory, long address, JsonObject payload)
    {
        if (!ReadablePayload(payload))
        {
            throw new NotSupportedException($"authored type requires backend implementation: {NodeText(payload["type"])}");
        }
        var typeKey = TypeKey(payload);
        if (typeKey == "ptr_string")
        {
            long pointer = ReadPointerValue(memory, address);
            if (pointer <= 0)
            {
                return string.Empty;
            }
            bool unicode = payload["unicode"] is JsonValue flag
                && flag.GetValueKind() == System.Text.Json.JsonValueKind.True;
            return ReadString(memory, pointer, payload, unicode);
        }
        if (UsesBitfieldIo(payload))
        {
            var (bitOffset, bitLength, width) = BitWindow(payload);
            long rawInt = ReadLittleEndian(memory.ReadBytes(address, width));
            long value = (rawInt >> bitOffset) & ((1L << bitLength) - 1);
            if (typeKey == "int" && value >= 1L << (bitLength - 1))
            {
                value -= 1L << bitLength;
            }
            return value;
        }
        if (NumericTypes.Contains(typeKey))
        {
            return ReadLittleEndian(memory.ReadBytes(address, NumericWidth(payload)));
        }
        if (typeKey == "float")
        {
            return BitConverter.ToSingle(memory.ReadBytes(address, 4), 0);
        }
        if (typeKey is "string" or "wstring" or "utf16le_vtable")
        {
            return ReadString(memory, address, payload, typeKey is "wstring" or "utf16le_vtable");
        }
        if (typeKey == "result_score")
        {
            var (first, second) = ResultScoreAddresses(address, payload);
            float left = BitConverter.ToSingle(memory.ReadBytes(first, 4), 0);
            float right = BitConverter.ToSingle(memory.ReadBytes(second, 4), 0);
            return (CoerceResultComponent(left), CoerceResultComponent(right));
        }
        if (typeKey is "color" or "binary" or "hex_bytes")
        {
            return memory.ReadBytes(address, NumericWidth(payload));
        }
        throw new NotSupportedException($"authored type requires backend implementation: {NodeText(payload["type"])}");
    }

    public static void WriteAuthoredValue(IMemory memory, long address, JsonObject payload, object? value)
    {
        if (!ImplementedPayload(payload))
        {
            throw new NotSupportedException($"authored type requires backend implementation: {NodeText(payload["type"])}");
        }
        var typeKey = TypeKey(payload);
        if (UsesBitfieldIo(payload))
        {
            var (bitOffset, bitLength, width) = BitWindow(payload);
            long rawInt = ReadLittleEndian(memory.ReadBytes(address, width));
            long mask = ((1L << bitLength) - 1) << bitOffset;
            long newInt = (rawInt & ~mask) | ((Conversions.ToInt(value) << bitOffset) & mask);
            memory.WriteBytes(address, WriteLittleEndian(newInt, width));
            return;
        }
        if (NumericTypes.Contains(typeKey))
        {
            int width = NumericWidth(payload);
            // 64-bit pointers must not be narrowed; ToInt would drop the high half.
            memory.WriteBytes(address, WriteLittleEndian(Conversions.ToLong(value), width));
            return;
        }
        if (typeKey == "float")
        {
            memory.WriteBytes(address, BitConverter.GetBytes((float)Conversions.ToDouble(value)));
            return;
        }
        if (typeKey is "string" or "wstring" or "utf16le_vtable")
        {
            WriteString(memory, address, payload, value, typeKey is "wstring" or "utf16le_vtable");
            return;
        }
        if (typeKey == "result_score")
        {
            var (first, second) = ParseResultScore(value);
            var (firstAddress, secondAddress) = ResultScoreAddresses(address, payload);
            memory.WriteBytes(firstAddress, BitConverter.GetBytes((float)first));
            memory.WriteBytes(secondAddress, BitConverter.GetBytes((float)second));
            return;
        }
        if (typeKey == "color")
        {
            memory.WriteBytes(address, ParseColorValue(value, NumericWidth(payload)));
            return;
        }
        if (typeKey is "binary" or "hex_bytes")
        {
            int width = NumericWidth(payload);
            var raw = value as byte[] ?? Array.Empty<byte>();
            if (raw.Length != width)
            {
                throw new ArgumentException($"binary value must be exactly {width} bytes");
            }
            memory.WriteBytes(address, raw);
            return;
        }
        throw new NotSupportedException($"authored type requires backend implementation: {NodeText(payload["type"])}");
    }

    public static object RawToDisplayValue(string section, JsonObject field, JsonObject payload, object? rawValue)
    {
        var typeKey = TypeKey(payload);
        if (typeKey == "hex_bytes")
        {
            return string.Join(" ", ((byte[])rawValue!).Select(one => one.ToString("x2", CultureInfo.InvariantCulture)));
        }
        if (typeKey == "color" && rawValue is byte[] colorBytes)
        {
            return "#" + Convert.ToHexString(colorBytes);
        }
        if (typeKey == "result_score" && rawValue is ValueTuple<double, double> score)
        {
            return $"{FormatResultComponent(score.Item1)}-{FormatResultComponent(score.Item2)}";
        }
        var mapped = MappedDisplayValue(payload, rawValue);
        if (mapped is not null)
        {
            return mapped;
        }
        var fieldName = FieldDisplayOrName(field);
        var fieldId = FieldIdentity(fieldName);
        int lengthBits = ResolvedLengthBits(payload);
        if (payload.ContainsKey("season_year_base"))
        {
            int startYear = Conversions.ToInt(NodeText(payload["season_year_base"])) + Conversions.ToInt(rawValue);
            bool seasonRange = payload["season_range"] is JsonValue range
                && range.GetValueKind() == System.Text.Json.JsonValueKind.True;
            return seasonRange ? $"{startYear}-{startYear + 1}" : (long)startYear;
        }
        if (payload.ContainsKey("year_map_base") || IsYearSection(section) == false && Conversions.IsYearOffsetField(fieldName))
        {
            int baseYear = Conversions.ToInt(NodeText(payload["year_map_base"]));
            return (long)Conversions.ConvertRawToYear(Conversions.ToInt(rawValue), baseYear > 0 ? baseYear : Conversions.YearBase);
        }
        if (fieldId is "HEIGHT" or "WINGSPAN")
        {
            return (long)Conversions.RawHeightToInches(Conversions.ToInt(rawValue));
        }
        if (IsTrue(payload, "div100"))
        {
            return Conversions.ToInt(rawValue) / 100.0;
        }
        if (IsTrue(payload, "body_scale_0_100") || IsTrue(payload, "body_scale_25_75"))
        {
            return (long)Conversions.ConvertRawToBodyScaleDisplay(rawValue, lengthBits);
        }
        if (payload["scale"] is { } scaleNode)
        {
            double scale = Conversions.ToDouble(NodeText(scaleNode), 1.0);
            return Conversions.ToDouble(rawValue) * (scale == 0 ? 1.0 : scale);
        }
        if (FieldIdentity(NodeText(field["normalized_name"])) == "BOX" && fieldName.Contains("+/-"))
        {
            return (long)Conversions.ConvertRawToBoxPlusMinus(rawValue);
        }
        if (fieldId == "POTENTIAL")
        {
            return (long)Conversions.ConvertRawToPotential(Conversions.ToInt(rawValue), lengthBits);
        }
        if (PlayerZeroTo100FieldIds.Contains(fieldId))
        {
            return (long)Conversions.ConvertTendencyRawToRating(Conversions.ToInt(rawValue), lengthBits);
        }
        if (IsTrue(payload, "injury_duration_days") || fieldId is "INJURY1DURATION" or "INJURY2DURATION")
        {
            return (long)Conversions.ConvertRawToInjuryDurationDays(Conversions.ToInt(rawValue));
        }
        if (section is "Attributes" or "Durability")
        {
            return (long)Conversions.ConvertRawToRating(Conversions.ToInt(rawValue), lengthBits);
        }
        if (section == "Tendencies")
        {
            return (long)Conversions.ConvertTendencyRawToRating(Conversions.ToInt(rawValue), lengthBits);
        }
        return rawValue ?? 0L;
    }

    public static object? DisplayToRawValue(string section, JsonObject field, JsonObject payload, object? value)
    {
        var typeKey = TypeKey(payload);
        if (typeKey == "hex_bytes")
        {
            return value is string text ? Convert.FromHexString(NonHex.Replace(text, string.Empty)) : value as byte[];
        }
        if (typeKey == "color")
        {
            return ParseColorValue(value, NumericWidth(payload));
        }
        if (typeKey == "result_score")
        {
            return ParseResultScore(value);
        }
        var mapped = MappedRawValue(payload, value);
        if (mapped is not null)
        {
            return mapped;
        }
        var fieldName = FieldDisplayOrName(field);
        var fieldId = FieldIdentity(fieldName);
        int lengthBits = ResolvedLengthBits(payload);
        if (payload.ContainsKey("season_year_base"))
        {
            var text = value?.ToString() ?? string.Empty;
            var startText = text.Split('-')[0].Trim();
            return Conversions.ToInt(startText) - Conversions.ToInt(NodeText(payload["season_year_base"]));
        }
        if (payload.ContainsKey("year_map_base") || IsYearSection(section) == false && Conversions.IsYearOffsetField(fieldName))
        {
            int baseYear = Conversions.ToInt(NodeText(payload["year_map_base"]));
            return Conversions.ConvertYearToRaw(Conversions.ToInt(value), baseYear > 0 ? baseYear : Conversions.YearBase);
        }
        if (fieldId is "HEIGHT" or "WINGSPAN")
        {
            return Conversions.HeightInchesToRaw(Conversions.ToInt(value));
        }
        if (IsTrue(payload, "div100"))
        {
            return (int)Math.Round(Conversions.ToDouble(value) * 100);
        }
        if (fieldId == "WEIGHT")
        {
            var normalized = Conversions.NormalizeWeightValue(value);
            return normalized ?? value;
        }
        if (IsTrue(payload, "body_scale_0_100") || IsTrue(payload, "body_scale_25_75"))
        {
            return Conversions.ConvertBodyScaleDisplayToRaw(value, lengthBits);
        }
        if (payload["scale"] is { } scaleNode)
        {
            double scale = Conversions.ToDouble(NodeText(scaleNode), 1.0);
            return scale == 0 ? value : Conversions.ToDouble(value) / scale;
        }
        if (FieldIdentity(NodeText(field["normalized_name"])) == "BOX" && fieldName.Contains("+/-"))
        {
            return Conversions.ConvertBoxPlusMinusToRaw(value);
        }
        if (fieldId == "POTENTIAL")
        {
            return Conversions.ConvertPotentialToRaw(Conversions.ToDouble(value), lengthBits);
        }
        if (PlayerZeroTo100FieldIds.Contains(fieldId))
        {
            return Conversions.ConvertRatingToTendencyRaw(Conversions.ToDouble(value), lengthBits);
        }
        if (IsTrue(payload, "injury_duration_days") || fieldId is "INJURY1DURATION" or "INJURY2DURATION")
        {
            return Conversions.ConvertInjuryDurationDaysToRaw(Conversions.ToDouble(value));
        }
        if (section is "Attributes" or "Durability")
        {
            return Conversions.ConvertRatingToRaw(Conversions.ToDouble(value), lengthBits);
        }
        if (section == "Tendencies")
        {
            return Conversions.ConvertRatingToTendencyRaw(Conversions.ToDouble(value), lengthBits);
        }
        return value;
    }

    public static string FieldIdentity(object? value)
    {
        var text = (value?.ToString() ?? string.Empty).ToUpperInvariant();
        return Regex.Replace(text, "[^A-Z0-9]+", string.Empty);
    }

    public static string FieldDisplayOrName(JsonObject field)
    {
        var display = NodeText(field["display_name"]);
        return display.Length > 0 ? display : NodeText(field["normalized_name"]);
    }

    public static string NodeText(JsonNode? node)
    {
        if (node is null)
        {
            return string.Empty;
        }
        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var text))
            {
                return text;
            }
            if (value.TryGetValue<double>(out var number))
            {
                return number == Math.Floor(number) && Math.Abs(number) < 1e15
                    ? ((long)number).ToString(CultureInfo.InvariantCulture)
                    : number.ToString("R", CultureInfo.InvariantCulture);
            }
            if (value.TryGetValue<bool>(out var flag))
            {
                return flag ? "true" : "false";
            }
        }
        return node.ToJsonString();
    }

    private static bool IsYearSection(string section) => section is "MyNBA Eras";

    private static bool IsTrue(JsonObject payload, string key)
    {
        return payload[key] is JsonValue value && value.GetValueKind() == System.Text.Json.JsonValueKind.True;
    }

    private static long ReadLittleEndian(byte[] raw)
    {
        long value = 0;
        for (int index = raw.Length - 1; index >= 0; index--)
        {
            value = (value << 8) | raw[index];
        }
        return value;
    }

    private static byte[] WriteLittleEndian(long value, int width)
    {
        var raw = new byte[width];
        for (int index = 0; index < width; index++)
        {
            raw[index] = (byte)((value >> (8 * index)) & 0xFF);
        }
        return raw;
    }

    private static string ReadString(IMemory memory, long address, JsonObject payload, bool unicode)
    {
        int length = Conversions.ToInt(NodeText(payload["length"]));
        if (length <= 0)
        {
            throw new KeyNotFoundException("authored string payload is missing length");
        }
        return unicode ? memory.ReadWString(address, length) : memory.ReadAscii(address, length);
    }

    private static void WriteString(IMemory memory, long address, JsonObject payload, object? value, bool unicode)
    {
        int length = Conversions.ToInt(NodeText(payload["length"]));
        if (length <= 0)
        {
            throw new KeyNotFoundException("authored string payload is missing length");
        }
        var text = value?.ToString() ?? string.Empty;
        if (unicode)
        {
            memory.WriteWStringFixed(address, text, length);
        }
        else
        {
            memory.WriteAsciiFixed(address, text, length);
        }
    }

    private static (long First, long Second) ResultScoreAddresses(long address, JsonObject payload)
    {
        int firstOffset = FieldOffset(payload);
        int secondOffset = Conversions.ToInt(NodeText(payload["offset2"]));
        if (secondOffset <= 0)
        {
            throw new KeyNotFoundException("result_score payload is missing offset2");
        }
        return (address, address - firstOffset + secondOffset);
    }

    private static double CoerceResultComponent(float value)
    {
        double rounded = Math.Round(value);
        return Math.Abs(value - rounded) < 0.0001 ? (long)rounded : value;
    }

    private static (double First, double Second) ParseResultScore(object? value)
    {
        if (value is ValueTuple<double, double> pair)
        {
            return pair;
        }
        var text = (value?.ToString() ?? string.Empty).Trim();
        char separator = text.Contains('-') ? '-' : text.Contains(':') ? ':' : '\0';
        if (separator == '\0')
        {
            throw new ArgumentException("result_score must be a two-part value like '1-0'");
        }
        var parts = text.Split(separator, 2);
        return (Conversions.ToDouble(parts[0].Trim()), Conversions.ToDouble(parts[1].Trim()));
    }

    private static string FormatResultComponent(double value)
    {
        double rounded = Math.Round(value);
        return Math.Abs(value - rounded) < 0.0001
            ? ((long)rounded).ToString(CultureInfo.InvariantCulture)
            : value.ToString("g", CultureInfo.InvariantCulture);
    }

    private static byte[] ParseColorValue(object? value, int width)
    {
        byte[] raw;
        if (value is byte[] bytes)
        {
            raw = bytes;
        }
        else
        {
            var text = (value?.ToString() ?? string.Empty).Trim();
            if (text.StartsWith('#'))
            {
                text = text[1..];
            }
            text = NonHex.Replace(text, string.Empty);
            raw = Convert.FromHexString(text);
        }
        if (raw.Length != width)
        {
            throw new ArgumentException($"color value must be exactly {width} bytes");
        }
        return raw;
    }

    private static object? MappedDisplayValue(JsonObject payload, object? rawValue)
    {
        if (IsTrue(payload, "id_prefixed_dropdown"))
        {
            long rawId = Conversions.ToInt(rawValue);
            foreach (var optionKey in new[] { "dropdown", "values" })
            {
                if (payload[optionKey] is not JsonArray options)
                {
                    continue;
                }
                foreach (var option in options)
                {
                    var text = NodeText(option);
                    if (Conversions.ParseIdPrefixedOption(text) == rawId)
                    {
                        return text;
                    }
                }
            }
            return null;
        }
        var byValues = ListMappingValue(rawValue, payload["values"]);
        if (byValues is not null)
        {
            return byValues;
        }
        var byDropdown = ListMappingValue(rawValue, payload["dropdown"]);
        if (byDropdown is not null)
        {
            return byDropdown;
        }
        if (payload["value_mapping"] is JsonObject mapping)
        {
            var key = rawValue?.ToString() ?? string.Empty;
            if (mapping.TryGetPropertyValue(key, out var direct))
            {
                return NodeText(direct);
            }
        }
        return null;
    }

    private static object? MappedRawValue(JsonObject payload, object? value)
    {
        if (IsTrue(payload, "id_prefixed_dropdown"))
        {
            return Conversions.ParseIdPrefixedOption(value);
        }
        var byValues = ReverseListMapping(value, payload["values"]);
        if (byValues is not null)
        {
            return byValues;
        }
        var byDropdown = ReverseListMapping(value, payload["dropdown"]);
        if (byDropdown is not null)
        {
            return byDropdown;
        }
        if (payload["value_mapping"] is JsonObject mapping)
        {
            var text = value?.ToString() ?? string.Empty;
            foreach (var pair in mapping)
            {
                if (text == NodeText(pair.Value))
                {
                    return Conversions.ToInt(pair.Key);
                }
            }
        }
        return null;
    }

    private static object? ListMappingValue(object? rawValue, JsonNode? options)
    {
        if (options is not JsonArray array)
        {
            return null;
        }
        long index;
        try
        {
            index = Conversions.ToInt(rawValue);
        }
        catch (Exception)
        {
            return null;
        }
        if (index < 0 || index >= array.Count)
        {
            return null;
        }
        return NodeText(array[(int)index]);
    }

    private static int? ReverseListMapping(object? value, JsonNode? options)
    {
        if (options is not JsonArray array)
        {
            return null;
        }
        var text = value?.ToString() ?? string.Empty;
        for (int index = 0; index < array.Count; index++)
        {
            if (text == NodeText(array[index]))
            {
                return index;
            }
        }
        return null;
    }
}
