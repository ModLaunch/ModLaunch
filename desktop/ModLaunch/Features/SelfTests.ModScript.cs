using ModLaunch.Creator;

namespace ModLaunch.Features;

static partial class SelfTests
{
    /// <summary>Стандартная библиотека ModScript: выражения, функции Python/C++, свои функции и циклы.</summary>
    [SelfTest]
    static string ModScriptLibrary()
    {
        const string code = """
            mod "Lib test"
            game subnautica
            import math
            using namespace std
            let xs = [3, 1, 2]
            let total = sum(xs) + math.sqrt(16)
            let names = ", ".join(sorted(["b", "a"]))
            let up = "abc".upper()
            let picked = std::clamp(15, 0, 10)
            let sq = [x * x for x in range(4) if x > 0]
            let cfg = {"hp": 100, "name": "Bob"}
            let hp = cfg.hp
            let f = format("{:.2f}", pi)
            let m = re.findall("\\d+", "a1b22c333")
            def area(w, h = 2) {
                return w * h
            }
            let a = area(5)
            let n = 0
            while n < 10 {
                n += 1
                if n == 3 {
                    break
                }
            }
            let acc = 0
            for v in xs {
                if v == 1 {
                    continue
                }
                acc += v
            }
            let ok = len(xs) == 3 and not empty(xs)
            let h = sha256("x")[0:8]
            """;
        var b = ModScript.Compile(code);
        if (!b.Ok) throw new Exception(string.Join("; ", b.Diags.Select(d => $"{d.Line}: {d.Message}")));
        var vars = ModScript.Debug(code);
        var expect = new Dictionary<string, string>
        {
            ["total"] = "10", ["names"] = "a, b", ["up"] = "ABC", ["picked"] = "10", ["sq"] = "[1,4,9]", ["hp"] = "100",
            ["f"] = "3.14", ["m"] = "[\"1\",\"22\",\"333\"]", ["a"] = "10", ["n"] = "3", ["acc"] = "5", ["ok"] = "true", ["h"] = "2d711642",
        };
        var bad = expect.Where(kv => vars.GetValueOrDefault(kv.Key) != kv.Value).Select(kv => $"{kv.Key}={vars.GetValueOrDefault(kv.Key)} (ждали {kv.Value})").ToList();
        if (bad.Count > 0) throw new Exception(string.Join("; ", bad));
        var broken = Templates.All.Select(t => (t.Id, B: ModScript.Compile(t.Code))).Where(x => !x.B.Ok).Select(x => $"{x.Id}: {string.Join(", ", x.B.Diags.Select(d => d.Line + " " + d.Message))}").ToList();
        if (broken.Count > 0) throw new Exception(string.Join("; ", broken));
        return $"{ModScript.LibraryList.Count} функций, {ModScript.Library.Count} имён";
    }
}
