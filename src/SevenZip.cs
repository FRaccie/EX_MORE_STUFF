using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ExMoreStuff
{
    // .7z archives read by EX More Stuff itself (user 2026-10-13): Windows 10's own tar can't unpack LZMA ("LZMA codec is
    // unsupported"), and 7 of GameBanana's USF4 skins come only as 7z. The 7z container (7-Zip's 7zFormat.txt) with the
    // Copy, LZMA and LZMA2 methods 7-Zip packs with, each file checked against its CRC; anything else (a chain of methods
    // such as BCJ, PPMd, BZip2, a password) is NotSupportedException, and the caller tries Windows' tar instead. Written
    // from the LZMA SDK's public-domain design (Igor Pavlov); no library.
    static class SevenZip
    {
        static readonly byte[] Signature = { (byte)'7', (byte)'z', 0xBC, 0xAF, 0x27, 0x1C };
        const int End = 0x00, Header = 0x01, ArchiveProperties = 0x02, AdditionalStreamsInfo = 0x03, MainStreamsInfo = 0x04, FilesInfo = 0x05,
                  PackInfo = 0x06, UnpackInfo = 0x07, SubStreamsInfo = 0x08, Size = 0x09, Crc = 0x0A, FolderId = 0x0B, CodersUnpackSize = 0x0C,
                  NumUnpackStream = 0x0D, EmptyStream = 0x0E, EmptyFile = 0x0F, Name = 0x11, EncodedHeader = 0x17;

        public static bool Is(string path)
        {
            var head = new byte[6];
            using (var s = File.OpenRead(path)) return s.Read(head, 0, 6) == 6 && head.SequenceEqual(Signature);
        }

        /// <summary>The files in a 7z archive whose names `want` takes (paths inside, with '/'), each checked against its CRC.</summary>
        public static List<KeyValuePair<string, byte[]>> Read(string path, Func<string, bool> want)
        {
            byte[] data = File.ReadAllBytes(path);
            if (data.Length < 32 || !data.Take(6).SequenceEqual(Signature)) throw new InvalidDataException("it isn't a 7z archive");
            long nextOffset = (long)BitConverter.ToUInt64(data, 12), nextSize = (long)BitConverter.ToUInt64(data, 20);
            if (nextSize == 0) return new List<KeyValuePair<string, byte[]>>();
            if (nextOffset < 0 || nextSize < 0 || 32 + nextOffset + nextSize > data.Length) throw new InvalidDataException("the 7z archive is cut short");
            var header = new byte[nextSize];
            Array.Copy(data, 32 + nextOffset, header, 0, nextSize);
            for (int packed = 0; packed < 4; packed++)   // a packed header: the real one is unpacked like the files
            {
                var r = new Reader(header);
                long id = r.Number();
                if (id == Header) return Files(data, r, want);
                if (id != EncodedHeader) throw new InvalidDataException("the 7z archive's header isn't one 7-Zip writes");
                Streams streams = ReadStreams(r);
                if (streams.Folders.Count == 0) throw new InvalidDataException("the 7z archive's header is empty");
                header = Unfold(data, streams, 0);
            }
            throw new InvalidDataException("the 7z archive's header is packed too many times");
        }

        sealed class Coder { public byte[] Id; public int In = 1, Out = 1; public byte[] Props = new byte[0]; }

        sealed class Folder
        {
            public readonly List<Coder> Coders = new List<Coder>();
            public readonly List<long> UnpackSizes = new List<long>();
            public uint? Crc;
            public int Substreams = 1, FirstPack, Packed = 1;
            public long UnpackSize { get { return UnpackSizes.Count > 0 ? UnpackSizes[UnpackSizes.Count - 1] : 0; } }
        }

        sealed class Streams
        {
            public long PackPos;
            public readonly List<long> PackSizes = new List<long>();
            public readonly List<Folder> Folders = new List<Folder>();
            public readonly List<long> Sizes = new List<long>();    // each file stream's size, folder by folder
            public readonly List<uint?> Crcs = new List<uint?>();
        }

        static Streams ReadStreams(Reader r)
        {
            var s = new Streams();
            bool sub = false;
            for (long id = r.Number(); id != End; id = r.Number())
            {
                if (id == PackInfo)
                {
                    s.PackPos = r.Number();
                    int count = (int)r.Number();
                    for (long t = r.Number(); t != End; t = r.Number())
                        if (t == Size) for (int i = 0; i < count; i++) s.PackSizes.Add(r.Number());
                        else if (t == Crc) Digests(r, count);
                        else throw new InvalidDataException("the 7z archive's pack info isn't one 7-Zip writes");
                }
                else if (id == UnpackInfo)
                {
                    if (r.Number() != FolderId) throw new InvalidDataException("the 7z archive's folders aren't where they should be");
                    int count = (int)r.Number();
                    if (r.Byte() != 0) throw new NotSupportedException("7z folders kept elsewhere");
                    for (int i = 0; i < count; i++) s.Folders.Add(ReadFolder(r));
                    if (r.Number() != CodersUnpackSize) throw new InvalidDataException("the 7z archive's sizes aren't where they should be");
                    foreach (Folder f in s.Folders) for (int i = 0, outs = f.Coders.Sum(c => c.Out); i < outs; i++) f.UnpackSizes.Add(r.Number());
                    for (long t = r.Number(); t != End; t = r.Number())
                        if (t == Crc) { var crcs = Digests(r, s.Folders.Count); for (int i = 0; i < crcs.Length; i++) s.Folders[i].Crc = crcs[i]; }
                        else throw new InvalidDataException("the 7z archive's unpack info isn't one 7-Zip writes");
                }
                else if (id == SubStreamsInfo)
                {
                    sub = true;
                    long t = r.Number();
                    if (t == NumUnpackStream) { foreach (Folder f in s.Folders) f.Substreams = (int)r.Number(); t = r.Number(); }
                    bool sizes = t == Size;
                    foreach (Folder f in s.Folders)
                    {
                        if (f.Substreams == 0) continue;
                        long sum = 0;
                        for (int i = 1; i < f.Substreams; i++)
                        {
                            if (!sizes) throw new InvalidDataException("the 7z archive's file sizes are missing");
                            long size = r.Number();
                            s.Sizes.Add(size);
                            sum += size;
                        }
                        s.Sizes.Add(f.UnpackSize - sum);
                    }
                    if (sizes) t = r.Number();
                    // CRCs listed for the streams whose CRC isn't known yet (a folder of one stream with its own CRC is known)
                    int unknown = s.Folders.Sum(f => f.Substreams == 1 && f.Crc != null ? 0 : f.Substreams);
                    uint?[] listed = t == Crc ? Digests(r, unknown) : new uint?[unknown];
                    if (t == Crc) t = r.Number();
                    int k = 0;
                    foreach (Folder f in s.Folders)
                        if (f.Substreams == 1 && f.Crc != null) s.Crcs.Add(f.Crc);
                        else for (int i = 0; i < f.Substreams; i++) s.Crcs.Add(listed[k++]);
                    if (t != End) throw new InvalidDataException("the 7z archive's stream info isn't one 7-Zip writes");
                }
                else throw new InvalidDataException("the 7z archive's streams aren't described the way 7-Zip does");
            }
            if (!sub) foreach (Folder f in s.Folders) { s.Sizes.Add(f.UnpackSize); s.Crcs.Add(f.Crc); }
            int pack = 0;
            foreach (Folder f in s.Folders) { f.FirstPack = pack; pack += f.Packed; }
            return s;
        }

        static Folder ReadFolder(Reader r)
        {
            var f = new Folder();
            int coders = (int)r.Number();
            for (int i = 0; i < coders; i++)
            {
                byte flags = r.Byte();
                if ((flags & 0x80) != 0) throw new NotSupportedException("7z alternative methods");
                var c = new Coder { Id = r.Bytes(flags & 0x0F) };
                if ((flags & 0x10) != 0) { c.In = (int)r.Number(); c.Out = (int)r.Number(); }
                if ((flags & 0x20) != 0) c.Props = r.Bytes((int)r.Number());
                f.Coders.Add(c);
            }
            int ins = f.Coders.Sum(c => c.In), outs = f.Coders.Sum(c => c.Out), binds = outs - 1;
            for (int i = 0; i < binds; i++) { r.Number(); r.Number(); }
            f.Packed = ins - binds;
            if (f.Packed > 1) for (int i = 0; i < f.Packed; i++) r.Number();
            return f;
        }

        static uint?[] Digests(Reader r, int count)
        {
            bool all = r.Byte() != 0;
            bool[] defined = all ? null : r.Bits(count);
            var crcs = new uint?[count];
            for (int i = 0; i < count; i++) if (all || defined[i]) crcs[i] = r.UInt32();
            return crcs;
        }

        // the files: names, which have data, and the data of those `want` takes (only the folders holding one are unpacked)
        static List<KeyValuePair<string, byte[]>> Files(byte[] data, Reader r, Func<string, bool> want)
        {
            Streams main = null;
            int count = 0;
            bool[] emptyStream = null;
            string[] names = null;
            for (long id = r.Number(); id != End; id = r.Number())
            {
                if (id == ArchiveProperties) { for (long t = r.Number(); t != End; t = r.Number()) r.Skip(r.Number()); }
                else if (id == AdditionalStreamsInfo) ReadStreams(r);
                else if (id == MainStreamsInfo) main = ReadStreams(r);
                else if (id == FilesInfo)
                {
                    count = (int)r.Number();
                    emptyStream = new bool[count];
                    names = new string[count];
                    for (long t = r.Number(); t != End; t = r.Number())
                    {
                        long size = r.Number();
                        int end = r.Pos + (int)size;
                        if (t == EmptyStream) emptyStream = r.Bits(count);
                        else if (t == Name)
                        {
                            if (r.Byte() != 0) throw new NotSupportedException("7z names kept elsewhere");
                            for (int i = 0; i < count; i++) names[i] = r.Utf16();
                        }
                        r.Pos = end;   // the rest (empty files, times, attributes) isn't needed
                    }
                }
                else throw new InvalidDataException("the 7z archive's header isn't one 7-Zip writes");
            }
            var result = new List<KeyValuePair<string, byte[]>>();
            if (main == null || names == null) return result;
            // each file with data is the next stream, folder by folder
            var where = new List<Tuple<int, long, long, uint?>>();   // folder, offset in it, size, CRC
            int stream = 0;
            for (int f = 0; f < main.Folders.Count; f++)
            {
                long offset = 0;
                for (int i = 0; i < main.Folders[f].Substreams; i++, stream++)
                {
                    where.Add(Tuple.Create(f, offset, main.Sizes[stream], main.Crcs[stream]));
                    offset += main.Sizes[stream];
                }
            }
            var unpacked = new Dictionary<int, byte[]>();
            int next = 0;
            for (int i = 0; i < count; i++)
            {
                if (emptyStream[i]) continue;   // a folder or an empty file
                if (next >= where.Count) throw new InvalidDataException("the 7z archive has more files than data");
                var w = where[next++];
                string name = (names[i] ?? "").Replace('\\', '/');
                if (!want(name)) continue;
                byte[] folder;
                if (!unpacked.TryGetValue(w.Item1, out folder)) unpacked[w.Item1] = folder = Unfold(data, main, w.Item1);
                var file = new byte[w.Item3];
                Array.Copy(folder, w.Item2, file, 0, w.Item3);
                if (w.Item4 != null && Crc32(file) != w.Item4.Value) throw new InvalidDataException(name + " in the 7z archive is damaged (its CRC doesn't match)");
                result.Add(new KeyValuePair<string, byte[]>(name, file));
            }
            return result;
        }

        // a folder's data: one method, Copy, LZMA or LZMA2
        static byte[] Unfold(byte[] data, Streams s, int index)
        {
            Folder f = s.Folders[index];
            if (f.Coders.Count != 1 || f.Coders[0].In != 1 || f.Coders[0].Out != 1) throw new NotSupportedException("a chain of 7z methods");
            long start = 32 + s.PackPos + s.PackSizes.Take(f.FirstPack).Sum(), size = s.PackSizes[f.FirstPack];
            if (start + size > data.Length) throw new InvalidDataException("the 7z archive is cut short");
            if (f.UnpackSize > int.MaxValue) throw new NotSupportedException("a 7z folder over 2 GB");
            byte[] id = f.Coders[0].Id, props = f.Coders[0].Props;
            var output = new byte[f.UnpackSize];
            if (id.SequenceEqual(new byte[] { 0x00 })) Array.Copy(data, start, output, 0, output.Length);
            else if (id.SequenceEqual(new byte[] { 0x03, 0x01, 0x01 }))
            {
                if (props.Length < 5) throw new InvalidDataException("the 7z archive's LZMA settings are missing");
                var lzma = new Lzma(output);
                lzma.SetProps(props[0]);
                lzma.Reset();
                lzma.Chunk(data, (int)start, output.Length);
            }
            else if (id.SequenceEqual(new byte[] { 0x21 })) Lzma.Lzma2(data, (int)start, (int)size, output);
            else throw new NotSupportedException("the 7z method " + BitConverter.ToString(id));
            if (f.Crc != null && f.Substreams != 1 && Crc32(output) != f.Crc.Value) throw new InvalidDataException("the 7z archive is damaged (a CRC doesn't match)");
            return output;
        }

        static uint[] crcTable;

        static uint Crc32(byte[] data)
        {
            if (crcTable == null)
            {
                var table = new uint[256];
                for (uint i = 0; i < 256; i++) { uint c = i; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1; table[i] = c; }
                crcTable = table;
            }
            uint crc = 0xFFFFFFFF;
            foreach (byte b in data) crc = crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return ~crc;
        }

        // the 7z header's numbers (1 to 9 bytes, the first byte's high bits saying how many follow), bit fields, names
        sealed class Reader
        {
            readonly byte[] b;
            public int Pos;
            public Reader(byte[] bytes) { b = bytes; }
            public byte Byte() { return b[Pos++]; }
            public byte[] Bytes(int n) { var x = new byte[n]; Array.Copy(b, Pos, x, 0, n); Pos += n; return x; }
            public void Skip(long n) { Pos += (int)n; }
            public uint UInt32() { uint v = BitConverter.ToUInt32(b, Pos); Pos += 4; return v; }

            public long Number()
            {
                int first = Byte(), mask = 0x80;
                long value = 0;
                for (int i = 0; i < 8; i++)
                {
                    if ((first & mask) == 0) return value | ((long)(first & (mask - 1)) << (8 * i));
                    value |= (long)Byte() << (8 * i);
                    mask >>= 1;
                }
                return value;
            }

            public bool[] Bits(int n)
            {
                var bits = new bool[n];
                int current = 0, mask = 0;
                for (int i = 0; i < n; i++)
                {
                    if (mask == 0) { current = Byte(); mask = 0x80; }
                    bits[i] = (current & mask) != 0;
                    mask >>= 1;
                }
                return bits;
            }

            public string Utf16()
            {
                int start = Pos;
                while (b[Pos] != 0 || b[Pos + 1] != 0) Pos += 2;
                string s = Encoding.Unicode.GetString(b, start, Pos - start);
                Pos += 2;
                return s;
            }
        }
    }

    // An LZMA decoder (the LZMA SDK's lzma-specification, public domain) writing into one output array that is its own
    // dictionary: a mod's files are unpacked whole into memory, so a match reads straight from what was written. LZMA2
    // (chunks of LZMA and stored data, each resetting the range coder and maybe the state, settings or dictionary) on top.
    sealed class Lzma
    {
        const int States = 12, PosBitsMax = 4, LenToPosStates = 4, AlignBits = 4, EndPosModel = 14, FullDistances = 128;
        readonly byte[] output;
        int outPos, dictStart;   // bytes since the dictionary began: the "position" LZMA's contexts count
        byte[] input;
        int inPos;
        uint range, code;
        int lc, lp, pb, state;
        uint rep0, rep1, rep2, rep3;
        ushort[] literal;
        readonly ushort[] isMatch = new ushort[States << PosBitsMax], isRep = new ushort[States], isRepG0 = new ushort[States], isRepG1 = new ushort[States],
                          isRepG2 = new ushort[States], isRep0Long = new ushort[States << PosBitsMax], posSlot = new ushort[LenToPosStates << 6],
                          posDecoders = new ushort[1 + FullDistances - EndPosModel], align = new ushort[1 << AlignBits];
        readonly LenCoder len = new LenCoder(), repLen = new LenCoder();

        public Lzma(byte[] output) { this.output = output; }

        public void SetProps(int d)
        {
            if (d >= 9 * 5 * 5) throw new InvalidDataException("the LZMA settings are out of range");
            lc = d % 9; d /= 9; lp = d % 5; pb = d / 5;
            literal = new ushort[0x300 << (lc + lp)];
        }

        public void Reset()
        {
            foreach (ushort[] p in new[] { literal, isMatch, isRep, isRepG0, isRepG1, isRepG2, isRep0Long, posSlot, posDecoders, align })
                for (int i = 0; i < p.Length; i++) p[i] = 1024;
            len.Reset(); repLen.Reset();
            state = 0;
            rep0 = rep1 = rep2 = rep3 = 0;
        }

        public void ResetDictionary() { dictStart = outPos; }

        // `unpack` bytes from LZMA data at input[start] (a range coder of its own)
        public void Chunk(byte[] data, int start, int unpack)
        {
            input = data; inPos = start;
            if (Next() != 0) throw new InvalidDataException("the LZMA data is damaged");
            range = 0xFFFFFFFF; code = 0;
            for (int i = 0; i < 4; i++) code = (code << 8) | Next();
            int end = outPos + unpack;
            if (end > output.Length) throw new InvalidDataException("the LZMA data is longer than said");
            uint pbMask = (1u << pb) - 1;
            while (outPos < end)
            {
                uint posState = (uint)(outPos - dictStart) & pbMask;
                if (Bit(isMatch, (state << PosBitsMax) + (int)posState) == 0)
                {
                    Literal();
                    state = state < 4 ? 0 : state < 10 ? state - 3 : state - 6;
                    continue;
                }
                int length;
                if (Bit(isRep, state) != 0)
                {
                    if (outPos == dictStart) throw new InvalidDataException("the LZMA data is damaged");
                    if (Bit(isRepG0, state) == 0)
                    {
                        if (Bit(isRep0Long, (state << PosBitsMax) + (int)posState) == 0)
                        {
                            state = state < 7 ? 9 : 11;
                            output[outPos] = output[outPos - rep0 - 1];
                            outPos++;
                            continue;
                        }
                    }
                    else
                    {
                        uint dist;
                        if (Bit(isRepG1, state) == 0) dist = rep1;
                        else
                        {
                            if (Bit(isRepG2, state) == 0) dist = rep2;
                            else { dist = rep3; rep3 = rep2; }
                            rep2 = rep1;
                        }
                        rep1 = rep0;
                        rep0 = dist;
                    }
                    length = repLen.Decode(this, (int)posState);
                    state = state < 7 ? 8 : 11;
                }
                else
                {
                    rep3 = rep2; rep2 = rep1; rep1 = rep0;
                    length = len.Decode(this, (int)posState);
                    state = state < 7 ? 7 : 10;
                    rep0 = Distance(length);
                    if (rep0 == 0xFFFFFFFF) break;   // the end marker
                }
                length += 2;
                if (rep0 >= outPos - dictStart) throw new InvalidDataException("the LZMA data is damaged");
                if (length > end - outPos) throw new InvalidDataException("the LZMA data is longer than said");
                int from = outPos - (int)rep0 - 1;
                for (int i = 0; i < length; i++) output[outPos++] = output[from + i];
            }
            if (outPos != end) throw new InvalidDataException("the LZMA data ended early");
        }

        void Literal()
        {
            int prev = outPos > dictStart ? output[outPos - 1] : 0;
            int litState = (((outPos - dictStart) & ((1 << lp) - 1)) << lc) + (prev >> (8 - lc));
            int probs = 0x300 * litState;
            int symbol = 1;
            if (state >= 7)
            {
                int match = output[outPos - rep0 - 1];
                do
                {
                    int matchBit = (match >> 7) & 1;
                    match <<= 1;
                    int bit = Bit(literal, probs + ((1 + matchBit) << 8) + symbol);
                    symbol = (symbol << 1) | bit;
                    if (matchBit != bit) break;
                }
                while (symbol < 0x100);
            }
            while (symbol < 0x100) symbol = (symbol << 1) | Bit(literal, probs + symbol);
            output[outPos++] = (byte)(symbol - 0x100);
        }

        uint Distance(int length)
        {
            int lenState = Math.Min(length, LenToPosStates - 1);
            int slot = Tree(posSlot, lenState << 6, 6);
            if (slot < 4) return (uint)slot;
            int direct = (slot >> 1) - 1;
            uint dist = (uint)((2 | (slot & 1)) << direct);
            if (slot < EndPosModel) dist += (uint)Reverse(posDecoders, (int)dist - slot, direct);
            else
            {
                dist += Direct(direct - AlignBits) << AlignBits;
                dist += (uint)Reverse(align, 0, AlignBits);
            }
            return dist;
        }

        byte Next() { return inPos < input.Length ? input[inPos++] : (byte)0; }

        internal int Bit(ushort[] probs, int index)
        {
            uint p = probs[index], bound = (range >> 11) * p;
            int bit;
            if (code < bound) { range = bound; probs[index] = (ushort)(p + ((2048 - p) >> 5)); bit = 0; }
            else { range -= bound; code -= bound; probs[index] = (ushort)(p - (p >> 5)); bit = 1; }
            if (range < (1u << 24)) { range <<= 8; code = (code << 8) | Next(); }
            return bit;
        }

        uint Direct(int count)
        {
            uint result = 0;
            for (int i = 0; i < count; i++)
            {
                range >>= 1;
                code -= range;
                uint t = 0u - (code >> 31);   // all ones when code went below 0, else 0
                code += range & t;
                if (range < (1u << 24)) { range <<= 8; code = (code << 8) | Next(); }
                result = (result << 1) + (t + 1);
            }
            return result;
        }

        // a bit tree of `bits` bits at probs[offset + 1 ...]
        internal int Tree(ushort[] probs, int offset, int bits)
        {
            int m = 1;
            for (int i = 0; i < bits; i++) m = (m << 1) + Bit(probs, offset + m);
            return m - (1 << bits);
        }

        int Reverse(ushort[] probs, int offset, int bits)
        {
            int m = 1, symbol = 0;
            for (int i = 0; i < bits; i++)
            {
                int bit = Bit(probs, offset + m);
                m = (m << 1) + bit;
                symbol |= bit << i;
            }
            return symbol;
        }

        sealed class LenCoder
        {
            readonly ushort[] choice = new ushort[2], low = new ushort[16 << 3], mid = new ushort[16 << 3], high = new ushort[1 << 8];

            public void Reset() { foreach (ushort[] p in new[] { choice, low, mid, high }) for (int i = 0; i < p.Length; i++) p[i] = 1024; }

            public int Decode(Lzma d, int posState)
            {
                if (d.Bit(choice, 0) == 0) return d.Tree(low, posState << 3, 3);
                if (d.Bit(choice, 1) == 0) return 8 + d.Tree(mid, posState << 3, 3);
                return 16 + d.Tree(high, 0, 8);
            }
        }

        /// <summary>LZMA2 data at input[start] (`size` bytes) into `output` (its size is the unpacked size).</summary>
        public static void Lzma2(byte[] input, int start, int size, byte[] output)
        {
            var lzma = new Lzma(output);
            int pos = start, end = start + size;
            bool haveProps = false;
            while (pos < end)
            {
                int control = input[pos++];
                if (control == 0) break;
                if (control == 1 || control == 2)   // stored data, 1 resetting the dictionary
                {
                    int stored = ((input[pos] << 8) | input[pos + 1]) + 1;
                    pos += 2;
                    if (control == 1) lzma.ResetDictionary();
                    if (lzma.outPos + stored > output.Length) throw new InvalidDataException("the LZMA2 data is longer than said");
                    Array.Copy(input, pos, output, lzma.outPos, stored);
                    lzma.outPos += stored;
                    pos += stored;
                    continue;
                }
                if (control < 0x80) throw new InvalidDataException("the LZMA2 data is damaged");
                int unpack = ((control & 0x1F) << 16) + (input[pos] << 8) + input[pos + 1] + 1;
                int packed = (input[pos + 2] << 8) + input[pos + 3] + 1;
                pos += 4;
                int reset = (control >> 5) & 3;   // 0 nothing, 1 the state, 2 state and settings, 3 those and the dictionary
                if (reset == 3) lzma.ResetDictionary();
                if (reset >= 2) { lzma.SetProps(input[pos++]); haveProps = true; }
                if (!haveProps) throw new InvalidDataException("the LZMA2 data has no settings");
                if (reset >= 1) lzma.Reset();
                lzma.Chunk(input, pos, unpack);
                pos += packed;
            }
            if (lzma.outPos != output.Length) throw new InvalidDataException("the LZMA2 data ended early");
        }
    }
}
