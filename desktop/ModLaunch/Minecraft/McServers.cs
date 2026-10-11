using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModLaunch.Core;

namespace ModLaunch.Minecraft;

/// <summary>Сервер из списка «Сетевая игра» сборки (servers.dat).</summary>
public sealed record McServer(string Name, string Address, string? Icon);

/// <summary>Ответ сервера на «пинг»: кто онлайн, что пишет в описании, задержка.</summary>
public sealed record McServerStatus(bool Online, int Players, int Max, string Motd, string Version, int Ms, string? Favicon);

/// <summary>
/// 9.4: серверы сборки — как вкладка «Servers» в Modrinth App: список из servers.dat (тот же, что
/// в «Сетевой игре»), онлайн и задержка по протоколу Minecraft, добавить и убрать сервер.
/// </summary>
public static partial class McServers
{
    public static string FileOf(McInstance i) => Path.Combine(i.Dir, "servers.dat");

    public static List<McServer> List(McInstance i)
    {
        var file = FileOf(i);
        if (!File.Exists(file)) return [];
        try
        {
            var root = Nbt.Read(File.ReadAllBytes(file));
            if (Nbt.Child(root, "servers") is not { Type: Nbt.TList } list) return [];
            return ((Nbt.ListValue)list.Value).Items
                .Where(t => t.Type == Nbt.TCompound)
                .Select(t => new McServer(Nbt.Str(t, "name") ?? "", Nbt.Str(t, "ip") ?? "", Nbt.Str(t, "icon")))
                .Where(s => s.Address != "").ToList();
        }
        catch { return []; }
    }

    /// <summary>Добавить сервер в начало списка (так делает и сама игра). Старый файл — в servers.dat_old, как у игры.</summary>
    public static void Add(McInstance i, string name, string address) => Change(i, items =>
        items.Insert(0, new Nbt.Tag(Nbt.TCompound, new List<(string, Nbt.Tag)>
        {
            ("name", new Nbt.Tag(Nbt.TString, name.Trim() == "" ? address.Trim() : name.Trim())),
            ("ip", new Nbt.Tag(Nbt.TString, address.Trim())),
        })));

    public static void Remove(McInstance i, McServer s) => Change(i, items =>
    {
        var at = items.FindIndex(t => Nbt.Str(t, "ip") == s.Address && (Nbt.Str(t, "name") ?? "") == s.Name);
        if (at >= 0) items.RemoveAt(at);
    });

    static void Change(McInstance i, Action<List<Nbt.Tag>> edit)
    {
        var file = FileOf(i);
        Nbt.Tag root;
        if (File.Exists(file))
        {
            root = Nbt.Read(File.ReadAllBytes(file));
            try { File.Copy(file, file + "_old", true); } catch { }
        }
        else root = new Nbt.Tag(Nbt.TCompound, new List<(string, Nbt.Tag)>());
        var fields = (List<(string Name, Nbt.Tag Tag)>)root.Value;
        var at = fields.FindIndex(f => f.Name == "servers");
        if (at < 0) { fields.Add(("servers", new Nbt.Tag(Nbt.TList, new Nbt.ListValue(Nbt.TCompound, [])))); at = fields.Count - 1; }
        var list = (Nbt.ListValue)fields[at].Tag.Value;
        var items = list.Items;
        edit(items);
        fields[at] = ("servers", new Nbt.Tag(Nbt.TList, new Nbt.ListValue(items.Count == 0 ? Nbt.TEnd : Nbt.TCompound, items)));
        Directory.CreateDirectory(i.Dir);
        File.WriteAllBytes(file + ".tmp", Nbt.Write(root));
        File.Move(file + ".tmp", file, true);
    }

    // ---------------------------------------------------------------- пинг (Server List Ping)

