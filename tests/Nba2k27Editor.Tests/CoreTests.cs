using System.Text.Json.Nodes;
using Nba2k27Editor.Core.Editing;
using Nba2k27Editor.Core.Gs;
using Nba2k27Editor.Core.Memory;
using Nba2k27Editor.Core.Model;

namespace Nba2k27Editor.Tests;

/// <summary>Offline checks for the byte level and conversion layers.</summary>
internal static class MemoryAndConversionTests
{
    public static void Run(Harness harness)
    {
        SimulatedMemoryStore(harness);
        Text(harness);
        ConversionMath(harness);
    }

    private static void SimulatedMemoryStore(Harness harness)
    {
        var memory = new SimulatedMemory(0x2000) { BaseAddress = 0x400000 };
        harness.Equal("simulated: pointer size", 8, memory.PointerSize);
        harness.Equal("simulated: attached", true, memory.Attached);
        harness.Equal("simulated: reports simulated", true, memory.Simulated);

        memory.WriteBytes(0x100, new byte[] { 1, 2, 3, 4 });
        harness.Sequence("simulated: write then read", new byte[] { 1, 2, 3, 4 }, memory.ReadBytes(0x100, 4));
        harness.Equal("simulated: write counter", 1, memory.Writes);

        memory.WriteUint32(0x200, 0xDEADBEEF);
        harness.Sequence("simulated: uint32 little endian",
            new byte[] { 0xEF, 0xBE, 0xAD, 0xDE }, memory.ReadBytes(0x200, 4));

        harness.Throws("simulated: read past end", () => memory.ReadBytes(0x1FFE, 4));
        harness.Throws("simulated: negative address", () => memory.ReadBytes(-1, 4));
        harness.Throws("simulated: write past end", () => memory.WriteBytes(0x1FFF, new byte[4]));

        // An injected failure must leave the partially written bytes visible, mirroring the game path.
        var failing = new SimulatedMemory(0x100) { FailOnWrite = 1 };
        harness.Throws("simulated: injected write failure", () => failing.WriteBytes(0x10, new byte[] { 9, 9 }));
        harness.Sequence("simulated: partial bytes survive", new byte[] { 9, 9 }, failing.ReadBytes(0x10, 2));

        var observed = new SimulatedMemory(0x100);
        long seenAddress = -1;
        observed.OnWrite = (address, _) => seenAddress = address;
        observed.WriteBytes(0x40, new byte[] { 7 });
        harness.Equal("simulated: write callback address", 0x40L, seenAddress);

        var snapshot = observed.Snapshot();
        harness.Equal("simulated: snapshot length", 0x100, snapshot.Length);
        harness.Equal("simulated: snapshot content", (byte)7, snapshot[0x40]);
    }

    private static void Text(Harness harness)
    {
        var memory = new SimulatedMemory(0x400);
        memory.WriteWStringFixed(0x20, "Brian Skinner", 40);
        harness.Equal("text: utf16 round trip", "Brian Skinner", memory.ReadWString(0x20, 40));
        // Trailing bytes must be zero so the reader stops at the terminator.
        harness.Sequence("text: utf16 padding", new byte[2], memory.ReadBytes(0x20 + 26, 2));

        memory.WriteWStringFixed(0x100, "abcdef", 3);
        harness.Equal("text: utf16 truncates to max chars", "abc", memory.ReadWString(0x100, 3));

        memory.WriteAsciiFixed(0x180, "NBA2K27", 16);
        harness.Equal("text: ascii round trip", "NBA2K27", memory.ReadAscii(0x180, 16));

        memory.WriteAsciiFixed(0x1C0, "0123456789", 4);
        harness.Equal("text: ascii truncates", "0123", memory.ReadAscii(0x1C0, 4));

        memory.WriteWStringFixed(0x200, "中文名", 8);
        harness.Equal("text: utf16 keeps non-ascii", "中文名", memory.ReadWString(0x200, 8));
    }

