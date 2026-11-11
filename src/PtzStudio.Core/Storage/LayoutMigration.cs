using PtzStudio.Core.Models;

namespace PtzStudio.Core.Storage;

/// <summary>
/// Quando o assistente de mapeamento descobre o número real de um botão, as funções já atribuídas devem continuar
/// no MESMO botão do desenho (por exemplo, "B continua sendo Preset 3"). Os perfis guardam números, não nomes de botão,
/// então os números dos perfis são trocados junto, de uma só vez (trocas entre dois botões também funcionam).
/// </summary>
public static class LayoutMigration
{
    public static void Apply(AppSettings s, IReadOnlyDictionary<string, int> newLayout)
    {
        var map = new Dictionary<int, int>();
        foreach (var kv in newLayout)
        {
            int old = s.Layout.TryGetValue(kv.Key, out var o) ? o : -1;
            if (old >= 0 && kv.Value >= 0 && old != kv.Value) map[old] = kv.Value;
        }
        if (map.Count > 0)
        {
            foreach (var p in s.Profiles)
                foreach (var b in p.Bindings)
                {
                    if (map.TryGetValue(b.Button, out var nb)) b.Button = nb;
                    if (b.Modifier >= 0 && map.TryGetValue(b.Modifier, out var nm)) b.Modifier = nm;
                }
        }
        foreach (var kv in newLayout) s.Layout[kv.Key] = kv.Value;
    }
}