    /// <summary>«play.example.net:25566» → хост и порт (по умолчанию 25565); IPv6 — в [скобках].</summary>
    public static (string Host, int Port) Parse(string address)
    {
        var a = address.Trim();
        if (a.StartsWith('['))
        {
            var close = a.IndexOf(']');
            var host = close > 0 ? a[1..close] : a.Trim('[', ']');
            var rest = close > 0 ? a[(close + 1)..] : "";
            return (host, rest.StartsWith(':') && int.TryParse(rest[1..], out var p6) ? p6 : 25565);
        }
        var colon = a.LastIndexOf(':');
        if (colon > 0 && a.IndexOf(':') == colon && int.TryParse(a[(colon + 1)..], out var port)) return (a[..colon], port);
        return (a, 25565);
    }

    /// <summary>Спросить сервер о себе, как это делает список серверов в игре.</summary>
    public static async Task<McServerStatus> Ping(string address, CancellationToken ct = default)
    {
        var (host, port) = Parse(address);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(6));
        try
        {
            if (Offline.On) throw new OfflineException();
            using var tcp = new TcpClient { NoDelay = true };
            var watch = Stopwatch.StartNew();
            await tcp.ConnectAsync(host, port, cts.Token);
            var connectMs = (int)watch.ElapsedMilliseconds;
            var stream = tcp.GetStream();
            // Рукопожатие: протокол -1 («какой есть»), адрес, порт, следующее состояние 1 — статус.
            var hs = new List<byte>();
            VarInt(hs, 0x00); VarInt(hs, -1); Str(hs, host);
            hs.Add((byte)(port >> 8)); hs.Add((byte)port);
            VarInt(hs, 1);
            await Send(stream, hs, cts.Token);
            await Send(stream, [0x00], cts.Token);
            var packet = await ReadPacket(stream, cts.Token);
            var at = 0;
            if (ReadVarInt(packet, ref at) != 0x00) throw new IOException("bad status packet");
            var len = ReadVarInt(packet, ref at);
            var json = Encoding.UTF8.GetString(packet, at, len);
            var ms = connectMs;
            // Пинг-понг — честная задержка; не ответил — остаётся время соединения.
            try
            {
                var ping = new List<byte>();
                VarInt(ping, 0x01);
                var payload = new byte[8];
                BinaryPrimitives.WriteInt64BigEndian(payload, Environment.TickCount64);
                ping.AddRange(payload);
                watch.Restart();
                await Send(stream, ping, cts.Token);
                await ReadPacket(stream, cts.Token);
                ms = (int)watch.ElapsedMilliseconds;
            }
            catch { }
            return FromJson(json, ms);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return new McServerStatus(false, 0, 0, "", "", -1, null);
        }
    }

    public static McServerStatus FromJson(string json, int ms)
    {
        var j = JsonNode.Parse(json);
        return new McServerStatus(true, (int)j?["players"].Long("online"), (int)j?["players"].Long("max"),
            Plain(j?["description"]), Strip(j?["version"].Str("name") ?? ""), ms, j.Str("favicon"));
    }

    /// <summary>Описание сервера — строка или «чат-компонент» с text/extra; цветовые коды §x убираем.</summary>
    public static string Plain(JsonNode? node)
    {
        var sb = new StringBuilder();
        void Walk(JsonNode? n)
        {
            switch (n)
            {
                case JsonValue v when v.TryGetValue<string>(out var s): sb.Append(s); break;
                case JsonObject o:
                    if (o.Str("text") is { } t) sb.Append(t);
                    if (o["extra"] is JsonArray extra) foreach (var e in extra) Walk(e);
                    break;
                case JsonArray a: foreach (var e in a) Walk(e); break;
            }
        }
        Walk(node);
        return Strip(sb.ToString()).Trim();
    }

    [GeneratedRegex("§.")] private static partial Regex Codes();
    static string Strip(string s) => Codes().Replace(s, "");

    static async Task Send(NetworkStream stream, IList<byte> body, CancellationToken ct)
    {
        var frame = new List<byte>();
        VarInt(frame, body.Count);
        frame.AddRange(body);
        await stream.WriteAsync(frame.ToArray(), ct);
    }

    static async Task<byte[]> ReadPacket(NetworkStream stream, CancellationToken ct)
    {
        var length = 0;
        for (var shift = 0; ; shift += 7)
        {
            var b = new byte[1];
            if (await stream.ReadAsync(b, ct) == 0) throw new IOException("closed");
            length |= (b[0] & 0x7F) << shift;
            if ((b[0] & 0x80) == 0) break;
            if (shift > 28) throw new IOException("bad length");
        }
        if (length is <= 0 or > (1 << 21)) throw new IOException("bad length");
        var data = new byte[length];
        await stream.ReadExactlyAsync(data, ct);
        return data;
    }

    static void VarInt(List<byte> to, int value)
    {
        var v = (uint)value;
        do
        {
            var b = (byte)(v & 0x7F);
            v >>= 7;
            if (v != 0) b |= 0x80;
            to.Add(b);
        } while (v != 0);
    }

    static int ReadVarInt(byte[] data, ref int at)
    {
        var result = 0;
        for (var shift = 0; shift < 35; shift += 7)
        {
            var b = data[at++];
            result |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0) return result;
        }
        throw new IOException("bad varint");
    }

    static void Str(List<byte> to, string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        VarInt(to, bytes.Length);
        to.AddRange(bytes);
    }

    // ---------------------------------------------------------------- проверки

    [SelfTest]
    static string ServersDatRoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ml-servers-" + Environment.ProcessId);
        Directory.CreateDirectory(dir);
        try
        {
            var i = new McInstance { Id = "t", Dir = dir };
            Add(i, "Друзья 🎮", "play.example.net:25566");
            Add(i, "Hub", "hub.example.org");
            var list = List(i);
            if (list.Count != 2 || list[0].Name != "Hub" || list[1].Name != "Друзья 🎮" || list[1].Address != "play.example.net:25566")
                throw new Exception(string.Join("; ", list));
            Remove(i, list[0]);
            if (List(i) is not [{ Name: "Друзья 🎮" }]) throw new Exception("remove failed");
            if (Parse("play.example.net:25566") != ("play.example.net", 25566) || Parse("mc.example.org") != ("mc.example.org", 25565) || Parse("[::1]:25570") != ("::1", 25570))
                throw new Exception("address parse");
            return "servers.dat written and read back (emoji in name), removed, addresses parsed";
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    /// <summary>Пинг локального «сервера», который отвечает как Minecraft 1.21: онлайн, описание без §-кодов.</summary>
    [SelfTest]
    static string PingsAServer()
    {
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            var s = client.GetStream();
            await ReadPacket(s, default); // рукопожатие
            await ReadPacket(s, default); // запрос статуса
            var body = new List<byte>();
            VarInt(body, 0);
            Str(body, """{"version":{"name":"1.21.1","protocol":767},"players":{"max":100,"online":7},"description":{"text":"§aДобро ","extra":[{"text":"пожаловать"}]}}""");
            await Send(s, body, default);
            var ping = await ReadPacket(s, default);
            await Send(s, ping, default);
        });
        try
        {
            var st = Task.Run(() => Ping($"127.0.0.1:{port}")).GetAwaiter().GetResult();
            if (!st.Online || st.Players != 7 || st.Max != 100 || st.Motd != "Добро пожаловать" || st.Version != "1.21.1" || st.Ms < 0)
                throw new Exception(st.ToString());
            return $"online 7/100, «{st.Motd}», {st.Ms} ms";
        }
        finally { listener.Stop(); }
    }
}