    private static void ConversionMath(Harness harness)
    {
        harness.Equal("conversions: int from string", 47, Conversions.ToInt("47"));
        harness.Equal("conversions: int from hex", 0x21C, Conversions.ToInt("0x21C"));
        harness.Equal("conversions: int from double", 47, Conversions.ToInt(47.9));
        harness.Equal("conversions: int from null", 0, Conversions.ToInt(null));

        // 64-bit addresses must not be narrowed through ToInt.
        harness.Equal("conversions: long keeps high half", 0x21C12F52948L, Conversions.ToLong("0x21C12F52948"));
        harness.Equal("conversions: long from long", 140701805117440L, Conversions.ToLong(140701805117440L));
        harness.Equal("conversions: int narrows deliberately",
            unchecked((int)0x21C12F52948L), Conversions.ToInt("0x21C12F52948"));

        harness.Equal("conversions: double from string", 1.5, Conversions.ToDouble("1.5"));
        harness.Equal("conversions: double fallback", 2.0, Conversions.ToDouble("bad", 2.0));

        harness.Equal("conversions: rating round trip", 99, Conversions.ConvertRawToRating(
            Conversions.ConvertRatingToRaw(99, 8), 8));
        // Live incident 2026-11-08: Eric Snow 3POINT raw 56 shows 44, and 44 encodes back to 57.
        // PlayerSession therefore keeps the stored raw when the submitted display value is unchanged.
        harness.Equal("conversions: lossy rating raw 56 shows 44", 44, Conversions.ConvertRawToRating(56, 8));
        harness.Equal("conversions: lossy rating 44 encodes to 57", 57, Conversions.ConvertRatingToRaw(44, 8));
        harness.Equal("conversions: potential round trip", 85, Conversions.ConvertRawToPotential(
            Conversions.ConvertPotentialToRaw(85, 8), 8));
        harness.Equal("conversions: pounds to kilograms", 45.3592,
            Math.Round(Conversions.ConvertPoundsToKilograms(100), 4));
        harness.Equal("conversions: kilograms to pounds", 100.0,
            Math.Round(Conversions.ConvertKilogramsToPounds(45.3592), 2));
    }
}

/// <summary>Offline checks for the field codec: widths, bitfields, pointers and display conversion.</summary>
internal static class FieldCodecTests
{
    // Copied verbatim from Data/offsets_players.json (2K27), so the fixtures match real metadata.
    private const string ThreePointPayload =
        """{"address":1088,"bit_length":8,"bit_offset":24,"hex":"0x440","length":8,"startBit":24}""";
    private const string FaceTypePayload =
        """{"address":284,"bit_length":2,"bit_offset":3,"hex":"0x11C","length":2,"startBit":3,"type":"bitfield"}""";

    public static void Run(Harness harness)
    {
        Widths(harness);
        Pointers(harness);
        ParentAddresses(harness);
        Bitfields(harness);
        RoundTrip(harness);
        AddressDropdown(harness);
        DropdownMergeTests.Run(harness);
    }

