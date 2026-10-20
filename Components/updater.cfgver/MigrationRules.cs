namespace updater.cfgver;

public static class MigrationRules
{
    public static bool Apply(Dictionary<object, object> root, List<object> rules, int targetVersion)
    {
        int current = root.TryGetValue("version", out var version) ? Convert.ToInt32(version) : 1;
        bool changed = false;
        var visited = new HashSet<int>();
        while (current != targetVersion)
        {
            if (!visited.Add(current)) throw new InvalidDataException("MIGRATION_RULE_CYCLE");
            var rule = rules.OfType<Dictionary<object, object>>().FirstOrDefault(x => x.TryGetValue("from", out var from) && Convert.ToInt32(from) == current)
                ?? throw new InvalidDataException("MIGRATION_RULE_MISSING");
            int next = Convert.ToInt32(rule["to"]);
            if (next <= current || next > targetVersion) throw new InvalidDataException("MIGRATION_RULE_VERSION");
            if (rule.TryGetValue("transformations", out var value) && value is List<object> transformations)
                foreach (var transform in transformations.OfType<Dictionary<object, object>>()) Transform(root, transform);
            current = next; root["version"] = current; changed = true;
        }
        return changed;
    }

    private static string Get(Dictionary<object, object> map, string key, string fallback)
        => map.TryGetValue(key, out var value) ? value?.ToString() ?? fallback : fallback;

    private static void Transform(Dictionary<object, object> root, Dictionary<object, object> transform)
    {
        string type = Get(transform, "type", "");
        if (type == "SplitRecycleBin")
        {
            string itemsKey = Get(transform, "itemsKey", "items"), recycleKey = Get(transform, "recycleKey", "recycleBin");
            if (!root.TryGetValue(itemsKey, out var value) || value is not List<object> items) return;
            if (!root.TryGetValue(recycleKey, out var existing)) root[recycleKey] = existing = new List<object>();
            if (existing is not List<object> recycled) throw new InvalidDataException("MIGRATION_RECYCLE_FORMAT");
            int normal = Convert.ToInt32(Get(transform, "normalValue", "0"));
            string field = Get(transform, "statusField", "status");
            foreach (var item in items.OfType<Dictionary<object, object>>().ToArray())
                if (Convert.ToInt32(Get(item, field, normal.ToString())) != normal) { recycled.Add(item); items.Remove(item); }
        }
        else if (type == "MoveAndGroup")
        {
            string target = Get(transform, "target", "");
            if (target.Length == 0 || !transform.TryGetValue("sourceFields", out var fields) || fields is not Dictionary<object, object> sources) throw new InvalidDataException("MIGRATION_GROUP_FORMAT");
            foreach (string key in new[] { "items", "recycleBin" })
            {
                if (!root.TryGetValue(key, out var collection) || collection is not List<object> items) continue;
                foreach (var item in items.OfType<Dictionary<object, object>>())
                {
                    var group = item.TryGetValue(target, out var existing) && existing is Dictionary<object, object> map ? map : new Dictionary<object, object>();
                    foreach (var source in sources)
                        if (item.Remove(source.Value, out var value)) group[source.Key] = value;
                    if (group.Count > 0) item[target] = group;
                }
            }
        }
        else throw new InvalidDataException("MIGRATION_TRANSFORMATION_UNKNOWN");
    }
}
