using System;
using System.Threading;
using LlamAcademy.Dinos.RoundManagement;
using LlamAcademy.Dinos.Session;
using UnityEngine;
using UnityEngine.UI;

namespace LlamAcademy.Dinos.UI
{
    [DefaultExecutionOrder(-40), DisallowMultipleComponent]
    public sealed class MapSelectUI : MonoBehaviour
    {
        [SerializeField] private Button OpenTropicalButton;
        [SerializeField] private Button LayeredBattlefieldButton;
        [SerializeField] private Button DefenseBattlefieldButton;
        [SerializeField] private Button DinosaurIntroductionButton;
        [SerializeField] private GameObject DinosaurIntroductionPanel;
        [SerializeField] private Button CloseIntroductionButton;
        [SerializeField] private GameSessionRestartService RestartService;
        [SerializeField] private Text StatusText;
        private static int LayoutSeedCursor = Environment.TickCount == int.MinValue
            ? 0
            : Environment.TickCount;

        private void Awake()
        {
            RestartService ??= FindFirstObjectByType<GameSessionRestartService>();
            HideDinosaurIntroduction();
            SetStatus("请选择地图");
        }

        public void LaunchOpenTropicalBattlefield() => Launch(GameMapId.OpenTropicalBattlefield);

        public void LaunchLayeredBattlefield() => Launch(GameMapId.LayeredBattlefield);

        public void LaunchDefenseBattlefield() => Launch(GameMapId.DefenseBattlefield);

        public void ShowDinosaurIntroduction()
        {
            if (DinosaurIntroductionPanel == null)
            {
                SetStatus("恐龙介绍暂不可用");
                return;
            }

            DinosaurIntroductionPanel.transform.SetAsLastSibling();
            DinosaurIntroductionPanel.SetActive(true);
            SetStatus("恐龙介绍");
        }

        public void HideDinosaurIntroduction()
        {
            if (DinosaurIntroductionPanel != null)
            {
                DinosaurIntroductionPanel.SetActive(false);
            }
        }

        public GameLaunchRequest BuildLaunchRequest(GameMapId map)
        {
            GameMapId playableMap = map.IsPlayable ? map : GameMapId.OpenTropicalBattlefield;
            return GameLaunchRequest.CreateHuman(playableMap, ConsumeNextLayoutSeed());
        }

        private void Launch(GameMapId map)
        {
            RestartService ??= FindFirstObjectByType<GameSessionRestartService>();
            if (RestartService == null)
            {
                SetStatus("无法开始：启动服务不可用");
                return;
            }

            GameLaunchRequest request = BuildLaunchRequest(map);
            bool accepted = RestartService.TryScheduleLaunch(request);
            SetStatus(accepted
                ? $"正在进入：{request.MapId.DisplayName}"
                : $"暂不可进入：{request.MapId.DisplayName}");
        }

        private void SetStatus(string value)
        {
            if (StatusText != null) StatusText.text = value;
        }

        private static int ConsumeNextLayoutSeed()
        {
            while (true)
            {
                int current = Volatile.Read(ref LayoutSeedCursor);
                int next = unchecked(current + 1);
                if (next == int.MinValue) next = unchecked(next + 1);
                if (Interlocked.CompareExchange(ref LayoutSeedCursor, next, current) == current)
                    return next;
            }
        }
    }
}