    private static void Widths(Harness harness)
    {
        harness.Equal("codec: byte width", 1, FieldIo.NumericWidth(Harness.Payload("""{"type":"byte"}""")));
        harness.Equal("codec: ubyte width", 1, FieldIo.NumericWidth(Harness.Payload("""{"type":"ubyte"}""")));
        harness.Equal("codec: ushort width", 2, FieldIo.NumericWidth(Harness.Payload("""{"type":"ushort"}""")));
        harness.Equal("codec: uint width", 4, FieldIo.NumericWidth(Harness.Payload("""{"type":"uint"}""")));
        harness.Equal("codec: uint64 width", 8, FieldIo.NumericWidth(Harness.Payload("""{"type":"uint64"}""")));
        harness.Equal("codec: ulonglong width", 8, FieldIo.NumericWidth(Harness.Payload("""{"type":"ulonglong"}""")));
        harness.Equal("codec: pointer width", 8, FieldIo.NumericWidth(Harness.Payload("""{"type":"pointer"}""")));
        harness.Equal("codec: address width", 8, FieldIo.NumericWidth(Harness.Payload("""{"type":"address"}""")));
        harness.Equal("codec: team dropdown width", 8,
            FieldIo.NumericWidth(Harness.Payload("""{"type":"team_address_dropdown"}""")));
        harness.Equal("codec: type key is case folded", "uint",
            FieldIo.TypeKey(Harness.Payload("""{"type":"UInt"}""")));

        // Real 2K27 metadata omits "type" for authored bit windows; the type is inferred.
        var threePoint = Harness.Payload(ThreePointPayload);
        harness.Equal("codec: real 3POINT infers bitfield", "bitfield", FieldIo.TypeKey(threePoint));
        harness.Equal("codec: real 3POINT is implemented", true, FieldIo.ImplementedPayload(threePoint));
        harness.Equal("codec: real 3POINT window offset", 24, FieldIo.BitWindow(threePoint).BitOffset);
        harness.Equal("codec: real 3POINT window length", 8, FieldIo.BitWindow(threePoint).BitLength);
        // The bit window spans bytes 3..3 of the word, so I/O must use four bytes, not one.
        harness.Equal("codec: real 3POINT window width", 4, FieldIo.BitWindow(threePoint).Width);

        // Float payloads carry no length, so they bypass NumericWidth exactly like the Python port.
        harness.Throws("codec: float payload bypasses NumericWidth",
            () => FieldIo.NumericWidth(Harness.Payload("""{"address":24,"hex":"0x18","type":"float"}""")));
    }

    private static void Pointers(Harness harness)
    {
        var memory = new SimulatedMemory(0x1000);
        long target = 0x21C12F52948L;
        memory.WriteBytes(0x100, BitConverter.GetBytes(target));
        harness.Equal("codec: pointer read keeps 64 bits", target, FieldIo.ReadPointerValue(memory, 0x100));

        // An 8-byte authored field must write the full value, not a narrowed int.
        var payload = Harness.Payload("""{"address":0,"type":"uint64"}""");
        FieldIo.WriteAuthoredValue(memory, 0x200, payload, target);
        harness.Equal("codec: uint64 write keeps high half", target,
            BitConverter.ToInt64(memory.ReadBytes(0x200, 8)));
        harness.Equal("codec: uint64 read back", target, FieldIo.ReadAuthoredValue(memory, 0x200, payload));

        var pointerPayload = Harness.Payload("""{"address":0,"type":"pointer"}""");
        FieldIo.WriteAuthoredValue(memory, 0x280, pointerPayload, "0x21C12F52948");
        harness.Equal("codec: hex pointer write", target, BitConverter.ToInt64(memory.ReadBytes(0x280, 8)));
    }

