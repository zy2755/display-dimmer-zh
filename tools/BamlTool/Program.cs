using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Markup.Localizer;

namespace BamlTool
{
    /// <summary>
    /// WPF BAML 本地化工具。
    /// 走的是 WPF 自带的 LocBaml 管线（内部 BamlResourceDeserializer / BamlResourceSerializer），
    /// 因此能完整保留 BAML 的全部结构（包括延迟内容、连接 ID、转换器等），只替换字符串。
    /// </summary>
    public static class Program
    {
        const BindingFlags BF = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        static Type T_BamlLocalizer = typeof(BamlLocalizer);
        static Type T_BamlResourceSerializer = T_BamlLocalizer.Assembly.GetType("MS.Internal.Globalization.BamlResourceSerializer", true);
        static Type T_BamlTreeNode = T_BamlLocalizer.Assembly.GetType("MS.Internal.Globalization.BamlTreeNode", true);
        static Type T_BamlTree = T_BamlLocalizer.Assembly.GetType("MS.Internal.Globalization.BamlTree", true);

        static MethodInfo M_SerializeTree = T_BamlResourceSerializer.GetMethod("Serialize", BF);
        static FieldInfo F_tree = T_BamlLocalizer.GetField("_tree", BF);
        static PropertyInfo P_Root = T_BamlTree.GetProperty("Root", BF);
        static PropertyInfo P_Children = T_BamlTreeNode.GetProperty("Children", BF);

