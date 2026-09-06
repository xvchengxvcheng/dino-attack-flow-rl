using System;

namespace LlamAcademy.Dinos.Session
{
    public readonly struct GameMapId : IEquatable<GameMapId>
    {
        public static readonly GameMapId Unknown = default;
        public static readonly GameMapId MapSelect = new("map_select", "MapSelect", "选择地图", false);
        public static readonly GameMapId OpenTropicalBattlefield =
            new("open_tropical_battlefield", "Dinos", "开阔战场", true);
        public static readonly GameMapId LayeredBattlefield =
            new("layered_battlefield", "LayeredBattlefield", "分层战场", true);
        public static readonly GameMapId DefenseBattlefield =
            new("defense_battlefield", "DefenseBattlefield", "防御战场", true);

        private GameMapId(string stableId, string sceneName, string displayName, bool isPlayable)
        {
            StableId = stableId;
            SceneName = sceneName;
            DisplayName = displayName;
            IsPlayable = isPlayable;
        }

        public string StableId { get; }
        public string SceneName { get; }
        public string DisplayName { get; }
        public bool IsPlayable { get; }
        public string SceneStableId => StableId;
        public bool IsKnown => !string.IsNullOrEmpty(StableId) && !string.IsNullOrEmpty(SceneName);

        public bool Equals(GameMapId other) =>
            string.Equals(StableId, other.StableId, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is GameMapId other && Equals(other);
        public override int GetHashCode() => StableId == null ? 0 : StringComparer.Ordinal.GetHashCode(StableId);
        public override string ToString() => StableId ?? "unknown";

        public static bool operator ==(GameMapId left, GameMapId right) => left.Equals(right);
        public static bool operator !=(GameMapId left, GameMapId right) => !left.Equals(right);

        public static bool TryFromSceneName(string sceneName, out GameMapId mapId)
        {
            if (string.Equals(sceneName, MapSelect.SceneName, StringComparison.Ordinal))
            {
                mapId = MapSelect;
                return true;
            }

            if (string.Equals(sceneName, OpenTropicalBattlefield.SceneName, StringComparison.Ordinal))
            {
                mapId = OpenTropicalBattlefield;
                return true;
            }

            if (string.Equals(sceneName, LayeredBattlefield.SceneName, StringComparison.Ordinal))
            {
                mapId = LayeredBattlefield;
                return true;
            }

            if (string.Equals(sceneName, DefenseBattlefield.SceneName, StringComparison.Ordinal))
            {
                mapId = DefenseBattlefield;
                return true;
            }

            mapId = Unknown;
            return false;
        }
    }
}