    private static void ParentAddresses(Harness harness)
    {
        var memory = new SimulatedMemory(0x2000);
        var schema = new Schema(memory, new Offsets("NBA2K27.exe"), "NBA2K27.exe");
        var head = schema.FieldByName("Players", "ISSCANNEDHEAD")!;
        var payload = schema.FieldVersionPayload(head.Field);
        var parent = schema.ParentPayload("Players", payload)!;
        harness.Equal("parent: head pointer metadata", 112, FieldIo.FieldOffset(parent));
        memory.Load(0x100 + 112, BitConverter.GetBytes(0x800L));
        harness.Equal("parent: field resolves relative to loaded head", 0x834L,
            FieldIo.FieldAddress(memory, 0x100, payload, parent));
        memory.Load(0x834, BitConverter.GetBytes(0x80000000u));
        var read = schema.ReadField(head, new RecordListItem("Players", 0, 0x100, "Test"));
        harness.Equal("parent: scanned head bit decodes", 1L, (long)read.RawValue);
        harness.Equal("parent: scanned head status", "scanned",
            PlayerAppearance.ReadHeadScan(schema, new RecordListItem("Players", 0, 0x100, "Test")).Status);
        memory.Load(0x834, BitConverter.GetBytes(0u));
        harness.Equal("parent: non-scanned head status", "not_scanned",
            PlayerAppearance.ReadHeadScan(schema, new RecordListItem("Players", 0, 0x100, "Test")).Status);
        memory.Load(0x100 + 112, new byte[8]);
        var missing = PlayerAppearance.ReadHeadScan(schema, new RecordListItem("Players", 0, 0x100, "Test"));
        harness.Equal("parent: missing head status is unknown", "unavailable", missing.Status);
        harness.Equal("parent: missing head flag is not zero", (long?)null, missing.Flag);
        harness.Check("parent: missing head includes reason", missing.Error?.Contains("Head Pointer") == true);
        memory.Load(0x100 + 112, BitConverter.GetBytes(0x800L));
        harness.Equal("parent: valid resolution is read-only", 0, memory.Writes);

        var skin = schema.FieldByName("Players", "LORESFACECAPTUREFILENAME")!;
        var skinPayload = schema.FieldVersionPayload(skin.Field);
        var skinParent = schema.ParentPayload("Players", skinPayload)!;
        int reads = memory.Reads;
        string message = harness.Throws("parent: absent skin stops before field I/O",
            () => schema.ReadField(skin, new RecordListItem("Players", 0, 0x100, "Test")));
        harness.Check("parent: absent object has specific error",
            message.Contains("Parent object is not loaded: SkinData Pointer", StringComparison.Ordinal));
        harness.Equal("parent: null resolution only reads the pointer", reads + 1, memory.Reads);
        harness.Equal("parent: absent object never writes", 0, memory.Writes);

        memory.Load(0x100 + 128, BitConverter.GetBytes(0xA00L));
        memory.Load(0xA00 + 412, System.Text.Encoding.ASCII.GetBytes("face.bin\0"));
        var skinRead = schema.ReadField(skin, new RecordListItem("Players", 0, 0x100, "Test"));
        harness.Equal("parent: present skin string resolves", "face.bin", (string)skinRead.RawValue);
        harness.Equal("parent: present skin address", 0xB9CL, skinRead.Address);

        var direct = Harness.Payload("""{"address":4,"type":"uint"}""");
        harness.Equal("parent: direct field unchanged", 0x104L, FieldIo.FieldAddress(memory, 0x100, direct));
        var inline = Harness.Payload("""{"address":16,"type":"binary","length":8}""");
        harness.Equal("parent: inline parent unchanged", 0x114L,
            FieldIo.FieldAddress(memory, 0x100, direct, inline));
        var deref = Harness.Payload(
            """{"address":4,"type":"uint","requiresDereference":true,"dereferenceAddress":32}""");
        harness.Throws("parent: null dereference stops", () => FieldIo.FieldAddress(memory, 0x100, deref));
        memory.Load(0x120, BitConverter.GetBytes(0xC00L));
        harness.Equal("parent: valid dereference unchanged", 0xC04L,
            FieldIo.FieldAddress(memory, 0x100, deref));
    }

    private static void Bitfields(Harness harness)
    {
        var threePoint = Harness.Payload(ThreePointPayload);
        var faceType = Harness.Payload(FaceTypePayload);
        harness.Equal("codec: bitfield inferred from metadata", true, FieldIo.UsesBitfieldIo(threePoint));
        harness.Equal("codec: declared bitfield type", true, FieldIo.UsesBitfieldIo(faceType));

        var memory = new SimulatedMemory(0x100);
        // FACETYPE occupies bits 3..4 of one byte; neighbouring bits must survive a write.
        memory.WriteBytes(0, new byte[] { 0x1F });
        harness.Equal("codec: bitfield read", 3, Convert.ToInt32(FieldIo.ReadAuthoredValue(memory, 0, faceType)));
        FieldIo.WriteAuthoredValue(memory, 0, faceType, 0);
        harness.Sequence("codec: bitfield write preserves neighbours", new byte[] { 0x07 }, memory.ReadBytes(0, 1));

        // 3POINT is byte 3 of the four-byte word at 0x440.
        memory.WriteBytes(0x10, new byte[] { 0xFF, 0xFF, 0xFF, 0x00 });
        harness.Equal("codec: 3POINT reads only its byte", 0,
            Convert.ToInt32(FieldIo.ReadAuthoredValue(memory, 0x10, threePoint)));
        FieldIo.WriteAuthoredValue(memory, 0x10, threePoint, 63);
        harness.Sequence("codec: 3POINT write keeps the low bytes",
            new byte[] { 0xFF, 0xFF, 0xFF, 0x3F }, memory.ReadBytes(0x10, 4));
        harness.Equal("codec: 3POINT write reads back", 63,
            Convert.ToInt32(FieldIo.ReadAuthoredValue(memory, 0x10, threePoint)));
    }