/// <summary>Двоичный формат NBT (Java Edition, big-endian) — ровно столько, сколько нужно для servers.dat.</summary>
public static class Nbt
{
    public const byte TEnd = 0, TByte = 1, TShort = 2, TInt = 3, TLong = 4, TFloat = 5, TDouble = 6, TByteArray = 7, TString = 8, TList = 9, TCompound = 10, TIntArray = 11, TLongArray = 12;

    public sealed record Tag(byte Type, object Value);
    public sealed record ListValue(byte ItemType, List<Tag> Items);

    public static Tag? Child(Tag compound, string name) =>
        compound.Value is List<(string Name, Tag Tag)> fields ? fields.FirstOrDefault(f => f.Name == name).Tag : null;

    public static string? Str(Tag compound, string name) => Child(compound, name) is { Type: TString, Value: string s } ? s : null;

    public static Tag Read(byte[] data)
    {
        var at = 0;
        var type = data[at++];
        if (type != TCompound) throw new InvalidDataException("not NBT");
        ReadString(data, ref at);
        return ReadPayload(data, ref at, type);
    }

    static Tag ReadPayload(byte[] d, ref int at, byte type)
    {
        switch (type)
        {
            case TByte: return new Tag(type, (sbyte)d[at++]);
            case TShort: { var v = BinaryPrimitives.ReadInt16BigEndian(d.AsSpan(at)); at += 2; return new Tag(type, v); }
            case TInt: { var v = BinaryPrimitives.ReadInt32BigEndian(d.AsSpan(at)); at += 4; return new Tag(type, v); }
            case TLong: { var v = BinaryPrimitives.ReadInt64BigEndian(d.AsSpan(at)); at += 8; return new Tag(type, v); }
            case TFloat: { var v = BinaryPrimitives.ReadSingleBigEndian(d.AsSpan(at)); at += 4; return new Tag(type, v); }
            case TDouble: { var v = BinaryPrimitives.ReadDoubleBigEndian(d.AsSpan(at)); at += 8; return new Tag(type, v); }
            case TByteArray:
            {
                var n = BinaryPrimitives.ReadInt32BigEndian(d.AsSpan(at)); at += 4;
                var v = d.AsSpan(at, n).ToArray(); at += n;
                return new Tag(type, v);
            }
            case TString: return new Tag(type, ReadString(d, ref at));
            case TList:
            {
                var item = d[at++];
                var n = BinaryPrimitives.ReadInt32BigEndian(d.AsSpan(at)); at += 4;
                var items = new List<Tag>(Math.Max(0, n));
                for (var k = 0; k < n; k++) items.Add(ReadPayload(d, ref at, item));
                return new Tag(type, new ListValue(item, items));
            }
            case TCompound:
            {
                var fields = new List<(string, Tag)>();
                while (true)
                {
                    var t = d[at++];
                    if (t == TEnd) break;
                    var name = ReadString(d, ref at);
                    fields.Add((name, ReadPayload(d, ref at, t)));
                }
                return new Tag(type, fields);
            }
            case TIntArray:
            {
                var n = BinaryPrimitives.ReadInt32BigEndian(d.AsSpan(at)); at += 4;
                var v = new int[n];
                for (var k = 0; k < n; k++) { v[k] = BinaryPrimitives.ReadInt32BigEndian(d.AsSpan(at)); at += 4; }
                return new Tag(type, v);
            }
            case TLongArray:
            {
                var n = BinaryPrimitives.ReadInt32BigEndian(d.AsSpan(at)); at += 4;
                var v = new long[n];
                for (var k = 0; k < n; k++) { v[k] = BinaryPrimitives.ReadInt64BigEndian(d.AsSpan(at)); at += 8; }
                return new Tag(type, v);
            }
            default: throw new InvalidDataException($"NBT tag {type}");
        }
    }

