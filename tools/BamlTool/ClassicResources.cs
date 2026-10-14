using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BamlTool
{
    /// <summary>
    /// 经典 .resources 文件（ResourceManager header v1 + format v2）的手写序列化器。
    ///
    /// 为什么不能用 System.Resources.ResourceWriter？
    ///   .NET 8 的 ResourceWriter 写出的是 System.Resources.Extensions 引入的
    ///   「新格式」（numResources=1 / numTypes=1，单一 _ResourceSet 包装类型），
    ///   而 WPF 内部用的是经典 System.Resources.ResourceReader 语义。
    ///   把新格式塞回 DLL 后，WPF 报 "Stream is not a valid resource file." 并崩溃。
    ///
    /// 所以这里按 dotnet/runtime 的 ResourceReader._ReadResources() 反推出格式，
    /// 逐字节重建。格式布局：
    ///
    ///   0   i32  magic = 0xBEEFCACE
    ///   4   i32  headerVersion = 1
    ///   8   i32  numBytesToSkip（后面 header 剩余字节数）
    ///   12  [7bit+UTF8] readerType
    ///       [7bit+UTF8] resourceSetType
    ///   --- header 结束 ---
    ///       i32 formatVersion = 2
    ///       i32 numResources
    ///       i32 numTypes（这里是 0）
    ///       （typeName * numTypes）
    ///       align 到 8 字节
    ///       i32 nameHashes[numResources]     <- 必须按「有符号」值升序，reader 做二分
    ///       i32 namePositions[numResources]  <- 相对 name section 的偏移
    ///       i32 dataSectionOffset            <- 绝对偏移
    ///   name section（紧接上面）：
    ///       每条：[7bit+UTF16LE 名字长度][名字][i32 dataPos（相对 data section）]
    ///   data section：
    ///       每条：[7bit typeCode][payload]；typeCode=33(Stream)/32(ByteArray) 时为 i32 长度 + 数据
    /// </summary>
    static class ClassicResources
    {
        const uint MAGIC = 0xBEEFCACE;
        const string READER_TYPE =
            "System.Resources.ResourceReader, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089";
        const string SET_TYPE = "System.Resources.RuntimeResourceSet";
        const int TYPE_STREAM = 33;

        /// <summary>System.Resources.FastResourceComparer.HashFunction —— 必须是同一个函数。</summary>
        public static int HashFunction(string key)
        {
            uint hash = 5381;
            for (int i = 0; i < key.Length; i++)
                hash = ((hash << 5) + hash) ^ key[i];
            return unchecked((int)hash);
        }

        static void Write7BitInt(Stream s, int value)
        {
            // 与 BinaryWriter.Write7BitEncodedInt 一致
            uint v = (uint)value;
            while (v >= 0x80) { s.WriteByte((byte)(v | 0x80)); v >>= 7; }
            s.WriteByte((byte)v);
        }

        static void WriteBinaryString(Stream s, string value)
        {
            // BinaryWriter.WriteString：7bit 长度（字节数）+ UTF8
            var b = Encoding.UTF8.GetBytes(value);
            Write7BitInt(s, b.Length);
            s.Write(b, 0, b.Length);
        }

        static void WriteUtf16String(Stream s, string value)
        {
            // ResourceWriter 写名字用的是 Encoding.Unicode（UTF-16LE）
            var b = Encoding.Unicode.GetBytes(value);
            Write7BitInt(s, b.Length);
            s.Write(b, 0, b.Length);
        }

        public static byte[] Write(IDictionary<string, byte[]> entries)
        {
            // 这里有三种不同的顺序，别搞混（照 .NET ResourceWriter 的行为）：
            //   1. hashes / namePositions 两个数组 —— 按 hash 升序（reader 要二分查找）
            //   2. name section 的物理排列 —— 按名字 ordinal 升序
            //   3. data section 的物理排列 —— 同样按名字 ordinal 升序
            // 用「按 hash 序」写全部内容也能被读出来，但做不到和原文件字节一致，
            // 而字节一致是「我的 writer 和 .NET 官方实现等价」的最强证据。
            var byName = new List<KeyValuePair<string, byte[]>>(entries.Count);
            foreach (var kv in entries) byName.Add(kv);
            byName.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));

            var byHash = new List<KeyValuePair<string, byte[]>>(byName);
            byHash.Sort((a, b) =>
            {
                int c = HashFunction(a.Key).CompareTo(HashFunction(b.Key));
                return c != 0 ? c : string.CompareOrdinal(a.Key, b.Key);
            });

            int n = entries.Count;
            var ms = new MemoryStream();
            var bw = new BinaryWriter(ms);

            bw.Write(MAGIC);
            bw.Write(1);                       // headerVersion

            long skipFieldPos = ms.Position;
            bw.Write(0);                       // numBytesToSkip 占位
            long hdrStart = ms.Position;
            WriteBinaryString(ms, READER_TYPE);
            WriteBinaryString(ms, SET_TYPE);
            long hdrEnd = ms.Position;
            long saved = ms.Position;
            ms.Position = skipFieldPos;
            bw.Write((int)(hdrEnd - hdrStart));
            ms.Position = saved;

            bw.Write(2);                       // formatVersion
            bw.Write(n);                       // numResources
            bw.Write(0);                       // numTypes

            // 8 字节对齐。官方 writer 用 ASCII "PAD" 循环填充；reader 明确不校验内容，
            // 但照样写上以保持字节一致。
            int padIdx = 0;
            while ((ms.Position & 7) != 0) bw.Write((byte)"PAD"[padIdx++ % 3]);

            foreach (var it in byHash) bw.Write(HashFunction(it.Key));

            long namePosArrayPos = ms.Position;
            bw.Write(new byte[4 * n]);          // namePositions 占位

            long dataSecFieldPos = ms.Position;
            bw.Write(0);                        // dataSectionOffset 占位

            // ---- name section：按名字字典序写，记下每条的偏移 ----
            long nameSecStart = ms.Position;
            var nameOffset = new Dictionary<string, int>(n, StringComparer.Ordinal);
            var dataPosFieldPos = new Dictionary<string, long>(n, StringComparer.Ordinal);
            foreach (var it in byName)
            {
                nameOffset[it.Key] = checked((int)(ms.Position - nameSecStart));
                WriteUtf16String(ms, it.Key);
                dataPosFieldPos[it.Key] = ms.Position;
                bw.Write(0);                    // 该条目的 dataPos 占位
            }

            // ---- data section：同样按名字字典序写 ----
            long dataSecStart = ms.Position;
            foreach (var it in byName)
            {
                int dp = checked((int)(ms.Position - dataSecStart));
                long keep = ms.Position;
                ms.Position = dataPosFieldPos[it.Key];
                bw.Write(dp);
                ms.Position = keep;

                Write7BitInt(ms, TYPE_STREAM);
                bw.Write(it.Value.Length);
                bw.Write(it.Value);
            }

            long end = ms.Position;

            ms.Position = dataSecFieldPos;
            bw.Write((int)dataSecStart);
            ms.Position = namePosArrayPos;
            foreach (var it in byHash) bw.Write(nameOffset[it.Key]);
            ms.Position = end;

            bw.Flush();
            var result = ms.ToArray();
            bw.Dispose();
            return result;
        }
    }
}