    private static void RoundTrip(Harness harness)
    {
        // Real Attributes/Offense/3POINT payload, and the live-verified 46/47 display pair.
        var threePoint = Harness.Payload(ThreePointPayload);
        var field = Harness.Payload("""{"normalized_name":"3POINT","display_name":"3pt Shot"}""");
        var memory = new SimulatedMemory(0x100);

        FieldIo.WriteAuthoredValue(memory, 0, threePoint, 63);
        harness.Equal("codec: display for raw 63", 46,
            Convert.ToInt32(FieldIo.RawToDisplayValue("Attributes", field, threePoint,
                FieldIo.ReadAuthoredValue(memory, 0, threePoint))));
        FieldIo.WriteAuthoredValue(memory, 0, threePoint, 66);
        harness.Equal("codec: display for raw 66", 47,
            Convert.ToInt32(FieldIo.RawToDisplayValue("Attributes", field, threePoint,
                FieldIo.ReadAuthoredValue(memory, 0, threePoint))));

        // Display -> raw must return the exact stored byte so undo restores the original value.
        harness.Equal("codec: raw for display 46", 63L,
            Convert.ToInt64(FieldIo.DisplayToRawValue("Attributes", field, threePoint, 46)));
        harness.Equal("codec: raw for display 47", 66L,
            Convert.ToInt64(FieldIo.DisplayToRawValue("Attributes", field, threePoint, 47)));
    }

    private static void AddressDropdown(Harness harness)
    {
        harness.Equal("codec: address dropdown count", true, FieldIo.AddressDropdownTypes.Count >= 1);
        foreach (var pair in FieldIo.AddressDropdownTypes)
        {
            harness.Equal($"codec: dropdown {pair.Key} is 8 bytes", 8,
                FieldIo.NumericWidth(Harness.Payload($$"""{"type":"{{pair.Key}}"}""")));
        }
    }
}

/// <summary>Offline checks for the general GS layer: ids, encoding and block decoding.</summary>
internal static class GeneralGsTests
{
    public static void Run(Harness harness)
    {
        Ids(harness);
        Encoding(harness);
        Decoding(harness);
        Epoch(harness);
    }

    private static void Ids(Harness harness)
    {
        harness.Equal("gs ids: field count", 70, GsFieldIds.Ids.Count);
        harness.Equal("gs ids: sides", 2, GsFieldIds.Sides.Length);
        harness.Equal("gs ids: inside_shot", 27, GsFieldIds.Ids["inside_shot"]);
        harness.Equal("gs ids: three_point", 30, GsFieldIds.Ids["three_point"]);
        harness.Equal("gs ids: speed", 4, GsFieldIds.Ids["speed"]);
        harness.Equal("gs ids: agility", 13, GsFieldIds.Ids["agility"]);
        harness.Equal("gs ids: acceleration_min", 149, GsFieldIds.Ids["acceleration_min"]);

        harness.Equal("gs ids: user inside_shot offset", 216, GsFieldIds.FieldOffset("user.inside_shot"));
        harness.Equal("gs ids: cpu inside_shot offset", 220, GsFieldIds.FieldOffset("cpu.inside_shot"));
        harness.Equal("gs ids: cpu speed offset", 36, GsFieldIds.FieldOffset("cpu.speed"));
        harness.Throws("gs ids: unknown field rejected", () => GsFieldIds.FieldOffset("user.nope"));
    }