    public static byte[] Write(Tag root)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(TCompound);
        WriteString(ms, "");
        WritePayload(ms, root);
        return ms.ToArray();
    }

    static void WritePayload(MemoryStream o, Tag t)
    {
        Span<byte> b = stackalloc byte[8];
        switch (t.Type)
        {
            case TByte: o.WriteByte((byte)(sbyte)t.Value); break;
            case TShort: BinaryPrimitives.WriteInt16BigEndian(b, (short)t.Value); o.Write(b[..2]); break;
            case TInt: BinaryPrimitives.WriteInt32BigEndian(b, (int)t.Value); o.Write(b[..4]); break;
            case TLong: BinaryPrimitives.WriteInt64BigEndian(b, (long)t.Value); o.Write(b[..8]); break;
            case TFloat: BinaryPrimitives.WriteSingleBigEndian(b, (float)t.Value); o.Write(b[..4]); break;
            case TDouble: BinaryPrimitives.WriteDoubleBigEndian(b, (double)t.Value); o.Write(b[..8]); break;
            case TByteArray: { var v = (byte[])t.Value; BinaryPrimitives.WriteInt32BigEndian(b, v.Length); o.Write(b[..4]); o.Write(v); break; }
            case TString: WriteString(o, (string)t.Value); break;
            case TList:
            {
                var l = (ListValue)t.Value;
                o.WriteByte(l.Items.Count == 0 ? TEnd : l.ItemType);
                BinaryPrimitives.WriteInt32BigEndian(b, l.Items.Count); o.Write(b[..4]);
                foreach (var item in l.Items) WritePayload(o, item);
                break;
            }
            case TCompound:
                foreach (var (name, tag) in (List<(string, Tag)>)t.Value)
                {
                    o.WriteByte(tag.Type);
                    WriteString(o, name);
                    WritePayload(o, tag);
                }
                o.WriteByte(TEnd);
                break;
            case TIntArray:
            {
                var v = (int[])t.Value;
                BinaryPrimitives.WriteInt32BigEndian(b, v.Length); o.Write(b[..4]);
                foreach (var x in v) { BinaryPrimitives.WriteInt32BigEndian(b, x); o.Write(b[..4]); }
                break;
            }
            case TLongArray:
            {
                var v = (long[])t.Value;
                BinaryPrimitives.WriteInt32BigEndian(b, v.Length); o.Write(b[..4]);
                foreach (var x in v) { BinaryPrimitives.WriteInt64BigEndian(b, x); o.Write(b[..8]); }
                break;
            }
        }
    }

    /// <summary>Строки NBT — «модифицированный UTF-8» Java: \0 двумя байтами, эмодзи — парой суррогатов по 3 байта.</summary>
    static string ReadString(byte[] d, ref int at)
    {
        var n = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(at)); at += 2;
        var end = at + n;
        var sb = new StringBuilder(n);
        while (at < end)
        {
            var c = d[at++];
            if (c < 0x80) sb.Append((char)c);
            else if ((c & 0xE0) == 0xC0) sb.Append((char)(((c & 0x1F) << 6) | (d[at++] & 0x3F)));
            else { var c2 = d[at++]; var c3 = d[at++]; sb.Append((char)(((c & 0x0F) << 12) | ((c2 & 0x3F) << 6) | (c3 & 0x3F))); }
        }
        return sb.ToString();
    }

    static void WriteString(MemoryStream o, string s)
    {
        var bytes = new List<byte>(s.Length);
        foreach (var ch in s)
        {
            if (ch != 0 && ch < 0x80) bytes.Add((byte)ch);
            else if (ch < 0x800) { bytes.Add((byte)(0xC0 | (ch >> 6))); bytes.Add((byte)(0x80 | (ch & 0x3F))); }
            else { bytes.Add((byte)(0xE0 | (ch >> 12))); bytes.Add((byte)(0x80 | ((ch >> 6) & 0x3F))); bytes.Add((byte)(0x80 | (ch & 0x3F))); }
        }
        var len = Math.Min(bytes.Count, ushort.MaxValue);
        o.WriteByte((byte)(len >> 8));
        o.WriteByte((byte)len);
        o.Write(bytes.ToArray(), 0, len);
    }
}