        public static int Main(string[] args)
        {
            var refDir = Environment.GetEnvironmentVariable("DD_REFDIR");
            if (!string.IsNullOrEmpty(refDir))
            {
                AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
                {
                    try
                    {
                        var simple = new AssemblyName(e.Name).Name;
                        foreach (var f in Directory.GetFiles(refDir, simple + ".dll"))
                            return Assembly.LoadFrom(f);
                        foreach (var f in Directory.GetFiles(refDir, "*.dll"))
                        {
                            var an = AssemblyName.GetAssemblyName(f);
                            if (string.Equals(an.Name, simple, StringComparison.OrdinalIgnoreCase))
                                return Assembly.LoadFrom(f);
                        }
                    }
                    catch { }
                    return null;
                };
            }
            try
            {
                if (args.Length == 0) { Usage(); return 2; }
                switch (args[0])
                {
                    case "dump": return Dump(args);
                    case "trans": return Trans(args);
                    case "roundtrip": return RoundTrip(args);
                    case "res": return Resources(args);
                    case "localize": return Localize(args);
                    case "unpack": return Unpack(args);
                    case "rescheck": return ResCheck(args);
                    case "ext": return Extract(args);
                    case "list": return List(args);
                    case "us": return UsExtract(args);
                    case "usx": return UserStrings(args);
                    case "ildump": return IlDump(args);
                    default: Usage(); return 2;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR: " + ex);
                return 1;
            }
        }

        static void Usage()
        {
            Console.Error.WriteLine("BamlTool dump <in.baml> <out.txt>      列出可翻译字符串");
            Console.Error.WriteLine("BamlTool trans <in.baml> <out.baml> <dict.tsv>  按词典替换并回写 BAML");
            Console.Error.WriteLine("BamlTool roundtrip <in.baml> <out.baml> 原样往返，用于验证");
            Console.Error.WriteLine("BamlTool res <dir>                    列出目录下所有 baml");
            Console.Error.WriteLine("BamlTool localize <orig.dll> <dict.tsv> <out.dll>   翻译 BAML 并回写 DLL");
            Console.Error.WriteLine("BamlTool unpack <dll> <outdir>        导出 g.resources 里的 baml 文件");
            Console.Error.WriteLine("BamlTool rescheck <dll> [out.resources]  自检经典 resources writer");
            Console.Error.WriteLine("BamlTool us  <dll> <out.tsv>          导出代码字符串(#US)清单");
            Console.Error.WriteLine("BamlTool usx <dll> <dict.tsv> <out.dll> [scope.tsv]  替换代码字符串");
            Console.Error.WriteLine("BamlTool ildump <dll> <类型名子串> [方法名子串]   打印 IL 里的 ldstr 顺序");
        }

        // ---------- BAML 树访问 ----------

        static object LoadTree(Stream stream, out BamlLocalizer localizer)
        {
            localizer = new BamlLocalizer(stream);
            var tree = F_tree.GetValue(localizer);
            if (tree == null) throw new InvalidOperationException("BamlLocalizer 未生成 BamlTree（可能该文件不含 x:Uid 或解析失败）");
            return tree;
        }

        static IEnumerable<object> Walk(object node)
        {
            yield return node;
            var children = (IEnumerable)P_Children.GetValue(node);
            if (children == null) yield break;
            foreach (var c in children)
                foreach (var d in Walk(c))
                    yield return d;
        }

        static string GetStr(object node, string field)
        {
            var fi = node.GetType().GetField(field, BF);
            if (fi == null) return null;
            return fi.GetValue(node) as string;
        }

        static void SetStr(object node, string field, string val)
        {
            var fi = node.GetType().GetField(field, BF);
            fi.SetValue(node, val);
        }

        /// <summary>返回该节点上可翻译的「字段名」列表；不可翻译返回 null。</summary>
        static string[] TranslatableFields(object node)
        {
            switch (node.GetType().Name)
            {
                case "BamlTextNode": return new[] { "_content" };
                case "BamlLiteralContentNode": return new[] { "_literalContent" };
                case "BamlPropertyNode": return new[] { "_value" };
                case "BamlStartElementNode": return new[] { "_content" };
                case "BamlDefAttributeNode": return new[] { "_value" };
                case "BamlPresentationOptionsAttributeNode": return new[] { "_value" };
                default: return null;
            }
        }

        static bool IsTextBoxProperty(object node)
        {
            // 只替换「属性值」类节点；BamlPropertyNode 的 _value 是属性值
            return node.GetType().Name == "BamlPropertyNode";
        }

        // ---------- dump ----------

        static int Dump(string[] args)
        {
            if (args.Length < 3) { Usage(); return 2; }
            var sw = new StreamWriter(args[2], false, new UTF8Encoding(false));
            using (var fs = File.OpenRead(args[1]))
            {
                BamlLocalizer loc;
                var tree = LoadTree(fs, out loc);
                int i = 0;
                foreach (var node in Walk(P_Root.GetValue(tree))) i++;
                sw.WriteLine("elem\ttype\tprop\tfield\tvalue");
                WalkCtx(P_Root.GetValue(tree), "", sw);
                Console.WriteLine("nodes=" + i);
            }
            sw.Close();
            return 0;
        }

        /// <summary>带上下文的遍历：记录最近的 BamlStartElementNode（元素类型）和属性名。</summary>
        static void WalkCtx(object node, string elem, StreamWriter sw)
        {
            string tn = node.GetType().Name;
            string cur = elem;
            if (tn == "BamlStartElementNode" || tn == "BamlStartComplexPropertyNode"
                || tn == "BamlStartConstructorNode")
            {
                var c = GetStr(node, "_content");
                if (string.IsNullOrEmpty(c)) c = GetStr(node, "_typeFullName");
                if (!string.IsNullOrEmpty(c)) cur = c;
            }
            string prop = GetStr(node, "_propertyName");
            var fields = TranslatableFields(node);
            if (fields != null)
            {
                foreach (var f in fields)
                {
                    var s = GetStr(node, f);
                    if (string.IsNullOrEmpty(s)) continue;
                    sw.WriteLine(string.Join("\t", new[] { elem ?? "", tn, prop ?? "", f, Escape(s) }));
                }
            }
            var children = (IEnumerable)P_Children.GetValue(node);
            if (children != null)
                foreach (var c2 in children) WalkCtx(c2, cur, sw);
        }

        static bool HasLetter(string s)
        {
            foreach (var c in s) if (char.IsLetter(c)) return true;
            return false;
        }

        static string Escape(string s) =>
            s.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");

        static string Unescape(string s)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    i++;
                    switch (s[i])
                    {
                        case 'r': sb.Append('\r'); break;
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case '\\': sb.Append('\\'); break;
                        default: sb.Append('\\').Append(s[i]); break;
                    }
                }
                else sb.Append(s[i]);
            }
            return sb.ToString();
        }

        // ---------- trans ----------

        static Dictionary<string, string> LoadDict(string path)
        {
            var d = new Dictionary<string, string>();
            int line = 0;
            foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                line++;
                if (raw.Length == 0 || raw.StartsWith("#")) continue;
                var parts = raw.Split('\t');
                if (parts.Length < 2)
                {
                    Console.Error.WriteLine("dict line " + line + " 格式错误（缺少制表符）: " + raw);
                    continue;
                }
                var en = Unescape(parts[0]);
                var zh = Unescape(parts[1]);
                if (en.Length == 0) continue;
                d[en] = zh;
            }
            return d;
        }

        static int Trans(string[] args)
        {
            if (args.Length < 4) { Usage(); return 2; }
            var dict = LoadDict(args[3]);
            int hits = 0, total = 0;
            using (var fs = File.OpenRead(args[1]))
            {
                BamlLocalizer loc;
                var tree = LoadTree(fs, out loc);
                foreach (var node in Walk(P_Root.GetValue(tree)))
                {
                    var fields = TranslatableFields(node);
                    if (fields == null) continue;
                    foreach (var f in fields)
                    {
                        var s = GetStr(node, f);
                        if (string.IsNullOrEmpty(s)) continue;
                        total++;
                        string zh;
                        string key = s;
                        // 文本节点常带首尾空白，做一次 trim 匹配
                        bool trimmed = false;
                        if (!dict.TryGetValue(key, out zh) && node.GetType().Name != "BamlPropertyNode")
                        {
                            var t = s.Trim();
                            if (t != s && dict.TryGetValue(t, out zh)) { trimmed = true; key = t; }
                        }
                        if (zh == null) continue;
                        var repl = trimmed ? s.Replace(s.Trim(), zh) : zh;
                        SetStr(node, f, repl);
                        hits++;
                    }
                }
                using (var outFs = File.Create(args[2]))
                {
                    M_SerializeTree.Invoke(null, new object[] { loc, tree, outFs });
                }
            }
            Console.WriteLine("strings=" + total + " translated=" + hits);
            return 0;
        }

        // ---------- roundtrip ----------

        static int RoundTrip(string[] args)
        {
            if (args.Length < 3) { Usage(); return 2; }
            using (var fs = File.OpenRead(args[1]))
            {
                BamlLocalizer loc;
                var tree = LoadTree(fs, out loc);
                using (var outFs = File.Create(args[2]))
                    M_SerializeTree.Invoke(null, new object[] { loc, tree, outFs });
            }
            var a = new FileInfo(args[1]).Length;
            var b = new FileInfo(args[2]).Length;
            Console.WriteLine("in=" + a + " out=" + b);
            return 0;
        }

        // ---------- res ----------

        static int Resources(string[] args)
        {
            foreach (var f in Directory.GetFiles(args.Length > 1 ? args[1] : ".", "*.baml", SearchOption.AllDirectories))
                Console.WriteLine(f);
            return 0;
        }

        // ---------- 资源包读写 ----------

        const string GRES = "Display Dimmer.g.resources";

        static byte[] ExtractGResources(string dllPath)
        {
            using (var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(dllPath))
            {
                foreach (var r in asm.MainModule.Resources)
                {
                    var er = r as Mono.Cecil.EmbeddedResource;
                    if (er != null && er.Name == GRES)
                    {
                        var blob = er.GetResourceData();
                        // Cecil 读 EmbeddedResource 时已经剥掉了 4 字节长度前缀，
                        // 所以 blob 应该直接以 0xBEEFCACE 开头。这里做严格校验。
                        if (blob.Length < 4 || BitConverter.ToUInt32(blob, 0) != 0xBEEFCACE)
                            throw new BadImageFormatException(
                                GRES + " 未以 0xBEEFCACE 开头（前 4 字节=" +
                                BitConverter.ToString(blob, 0, Math.Min(4, blob.Length)) + "），说明长度前缀处理有误");
                        return blob;
                    }
                }
            }
            throw new FileNotFoundException("未找到资源 " + GRES + " in " + dllPath);
        }

        static byte[] Slice(byte[] b, int from)
        {
            var r = new byte[b.Length - from];
            Array.Copy(b, from, r, 0, r.Length);
            return r;
        }

        static Dictionary<string, byte[]> ReadResourceEntries(byte[] resBlob)
        {
            var dict = new Dictionary<string, byte[]>();
            var order = new List<string>();
            using (var ms = new MemoryStream(resBlob, false))
            using (var rr = new System.Resources.ResourceReader(ms))
            {
                foreach (System.Collections.DictionaryEntry e in rr)
                {
                    var key = (string)e.Key;
                    byte[] bytes;
                    if (e.Value is byte[])
                    {
                        bytes = (byte[])e.Value;
                    }
                    else if (e.Value is Stream)
                    {
                        using (var s = (Stream)e.Value)
                        using (var o = new MemoryStream())
                        {
                            s.CopyTo(o);
                            bytes = o.ToArray();
                        }
                    }
                    else
                    {
                        bytes = Encoding.UTF8.GetBytes(Convert.ToString(e.Value));
                    }
                    dict[key] = bytes;
                }
            }
            return dict;
        }

        static byte[] WriteResourceEntries(Dictionary<string, byte[]> entries)
        {
            return ClassicResources.Write(entries);
        }

        // ---------- ext：用官方 BamlLocalizer.ExtractResources() 列出可本地化条目 ----------
        // 用法: ext <in.dll> <out.tsv>
        // 输出列: BAML文件 / Uid / PropertyName / Category / Modifiable / Content
        // 这是 LocBaml 判定「哪些属性算用户可见文案」的权威依据，
        // 比我自己按节点类型猜可靠得多（能排除 x:Name、转换器名、样式键等）。

        static int Extract(string[] args)
        {
            var entries = ReadResourceEntries(ExtractGResources(args[1]));
            using (var sw = new StreamWriter(args[2], false, new UTF8Encoding(false)))
            {
                foreach (var kv in entries)
                {
                    if (!kv.Key.EndsWith(".baml", StringComparison.OrdinalIgnoreCase)) continue;
                    using (var ms = new MemoryStream(kv.Value, false))
                    {
                        var loc = new BamlLocalizer(ms);
                        BamlLocalizationDictionary dict;
                        try { dict = loc.ExtractResources(); }
                        catch (Exception ex)
                        {
                            sw.WriteLine("### " + kv.Key + "  提取失败: " + ex.Message);
                            continue;
                        }
                        sw.WriteLine("### " + kv.Key + "  条目=" + dict.Count);
                        foreach (System.Collections.DictionaryEntry de in dict)
                        {
                            var k = (BamlLocalizableResourceKey)de.Key;
                            var v = (BamlLocalizableResource)de.Value;
                            sw.WriteLine(string.Join("\t", new string[] {
                                k.Uid ?? "", k.PropertyName ?? "",
                                v.Category.ToString(),
                                v.Modifiable ? "M" : "-",
                                v.Readable ? "R" : "-",
                                Escape(v.Content ?? "")
                            }));
                        }
                    }
                }
            }
            Console.WriteLine("已写出 " + args[2]);
            return 0;
        }

        // ---------- us / usx：代码里的字符串常量（IL ldstr） ----------
        //
        // BAML 之外还有第二个翻译面：编译进方法体里的字符串常量（存于 #US 堆）。
        // 主窗口底部那个「Reset levels」按钮的文案就是这类 —— BAML 里根本没有，
        // 是运行时由代码赋值的。托盘菜单、动态按钮标签同理。

        static int UserStrings(string[] args)
        {
            // us  <dll> <out.tsv>   导出
            // usx <dll> <dict.tsv> <out.dll>   应用词典
            if (args.Length >= 2 && args[1].EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                && args[0] == "us")
                return UsExtract(args);

            // usx <dll> <dict.tsv> <out.dll> [scope.tsv]
            //
            // scope.tsv 是「按方法限定」的替换规则，格式：方法名子串 \t 原文 \t 译文。
            // 为什么需要它：像 "." " (" ")" " — " 这类**共享的标点/分隔串**被 UI 和
            // 诊断日志、CLI 输出同时引用（"." 全程序 66 处，横跨版本号解析、日志拼接），
            // 全局替换会把版本号、文件路径一起改烂。所以只允许在指定的界面方法里替换。
            string src = args[1], dictPath = args[2], outDll = args[3];
            var dict = LoadDict(dictPath);
            var scoped = args.Length > 4 ? LoadScoped(args[4]) : new List<ScopedRule>();
            if (scoped.Count > 0) Console.WriteLine("限定规则: " + scoped.Count);
            var hits = 0; var scopedHits = 0; var rows = new List<string>();

            using (var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(src))
            {
                foreach (var mod in asm.Modules)
                    foreach (var t in mod.GetTypes())
                        foreach (var m in t.Methods)
                        {
                            if (!m.HasBody) continue;
                            var where = t.FullName + "." + m.Name;
                            foreach (var il in m.Body.Instructions)
                            {
                                if (il.OpCode != Mono.Cecil.Cil.OpCodes.Ldstr) continue;
                                var s = il.Operand as string;
                                if (s == null) continue;
                                string zh;
                                if (dict.TryGetValue(s, out zh))
                                {
                                    il.Operand = zh; hits++;
                                    rows.Add(where + "\t" + s + "\t=> " + zh);
                                    continue;
                                }
                                foreach (var r in scoped)
                                {
                                    if (r.En != s) continue;
                                    // 规则文件里写的是 Type::Method（好读），这里要跟
                                    // t.FullName + "." + m.Name 比对，先把 :: 换成 .
                                    var mneedle = r.Method.Replace("::", ".");
                                    if (where.IndexOf(mneedle, StringComparison.Ordinal) < 0) continue;
                                    il.Operand = r.Zh; scopedHits++;
                                    rows.Add(where + "\t" + s + "\t=> " + r.Zh + "   [限定:" + r.Method + "]");
                                    break;
                                }
                            }
                        }
                var outDir = Path.GetDirectoryName(Path.GetFullPath(outDll));
                if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);
                asm.Write(outDll);
            }
            foreach (var r in rows) Console.WriteLine("  " + r);
            Console.WriteLine("代码字符串替换=" + hits + "（另有限定替换 " + scopedHits + "）-> " + outDll);
            return 0;
        }

        sealed class ScopedRule
        {
            public string Method;
            public string En;
            public string Zh;
        }

        static List<ScopedRule> LoadScoped(string path)
        {
            var list = new List<ScopedRule>();
            int line = 0;
            foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                line++;
                if (raw.Length == 0 || raw.StartsWith("#")) continue;
                var p = raw.Split('\t');
                if (p.Length < 3)
                {
                    Console.Error.WriteLine("scope line " + line + " 格式错误（需要 3 列）: " + raw);
                    continue;
                }
                list.Add(new ScopedRule { Method = Unescape(p[0]), En = Unescape(p[1]), Zh = Unescape(p[2]) });
            }
            return list;
        }

        static int UsExtract(string[] args)
        {
            var byString = new Dictionary<string, List<string>>();
            using (var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(args[1]))
            {
                foreach (var mod in asm.Modules)
                    foreach (var t in mod.GetTypes())
                        foreach (var m in t.Methods)
                        {
                            if (!m.HasBody) continue;
                            foreach (var il in m.Body.Instructions)
                            {
                                if (il.OpCode != Mono.Cecil.Cil.OpCodes.Ldstr) continue;
                                var s = il.Operand as string;
                                if (string.IsNullOrEmpty(s)) continue;
                                bool hasLetter = false;
                                foreach (var c in s) if (char.IsLetter(c)) { hasLetter = true; break; }
                                if (!hasLetter) continue;
                                List<string> lst;
                                if (!byString.TryGetValue(s, out lst)) { lst = new List<string>(); byString[s] = lst; }
                                var where = t.Name + "." + m.Name;
                                if (!lst.Contains(where)) lst.Add(where);
                            }
                        }
            }
            using (var sw = new StreamWriter(args[2], false, new UTF8Encoding(false)))
            {
                sw.WriteLine("text\tusedIn");
                foreach (var kv in byString)
                    sw.WriteLine(kv.Key + "\t" + string.Join(", ", kv.Value.ToArray()));
            }
            Console.WriteLine("代码字符串（含字母）去重=" + byString.Count + " -> " + args[2]);
            return 0;
        }

        // ---------- ildump：按顺序打印某方法的 IL（看字符串表结构用） ----------
        // 用法: ildump <dll> <类型名含> [方法名含]
        // 只打 ldstr / call / callvirt / ldc.*，够看清「字符串表是怎么拼出来的」。

        static int IlDump(string[] args)
        {
            using (var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(args[1]))
            {
                foreach (var mod in asm.Modules)
                    foreach (var t in mod.GetTypes())
                    {
                        if (args.Length > 2 && t.FullName.IndexOf(args[2], StringComparison.OrdinalIgnoreCase) < 0) continue;
                        foreach (var m in t.Methods)
                        {
                            if (args.Length > 3 && m.Name.IndexOf(args[3], StringComparison.OrdinalIgnoreCase) < 0) continue;
                            if (!m.HasBody) continue;
                            Console.WriteLine("=== " + t.FullName + "::" + m.Name + " ===");
                            foreach (var il in m.Body.Instructions)
                            {
                                var op = il.OpCode.Name;
                                if (il.Operand is string s) Console.WriteLine("    " + op + " " + Escape(s));
                                else if (il.Operand is Mono.Cecil.MethodReference mr)
                                    Console.WriteLine("    " + op + " " + mr.DeclaringType.Name + "::" + mr.Name);
                                else if (il.Operand == null)
                                    Console.WriteLine("    " + op);
                                else
                                    Console.WriteLine("    " + op + " " + il.Operand);
                            }
                        }
                    }
            }
            return 0;
        }

        // ---------- rescheck：自检手写经典格式 writer ----------
        // 用法: rescheck <in.dll> [out.resources]
        // 流程: 从 DLL 取 g.resources -> 用 ClassicResources 重写 -> 再用 .NET
        //       ResourceReader 读一遍 -> 逐条目比对。读得回来才算 writer 可用。

        static int ResCheck(string[] args)
        {
            var orig = ExtractGResources(args[1]);
            var entries = ReadResourceEntries(orig);
            Console.WriteLine("原始条目数: " + entries.Count + "，原始字节数: " + orig.Length);

            var rebuilt = WriteResourceEntries(entries);
            Console.WriteLine("重建字节数: " + rebuilt.Length + "，与原文件完全一致: " + (rebuilt.Length == orig.Length && ByteEq(rebuilt, orig)));

            if (args.Length > 2)
            {
                File.WriteAllBytes(args[2], rebuilt);
                Console.WriteLine("已写出: " + args[2]);
            }

            // 关键验证：能用 .NET 自带的 ResourceReader 读回来
            Dictionary<string, byte[]> back;
            try
            {
                back = ReadResourceEntries(rebuilt);
            }
            catch (Exception ex)
            {
                Console.WriteLine("!! 回读失败: " + ex.GetType().Name + ": " + ex.Message);
                return 1;
            }
            Console.WriteLine("回读条目数: " + back.Count);

            int bad = 0;
            foreach (var kv in entries)
            {
                byte[] v;
                if (!back.TryGetValue(kv.Key, out v)) { Console.WriteLine("  缺失: " + kv.Key); bad++; continue; }
                if (!ByteEq(v, kv.Value)) { Console.WriteLine("  内容不符: " + kv.Key); bad++; }
            }
            foreach (var kv in back)
                if (!entries.ContainsKey(kv.Key)) { Console.WriteLine("  多出: " + kv.Key); bad++; }

            Console.WriteLine(bad == 0 ? "== 自检通过 ==" : "== 自检失败，异常条目 " + bad + " ==");
            return bad == 0 ? 0 : 1;
        }

        static bool ByteEq(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        // ---------- unpack：把 g.resources 里的 baml 导成单独文件 ----------

        static int Unpack(string[] args)
        {
            var entries = ReadResourceEntries(ExtractGResources(args[1]));
            Directory.CreateDirectory(args[2]);
            foreach (var kv in entries)
            {
                if (!kv.Key.EndsWith(".baml", StringComparison.OrdinalIgnoreCase)) continue;
                var safe = kv.Key.Replace("/", "__");
                File.WriteAllBytes(Path.Combine(args[2], safe), kv.Value);
                Console.WriteLine(safe + "\t" + kv.Value.Length);
            }
            return 0;
        }

        // ---------- localize：翻译 + 回写 DLL ----------

        static int Localize(string[] args)
        {
            string origDll = args[1], dictPath = args[2], outDll = args[3];
            var dict = LoadDict(dictPath);
            Console.WriteLine("词典条目: " + dict.Count);

            var entries = ReadResourceEntries(ExtractGResources(origDll));
            int files = 0, replaced = 0, totalStrings = 0;
            var log = new List<string>();

            foreach (var key in new List<string>(entries.Keys))
            {
                if (!key.EndsWith(".baml", StringComparison.OrdinalIgnoreCase)) continue;
                files++;
                byte[] newBaml;
                int hits;
                try
                {
                    newBaml = TranslateBaml(entries[key], dict, out hits, out totalStrings);
                }
                catch (Exception ex)
                {
                    log.Add("SKIP " + key + " -> " + ex.Message);
                    continue;
                }
                entries[key] = newBaml;
                replaced += hits;
                log.Add(key + "\t命中 " + hits);
            }

            var resBlob = WriteResourceEntries(entries);

            // 注意：Mono.Cecil 的 EmbeddedResource 在写元数据时**自己会加 4 字节长度前缀**
            // （EmbeddedResource.WriteResourceData -> WriteInt32(len) + WriteBytes(data)），
            // 读的时候也只返回去掉该前缀的载荷。所以这里必须传裸 blob，
            // 否则磁盘上会变成 [外层长度][内层长度][数据]，运行时读到的是长度字段而不是
            // 0xBEEFCACE，直接 "Stream is not a valid resource file." 崩溃。

            var outDir = Path.GetDirectoryName(Path.GetFullPath(outDll));
            if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);

            using (var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(origDll))
            {
                Mono.Cecil.Resource old = null;
                foreach (var r in asm.MainModule.Resources)
                    if (r.Name == GRES) old = r;
                if (old != null) asm.MainModule.Resources.Remove(old);
                asm.MainModule.Resources.Add(new Mono.Cecil.EmbeddedResource(
                    GRES, Mono.Cecil.ManifestResourceAttributes.Private, resBlob));
                asm.Write(outDll);
            }

            foreach (var l in log) Console.WriteLine("  " + l);
            Console.WriteLine("baml 文件=" + files + " 可见字符串=" + totalStrings + " 已翻译=" + replaced);
            Console.WriteLine("输出: " + outDll + " (" + new FileInfo(outDll).Length + " bytes)");
            return 0;
        }

        static byte[] TranslateBaml(byte[] baml, Dictionary<string, string> dict, out int hits, out int total)
        {
            hits = 0; total = 0;
            using (var ms = new MemoryStream(baml, false))
            {
                BamlLocalizer loc;
                var tree = LoadTree(ms, out loc);
                TranslateNode(P_Root.GetValue(tree), "", dict, ref hits, ref total);
                using (var outMs = new MemoryStream())
                {
                    M_SerializeTree.Invoke(null, new object[] { loc, tree, outMs });
                    return outMs.ToArray();
                }
            }
        }

        // ---------- 什么算「用户可见文案」 ----------
        //
        // 注意：本应用编译时没有生成 x:Uid，所以官方 BamlLocalizer.ExtractResources()
        // 返回 0 条，无法直接用 LocBaml 那套。这里按「元素 + 属性名」自己判定。
        //
        // 踩过的坑：直接翻译所有字符串节点会把 x:Name、样式 Setter 的 Property/Value、
        // 转换器类名、绑定路径、颜色值一并改掉，界面直接崩。
        // 观察：BAML 里可见文本只出现在两类地方 ——
        //   1) 属性节点，属性名是下面这几个
        //   2) 文本节点，且父元素是承载文本的元素（Binding/Geometry/Color 这些不是）

        static readonly HashSet<string> TextProps = new HashSet<string>(StringComparer.Ordinal)
        {
            "Text", "ToolTip", "Content", "Header", "Title", "Watermark", "Placeholder",
        };

        static readonly HashSet<string> TextElements = new HashSet<string>(StringComparer.Ordinal)
        {
            "System.Windows.Controls.TextBlock",
            "System.Windows.Controls.Label",
            "System.Windows.Controls.AccessText",
            "System.Windows.Controls.Button",
            "System.Windows.Controls.CheckBox",
            "System.Windows.Controls.RadioButton",
            "System.Windows.Controls.ContentControl",
            "System.Windows.Controls.ContentPresenter",
            "System.Windows.Controls.MenuItem",
            "System.Windows.Controls.ComboBoxItem",
            "System.Windows.Controls.ListBoxItem",
            "System.Windows.Controls.ToolTip",
            "System.Windows.Documents.Hyperlink",
            "System.Windows.Documents.Run",
            "System.Windows.Documents.Span",
            "System.Windows.Documents.Bold",
            "System.Windows.Documents.Italic",
            "System.Windows.Documents.Underline",
            "System.Windows.Documents.Paragraph",
            "System.Windows.Documents.Section",
        };

        static bool IsVisibleText(string elem, string nodeType, string prop, string val)
        {
            if (string.IsNullOrWhiteSpace(val)) return false;
            // 标记扩展（{TemplateBinding ...} / {Binding ...} / {StaticResource ...}）不是文案
            if (val.Length >= 2 && val[0] == '{' && val[val.Length - 1] == '}') return false;
            // 纯符号/纯数字（项目符号 •、对勾 ✓、100%）不含字母，一律跳过
            bool hasLetter = false;
            foreach (var c in val) if (char.IsLetter(c)) { hasLetter = true; break; }
            if (!hasLetter) return false;
            if (nodeType == "BamlPropertyNode")
                return prop != null && TextProps.Contains(prop);
            if (nodeType == "BamlTextNode" || nodeType == "BamlLiteralContentNode")
                return elem != null && TextElements.Contains(elem);
            return false;   // BamlDefAttributeNode 是 x:Key，一律不动
        }

        static void TranslateNode(object node, string elem, Dictionary<string, string> dict,
                                  ref int hits, ref int total)
        {
            string tn = node.GetType().Name;
            string cur = elem;
            if (tn == "BamlStartElementNode" || tn == "BamlStartComplexPropertyNode"
                || tn == "BamlStartConstructorNode")
            {
                var c = GetStr(node, "_content");
                if (string.IsNullOrEmpty(c)) c = GetStr(node, "_typeFullName");
                if (!string.IsNullOrEmpty(c)) cur = c;
            }
            var fields = TranslatableFields(node);
            if (fields != null)
            {
                string prop = GetStr(node, "_propertyName");
                foreach (var f in fields)
                {
                    var s = GetStr(node, f);
                    if (!IsVisibleText(elem, tn, prop, s)) continue;
                    total++;
                    string zh;
                    if (dict.TryGetValue(s, out zh) && zh != null)
                    {
                        SetStr(node, f, zh); hits++;
                    }
                }
            }
            var children = (IEnumerable)P_Children.GetValue(node);
            if (children != null)
                foreach (var ch in children) TranslateNode(ch, cur, dict, ref hits, ref total);
        }

        // ---------- list：导出「待翻译」清单（已过滤） ----------
        // 用法: list <in.dll> <out.tsv>
        // 输出: baml资源键 \t 父元素 \t 属性 \t 原文

        static int List(string[] args)
        {
            var entries = ReadResourceEntries(ExtractGResources(args[1]));
            var rows = new List<string[]>();
            foreach (var kv in entries)
            {
                if (!kv.Key.EndsWith(".baml", StringComparison.OrdinalIgnoreCase)) continue;
                using (var ms = new MemoryStream(kv.Value, false))
                {
                    BamlLocalizer loc;
                    object tree;
                    try { tree = LoadTree(ms, out loc); }
                    catch (Exception ex) { Console.WriteLine("SKIP " + kv.Key + ": " + ex.Message); continue; }
                    var acc = new List<string[]>();
                    CollectNode(P_Root.GetValue(tree), "", kv.Key, acc);
                    rows.AddRange(acc);
                }
            }
            using (var sw = new StreamWriter(args[2], false, new UTF8Encoding(false)))
            {
                sw.WriteLine("baml\telement\tprop\ttext");
                foreach (var r in rows) sw.WriteLine(string.Join("\t", r));
            }
            var uniq = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in rows) uniq.Add(r[3]);
            Console.WriteLine("可见文案条目=" + rows.Count + "  去重后=" + uniq.Count);
            Console.WriteLine("已写出 " + args[2]);
            return 0;
        }

        static void CollectNode(object node, string elem, string file, List<string[]> acc)
        {
            string tn = node.GetType().Name;
            string cur = elem;
            if (tn == "BamlStartElementNode" || tn == "BamlStartComplexPropertyNode"
                || tn == "BamlStartConstructorNode")
            {
                var c = GetStr(node, "_content");
                if (string.IsNullOrEmpty(c)) c = GetStr(node, "_typeFullName");
                if (!string.IsNullOrEmpty(c)) cur = c;
            }
            var fields = TranslatableFields(node);
            if (fields != null)
            {
                string prop = GetStr(node, "_propertyName");
                foreach (var f in fields)
                {
                    var s = GetStr(node, f);
                    if (!IsVisibleText(elem, tn, prop, s)) continue;
                    acc.Add(new[] { file, elem ?? "", prop ?? "", s });
                }
            }
            var children = (IEnumerable)P_Children.GetValue(node);
            if (children != null)
                foreach (var c2 in children) CollectNode(c2, cur, file, acc);
        }
    }
}