    private static void Encoding(Harness harness)
    {
        // The live-verified encodings for 47 and 48 percent.
        harness.Sequence("gs encode: 47 percent", new byte[] { 0xD7, 0xA3, 0xF0, 0x3E },
            GsFieldIds.PercentBytes(47));
        harness.Sequence("gs encode: 48 percent", new byte[] { 0x8F, 0xC2, 0xF5, 0x3E },
            GsFieldIds.PercentBytes(48));
        harness.Equal("gs encode: 0 percent", 0f, BitConverter.ToSingle(GsFieldIds.PercentBytes(0), 0));
        harness.Equal("gs encode: 100 percent", 1f, BitConverter.ToSingle(GsFieldIds.PercentBytes(100), 0));

        harness.Equal("gs encode: check raw 47", 47, GsFieldIds.CheckRaw(GsFieldIds.PercentBytes(47)));
        harness.Equal("gs encode: check raw 0", 0, GsFieldIds.CheckRaw(GsFieldIds.PercentBytes(0)));
        harness.Equal("gs encode: check raw 100", 100, GsFieldIds.CheckRaw(GsFieldIds.PercentBytes(100)));

        // Out of range and non-integer percentages must be refused.
        harness.Throws("gs encode: rejects -1", () => GsFieldIds.PercentBytes(-1));
        harness.Throws("gs encode: rejects 101", () => GsFieldIds.PercentBytes(101));
        harness.Throws("gs encode: rejects uninitialized bytes",
            () => GsFieldIds.CheckRaw(BitConverter.GetBytes(float.NaN)));
        harness.Throws("gs encode: rejects 1.5",
            () => GsFieldIds.CheckRaw(BitConverter.GetBytes(1.5f)));
    }

    private static void Decoding(Harness harness)
    {
        // Build one 1296-byte block with every field set to its id modulo 101.
        var block = new byte[1296];
        foreach (var pair in GsFieldIds.Ids)
        {
            foreach (string side in GsFieldIds.Sides)
            {
                int value = pair.Value % 101;
                GsFieldIds.PercentBytes(value).CopyTo(block, GsFieldIds.FieldOffset($"{side}.{pair.Key}"));
            }
        }
        var decoded = GsFieldIds.DecodeBlock(block);
        harness.Equal("gs decode: value count", 140, decoded.Count);
        bool allMatch = true;
        foreach (var pair in GsFieldIds.Ids)
        {
            foreach (string side in GsFieldIds.Sides)
            {
                allMatch &= decoded[$"{side}.{pair.Key}"] == pair.Value % 101;
            }
        }
        harness.Check("gs decode: every field matches", allMatch);

        harness.Throws("gs decode: rejects short block", () => GsFieldIds.DecodeBlock(new byte[64]));
        var zero = new byte[1296];
        harness.Equal("gs decode: zero block decodes to zeros", 0, GsFieldIds.DecodeBlock(zero)["user.inside_shot"]);
    }

    private static void Epoch(Harness harness)
    {
        var epoch = new GsEpoch(34112, DateTime.UnixEpoch.AddSeconds(1792920310.5834556).Ticks,
            140701805117440L, NativeGsConstants.BuildSha256);
        harness.Equal("gs epoch: unix seconds", 1792920310.5834556, epoch.CreatedUnix, 0.001);
        var array = epoch.ToJsonArray();
        harness.Equal("gs epoch: json length", 4, array.Count);
        harness.Equal("gs epoch: json pid", 34112L, array[0]!.GetValue<long>());
        harness.Equal("gs epoch: json base", 140701805117440L, array[2]!.GetValue<long>());
        harness.Equal("gs epoch: json digest", NativeGsConstants.BuildSha256, array[3]!.GetValue<string>());
    }
}

/// <summary>Offline checks for the legacy five-pair GS layer.</summary>
internal static class LegacyGsTests
{
    public static void Run(Harness harness)
    {
        Decode(harness);
        Encode(harness);
        Snapshot(harness);
    }

    /// <summary>Window with every mapped field at the given percentages; sentinels stay zero.</summary>
    private static byte[] Block(int user = 50, int cpu = 50)
    {
        var raw = new byte[NativeMenuGs.BlockBytes];
        foreach (string field in NativeMenuGs.Fields)
        {
            int id = GsFieldIds.Ids[field];
            BitConverter.GetBytes(user / 100f).CopyTo(raw, id * 8);
            BitConverter.GetBytes(cpu / 100f).CopyTo(raw, id * 8 + 4);
        }
        return raw;
    }

    /// <summary>Set one field by its general GS id.</summary>
    private static void Poke(byte[] raw, int generalId, int user, int cpu)
    {
        BitConverter.GetBytes(user / 100f).CopyTo(raw, generalId * 8);
        BitConverter.GetBytes(cpu / 100f).CopyTo(raw, generalId * 8 + 4);
    }

    private static void Decode(Harness harness)
    {
        var raw = Block();
        Poke(raw, 27, 47, 45);
        Poke(raw, 30, 100, 57);
        var values = NativeMenuGs.DecodeBlock(raw);
        harness.Equal("legacy decode: field count", 70, NativeMenuGs.Fields.Length);
        harness.Equal("legacy decode: value count", 140, values.Count);
        harness.Equal("legacy decode: user.inside_shot", 47, values["user.inside_shot"]);
        harness.Equal("legacy decode: cpu.inside_shot", 45, values["cpu.inside_shot"]);
        harness.Equal("legacy decode: user.three_point", 100, values["user.three_point"]);
        harness.Equal("legacy decode: cpu.three_point", 57, values["cpu.three_point"]);
        harness.Equal("legacy decode: key order", "user.stealing", NativeMenuGs.Keys[0]);
        harness.Equal("legacy decode: last key", "cpu.acceleration_min", NativeMenuGs.Keys[^1]);

        harness.Throws("legacy decode: rejects 39 bytes", () => NativeMenuGs.DecodeBlock(new byte[39]));
        harness.Throws("legacy decode: rejects 41 bytes", () => NativeMenuGs.DecodeBlock(new byte[41]));
        var nan = Block();
        BitConverter.GetBytes(float.NaN).CopyTo(nan, 27 * 8);
        harness.Throws("legacy decode: rejects NaN", () => NativeMenuGs.DecodeBlock(nan));
        var over = Block();
        BitConverter.GetBytes(1.5f).CopyTo(over, 27 * 8);
        harness.Throws("legacy decode: rejects above 1.0", () => NativeMenuGs.DecodeBlock(over));
        var fractional = Block();
        BitConverter.GetBytes(0.555f).CopyTo(fractional, 27 * 8);
        harness.Throws("legacy decode: rejects non-integer percent", () => NativeMenuGs.DecodeBlock(fractional));
    }

    private static void Encode(Harness harness)
    {
        harness.Sequence("legacy encode: 56", BitConverter.GetBytes(0.56f), NativeMenuGs.EncodeTestedValue(56));
        harness.Sequence("legacy encode: 57", BitConverter.GetBytes(0.57f), NativeMenuGs.EncodeTestedValue(57));
        harness.Throws("legacy encode: rejects 55",
            () => NativeMenuGs.EncodeTestedValue(55),
            "Only the menu-verified CPU three-point values 56 and 57 are enabled");
        harness.Throws("legacy encode: rejects 100",
            () => NativeMenuGs.EncodeTestedValue(100),
            "Only the menu-verified CPU three-point values 56 and 57 are enabled");
    }

    private static void Snapshot(Harness harness)
    {
        var epoch = new LegacyGsEpoch(34112, DateTime.UnixEpoch.AddSeconds(1792920310.5834556).Ticks,
            140701805117440L, NativeGsConstants.BuildSha256);

        // Identical blocks with cpu.three_point at a tested value are writable.
        var same = Block();
        Poke(same, 30, 100, 57);
        var writable = new LegacyGsSnapshot(epoch, same, (byte[])same.Clone());
        harness.Equal("legacy snapshot: writable", true, writable.Writable);
        harness.Equal("legacy snapshot: status", "memory_values_menu_unverified", writable.ObservationStatus);
        harness.Check("legacy snapshot: note mentions menu",
            writable.ObservationNote.Contains("not been verified"), writable.ObservationNote);

        // All-zero blocks are reported separately.
        var zeros = new byte[NativeMenuGs.BlockBytes];
        var zeroSnapshot = new LegacyGsSnapshot(epoch, zeros, (byte[])zeros.Clone());
        harness.Equal("legacy snapshot: all zero status", "all_zero_menu_unverified",
            zeroSnapshot.ObservationStatus);
        harness.Equal("legacy snapshot: all zero is not writable", false, zeroSnapshot.Writable);
        harness.Equal("legacy snapshot: all zero arbitrary field is not writable", false,
            zeroSnapshot.WritableField("user.inside_shot"));
        harness.Equal("legacy snapshot: initialized arbitrary field is writable", true,
            writable.WritableField("user.inside_shot"));

        // A sentinel byte difference means the layout is not the expected one.
        var other = (byte[])same.Clone();
        BitConverter.GetBytes(0.5f).CopyTo(other, 0);
        harness.Equal("legacy snapshot: other byte differs -> not writable", false,
            new LegacyGsSnapshot(epoch, same, other).Writable);

        // An untested cpu.three_point value makes the snapshot unwritable.
        var untested = Block();
        Poke(untested, 30, 100, 55);
        harness.Equal("legacy snapshot: untested value -> not writable", false,
            new LegacyGsSnapshot(epoch, untested, (byte[])untested.Clone()).Writable);

        harness.Equal("legacy epoch: unix seconds", 1792920310.5834556, epoch.CreatedUnix, 0.001);
        var array = epoch.ToJsonArray();
        harness.Equal("legacy epoch: json length", 4, array.Count);
        harness.Equal("legacy epoch: json digest", NativeGsConstants.BuildSha256, array[3]!.GetValue<string>());
    }
}

/// <summary>Offline checks for the persisted base-root cache.</summary>
internal static class BaseRootCacheTests
{
    public static void Run(Harness harness)
    {
        string directory = Path.Combine(Path.GetTempPath(), "nba2k27-cache-" + Guid.NewGuid().ToString("N"));
        try
        {
            string path = Path.Combine(directory, "base-roots.json");
            var cache = new BaseRootCache(path);
            cache.Save(111, 0x7ff700001000, new Dictionary<string, long>(StringComparer.Ordinal)
            {
                ["GameDataStore"] = 161775288,
                ["DraftClass"] = 163077560,
            });
            harness.Equal("base cache: no warning after first save", string.Empty, cache.LastWarning);

            // A second process must be added without dropping the first entry; this is the path
            // that used to throw "The node already has a parent".
            cache.Save(222, 0x7ff700002000, new Dictionary<string, long>(StringComparer.Ordinal)
            {
                ["GameDataStore"] = 161775300,
            });
            harness.Equal("base cache: no warning after second save", string.Empty, cache.LastWarning);

            harness.Equal("base cache: keeps the earlier process", true,
                cache.TryLoad(111, 0x7ff700001000, out Dictionary<string, long> rootsFirst));
            harness.Equal("base cache: earlier root value", 161775288L, rootsFirst["GameDataStore"]);
            harness.Equal("base cache: keeps the later process", true,
                cache.TryLoad(222, 0x7ff700002000, out Dictionary<string, long> rootsSecond));
            harness.Equal("base cache: later root value", 161775300L, rootsSecond["GameDataStore"]);

            harness.Equal("base cache: unknown pid misses", false,
                cache.TryLoad(999, 0x7ff700001000, out _));
            harness.Equal("base cache: wrong base misses", false,
                cache.TryLoad(111, 0x7ff700009000, out _));

            // Corrupt content must degrade to a miss instead of throwing.
            File.WriteAllText(path, "{ not json");
            harness.Equal("base cache: corrupt file misses", false,
                cache.TryLoad(111, 0x7ff700001000, out _));
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
